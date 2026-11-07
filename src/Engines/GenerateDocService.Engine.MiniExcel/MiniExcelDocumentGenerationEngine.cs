using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Collections;
using GenerateDocService.Engine.Abstractions;
using MiniExcelLibs;
using MiniExcelLibs.OpenXml;

namespace GenerateDocService.Engine.MiniExcel;

[DocumentEngine(
    "miniexcel",
    InputFormats = ["json"],
    OutputFormats = ["xlsx"],
    TemplateFormats = ["xlsx"],
    Priority = 150)]
public sealed class MiniExcelDocumentGenerationEngine : IDocumentGenerationEngine
{
    public string Name => "miniexcel";

    // MiniExcel formats numbers with the ambient culture unless told otherwise, so a template
    // rendered on a ru-RU host wrote 1234,56 where an invariant host wrote 1234.56. Pinning the
    // culture makes output independent of where the service happens to run.
    private static readonly OpenXmlConfiguration ExcelConfiguration = new()
    {
        Culture = CultureInfo.InvariantCulture
    };

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => string.Equals(inputFormat, "json", StringComparison.OrdinalIgnoreCase)
           && string.Equals(outputFormat, "xlsx", StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrWhiteSpace(templateFormat)
               || string.Equals(templateFormat, "xlsx", StringComparison.OrdinalIgnoreCase));

    public Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException($"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat}.");
        }

        var payloadText = Encoding.UTF8.GetString(request.Payload.Span);
        using var stream = new MemoryStream();

        if (request.Template is { } template && !template.IsEmpty)
        {
            var templateValues = ParseTemplateValues(payloadText);
            using var templateStream = new MemoryStream(template.ToArray(), writable: false);
            MiniExcelLibs.MiniExcel.SaveAsByTemplate(stream, templateStream, templateValues, ExcelConfiguration);
        }
        else
        {
            var values = ParseJsonPayload(payloadText);
            MiniExcelLibs.MiniExcel.SaveAs(stream, values, configuration: ExcelConfiguration);
        }

        // Reset position to get the full content
        stream.Position = 0;
        var content = stream.ToArray();

        return Task.FromResult<IReportResult>(new MiniExcelReportResult(
            request.RequestId,
            $"{request.RequestId}.xlsx",
            content,
            request.Metadata));
    }

    private static IEnumerable<IDictionary<string, object>> ParseJsonPayload(string payloadText)
    {
        using var document = JsonDocument.Parse(payloadText);
        var root = document.RootElement;

        // Case 1: JSON array of objects → each object becomes a row
        if (root.ValueKind == JsonValueKind.Array)
        {
            return AlignRows(root.EnumerateArray()
                .Select(element => JsonElementToTableRow(element, "$"))
                .ToList());
        }

        // Case 2: JSON object with array property → use the first array property
        if (root.ValueKind == JsonValueKind.Object)
        {
            var firstArrayProperty = root.EnumerateObject()
                .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array);

            if (!firstArrayProperty.Equals(default) && firstArrayProperty.Value.ValueKind == JsonValueKind.Array)
            {
                var arrayPath = $"$.{firstArrayProperty.Name}";
                return AlignRows(firstArrayProperty.Value.EnumerateArray()
                    .Select(element => JsonElementToTableRow(element, arrayPath))
                    .ToList());
            }

            // Case 3: Single JSON object → wrap in a list
            return AlignRows([JsonElementToTableRow(root, "$")]);
        }

        throw new InvalidOperationException("JSON payload must be an array of objects or a single object.");
    }

    /// <summary>
    /// MiniExcel derives the header row from the first item only, so a later item that introduced a
    /// key had it silently dropped and one that omitted a key threw KeyNotFoundException. Aligning
    /// every row to the union of keys makes a ragged payload readable instead of fatal.
    /// </summary>
    private static IReadOnlyList<IDictionary<string, object>> AlignRows(List<Dictionary<string, object>> rows)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key))
                {
                    columns.Add(key);
                }
            }
        }

        return rows
            .Select(row =>
            {
                IDictionary<string, object> aligned = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var column in columns)
                {
                    aligned[column] = row.TryGetValue(column, out var value) ? value : string.Empty;
                }

                return aligned;
            })
            .ToList();
    }

    private static Dictionary<string, object> JsonElementToTableRow(JsonElement element, string path)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (element.ValueKind != JsonValueKind.Object)
        {
            dict["value"] = ToCellValue(element, path);
            return dict;
        }

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = ToCellValue(property.Value, $"{path}.{property.Name}");
        }

        return dict;
    }

    private static object ToCellValue(JsonElement element, string path)
        => element.ValueKind switch
        {
            // A cell cannot hold a structure. MiniExcel would write the CLR type name into it and
            // report success, so the corruption shipped silently; failing loudly is the lesser harm.
            JsonValueKind.Object or JsonValueKind.Array => throw new InvalidOperationException(
                $"Cannot write the nested JSON value at '{path}' into a worksheet cell. Flatten it in the payload."),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };

    private static Dictionary<string, object> ParseTemplateValues(string payloadText)
    {
        using var document = JsonDocument.Parse(payloadText);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("MiniExcel templates require a JSON object as payload.");
        }

        return JsonElementToDictionary(document.RootElement);
    }

    private static Dictionary<string, object> JsonElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (element.ValueKind != JsonValueKind.Object)
        {
            dict["value"] = ToValue(element);
            return dict;
        }

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = ToValue(property.Value);
        }

        return dict;
    }

    private static object ToValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ToValue(p.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ToValue).ToArray(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };

    private sealed class MiniExcelReportResult(
        string requestId,
        string fileName,
        byte[] content,
        IReadOnlyDictionary<string, string> metadata) : IReportResult, IDisposable
    {
        private readonly MemoryStream _content = new(content, writable: false);

        public string RequestId { get; } = requestId;
        public string FileName { get; } = fileName;
        public string ContentType { get; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        public string OutputFormat { get; } = "xlsx";
        public long ContentLength { get; } = content.LongLength;
        public Stream Content => _content;
        public string? Checksum => null;
        public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

        public void Dispose()
        {
            _content.Dispose();
        }
    }
}
