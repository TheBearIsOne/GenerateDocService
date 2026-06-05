using System.Text;
using System.Text.Json;
using DotLiquid;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.Engine.DotLiquid;

[DocumentEngine(
    "dotliquid",
    InputFormats = ["json"],
    OutputFormats = ["txt", "html", "json", "md", "markdown"],
    TemplateFormats = ["liquid", "dotliquid", "txt", "html", "md", "markdown"],
    Priority = 90)]
public sealed class DotLiquidDocumentGenerationEngine : IDocumentGenerationEngine
{
    private readonly ITemplateParserRegistry _templateParserRegistry;

    private static readonly IReadOnlySet<string> SupportedOutputFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "txt",
        "html",
        "json",
        "md",
        "markdown"
    };

    public DotLiquidDocumentGenerationEngine()
        : this(new DefaultTemplateParserRegistry(new DotLiquidTemplateParser()))
    {
    }

    public DotLiquidDocumentGenerationEngine(ITemplateParserRegistry templateParserRegistry)
    {
        _templateParserRegistry = templateParserRegistry ?? throw new ArgumentNullException(nameof(templateParserRegistry));
    }

    public string Name => "dotliquid";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => string.Equals(inputFormat, "json", StringComparison.OrdinalIgnoreCase)
           && SupportedOutputFormats.Contains(outputFormat)
           && (string.IsNullOrWhiteSpace(templateFormat)
               || string.Equals(templateFormat, "liquid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(templateFormat, "dotliquid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(templateFormat, outputFormat, StringComparison.OrdinalIgnoreCase)
               || string.Equals(templateFormat, "txt", StringComparison.OrdinalIgnoreCase));

    public async Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException($"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat} with template '{request.TemplateFormat}'.");
        }

        var payloadText = Encoding.UTF8.GetString(request.Payload.Span);
        using var document = JsonDocument.Parse(payloadText);
        var template = await ResolveTemplateAsync(request, document.RootElement, cancellationToken);
        var rendered = template.Render(Hash.FromDictionary(ToDictionary(document.RootElement, request.Metadata)));
        var content = Encoding.UTF8.GetBytes(rendered);

        return new DotLiquidGeneratedReportResult(
            request.RequestId,
            $"{request.RequestId}.{NormalizeExtension(request.OutputFormat)}",
            ResolveContentType(request.OutputFormat),
            NormalizeOutputFormat(request.OutputFormat),
            content,
            request.Metadata);
    }

    private async Task<Template> ResolveTemplateAsync(IReportRequest request, JsonElement root, CancellationToken cancellationToken)
    {
        var templateParser = _templateParserRegistry.Resolve(request.TemplateFormat ?? "liquid");

        if (request.Template is { } template && !template.IsEmpty)
        {
            var parsedTemplate = await templateParser.ParseAsync(template, cancellationToken);
            if (parsedTemplate.CompiledTemplate is Template liquidTemplate)
            {
                return liquidTemplate;
            }

            throw new InvalidOperationException($"Template parser '{templateParser.Name}' returned unsupported compiled template type '{parsedTemplate.CompiledTemplate.GetType().Name}'.");
        }

        var fallbackTemplate = root.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true })
            : root.ToString();

        var parsedFallback = await templateParser.ParseAsync(Encoding.UTF8.GetBytes(fallbackTemplate), cancellationToken);
        if (parsedFallback.CompiledTemplate is Template fallback)
        {
            return fallback;
        }

        throw new InvalidOperationException($"Template parser '{templateParser.Name}' returned unsupported compiled template type '{parsedFallback.CompiledTemplate.GetType().Name}'.");
    }

    private static string ResolveContentType(string outputFormat)
        => NormalizeOutputFormat(outputFormat) switch
        {
            "html" => "text/html",
            "json" => "application/json",
            "markdown" => "text/markdown",
            _ => "text/plain"
        };

    private static string NormalizeOutputFormat(string outputFormat)
        => string.Equals(outputFormat, "md", StringComparison.OrdinalIgnoreCase)
            ? "markdown"
            : outputFormat.ToLowerInvariant();

    private static string NormalizeExtension(string outputFormat)
        => string.Equals(outputFormat, "markdown", StringComparison.OrdinalIgnoreCase)
            ? "md"
            : outputFormat.ToLowerInvariant();

    private static Dictionary<string, object?> ToDictionary(JsonElement element, IReadOnlyDictionary<string, string> metadata)
    {
        var result = element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().ToDictionary(static property => property.Name, static property => ToValue(property.Value), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = ToValue(element)
            };

        result["metadata"] = metadata.ToDictionary(static pair => pair.Key, static pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static object? ToValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(static property => property.Name, static property => ToValue(property.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ToValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };

    private sealed class DotLiquidGeneratedReportResult(
        string requestId,
        string fileName,
        string contentType,
        string outputFormat,
        byte[] content,
        IReadOnlyDictionary<string, string> metadata) : IReportResult, IDisposable
    {
        private readonly MemoryStream _content = new(content, writable: false);

        public string RequestId { get; } = requestId;
        public string FileName { get; } = fileName;
        public string ContentType { get; } = contentType;
        public string OutputFormat { get; } = outputFormat;
        public long ContentLength { get; } = content.LongLength;
        public Stream Content => _content;
        public string? Checksum => null;
        public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

        public void Dispose()
        {
            _content.Dispose();
        }
    }

    private sealed class DefaultTemplateParserRegistry(ITemplateParser parser) : ITemplateParserRegistry
    {
        public ITemplateParser Resolve(string templateFormat)
            => parser.CanHandle(templateFormat)
                ? parser
                : throw new InvalidOperationException($"Template parser for format '{templateFormat}' is not registered.");

        public IReadOnlyCollection<ITemplateParser> GetAll() => [parser];

        public IReadOnlyCollection<ITemplateParser> FindCandidates(string templateFormat)
            => parser.CanHandle(templateFormat) ? [parser] : [];
    }
}
