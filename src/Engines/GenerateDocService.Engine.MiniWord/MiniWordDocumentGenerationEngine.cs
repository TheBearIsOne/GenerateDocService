using System.Text;
using System.Text.Json;
using GenerateDocService.Engine.Abstractions;
using MiniWordEngine = MiniSoftware.MiniWord;

namespace GenerateDocService.Engine.MiniWord;

[DocumentEngine(
    "miniword",
    InputFormats = ["json"],
    OutputFormats = ["docx"],
    TemplateFormats = ["miniword", "docx"],
    Priority = 100)]
public sealed class MiniWordDocumentGenerationEngine : IDocumentGenerationEngine
{
    public string Name => "miniword";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => string.Equals(inputFormat, "json", StringComparison.OrdinalIgnoreCase)
           && string.Equals(outputFormat, "docx", StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrWhiteSpace(templateFormat)
               || string.Equals(templateFormat, "miniword", StringComparison.OrdinalIgnoreCase)
               || string.Equals(templateFormat, "docx", StringComparison.OrdinalIgnoreCase));

    public Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException($"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat}.");
        }

        var templateBytes = request.Template?.ToArray()
            ?? throw new InvalidOperationException($"Engine '{Name}' requires a .docx template.");

        var valueDictionary = ParseJsonPayload(request);

        using var outputStream = new MemoryStream();
        MiniWordEngine.SaveAsByTemplate(outputStream, templateBytes, valueDictionary);

        var content = outputStream.ToArray();

        return Task.FromResult<IReportResult>(new MiniWordReportResult(
            request.RequestId,
            $"{request.RequestId}.docx",
            content,
            request.Metadata));
    }

    private static Dictionary<string, object> ParseJsonPayload(IReportRequest request)
    {
        var payloadText = Encoding.UTF8.GetString(request.Payload.Span);
        using var document = JsonDocument.Parse(payloadText);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("MiniWord engine requires a JSON object as payload.");
        }

        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            dict[property.Name] = JsonElementToValue(property.Value);
        }

        // Add metadata as a nested dictionary for template access
        if (request.Metadata.Count > 0)
        {
            dict["metadata"] = request.Metadata.ToDictionary(
                static pair => pair.Key,
                static pair => (object)pair.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        return dict;
    }

    private static object JsonElementToValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToValue(p.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToValue).ToList(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };

    private sealed class MiniWordReportResult(
        string requestId,
        string fileName,
        byte[] content,
        IReadOnlyDictionary<string, string> metadata) : IReportResult, IDisposable
    {
        private readonly MemoryStream _content = new(content, writable: false);

        public string RequestId { get; } = requestId;
        public string FileName { get; } = fileName;
        public string ContentType { get; } = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        public string OutputFormat { get; } = "docx";
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
