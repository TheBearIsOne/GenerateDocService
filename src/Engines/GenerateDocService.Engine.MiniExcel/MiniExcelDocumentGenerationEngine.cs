using System.Text;
using System.Text.Json;
using System.Collections;
using GenerateDocService.Engine.Abstractions;
using MiniExcelLibs;

namespace GenerateDocService.Engine.MiniExcel;

[DocumentEngine(
    "miniexcel",
    InputFormats = ["json"],
    OutputFormats = ["xlsx"],
    TemplateFormats = ["Excell"],
    Priority = 150)]
public sealed class MiniExcelDocumentGenerationEngine : IDocumentGenerationEngine
{
    public string Name => "miniexcel";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => string.Equals(inputFormat, "json", StringComparison.OrdinalIgnoreCase)
           && string.Equals(outputFormat, "xlsx", StringComparison.OrdinalIgnoreCase);

    public Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException($"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat}.");
        }

        var payloadText = Encoding.UTF8.GetString(request.Payload.Span);
        var values = ParseJsonPayload(payloadText);
        using var stream = new MemoryStream();
        
        if(request.Template != null)
        {
            using var templateStream = new MemoryStream(request.Template.Value.ToArray(), writable: false);
            MiniExcelLibs.MiniExcel.SaveAsByTemplate(stream,templateStream,values);
        }
        else
            MiniExcelLibs.MiniExcel.SaveAs(stream, values);

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
            return root.EnumerateArray()
                .Select(element => JsonElementToDictionary(element))
                .ToList();
        }

        // Case 2: JSON object with array property → use the first array property
        if (root.ValueKind == JsonValueKind.Object)
        {
            var firstArrayProperty = root.EnumerateObject()
                .FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array);

            if (!firstArrayProperty.Equals(default) && firstArrayProperty.Value.ValueKind == JsonValueKind.Array)
            {
                return firstArrayProperty.Value.EnumerateArray()
                    .Select(element => JsonElementToDictionary(element))
                    .ToList();
            }

            // Case 3: Single JSON object → wrap in a list
            return [JsonElementToDictionary(root)];
        }

        throw new InvalidOperationException("JSON payload must be an array of objects or a single object.");
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
