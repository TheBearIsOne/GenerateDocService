using System.Text;
using System.Text.Json;
using GenerateDocService.Engine.Abstractions;
using Scriban;
using Scriban.Runtime;

namespace GenerateDocService.Engine.Scriban;

[DocumentEngine(
    "scriban",
    InputFormats = ["json"],
    OutputFormats = ["txt", "html", "json", "md", "markdown"],
    TemplateFormats = ["scriban", "txt", "html", "md", "markdown"],
    Priority = 100)]
public sealed class ScribanDocumentGenerationEngine : IDocumentGenerationEngine
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

    public ScribanDocumentGenerationEngine()
        : this(new DefaultTemplateParserRegistry(new ScribanTemplateParser()))
    {
    }

    public ScribanDocumentGenerationEngine(ITemplateParserRegistry templateParserRegistry)
    {
        _templateParserRegistry = templateParserRegistry ?? throw new ArgumentNullException(nameof(templateParserRegistry));
    }

    public string Name => "scriban";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => string.Equals(inputFormat, "json", StringComparison.OrdinalIgnoreCase)
           && SupportedOutputFormats.Contains(outputFormat)
           && (string.IsNullOrWhiteSpace(templateFormat)
               || string.Equals(templateFormat, "scriban", StringComparison.OrdinalIgnoreCase)
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

        var scriptObject = new ScriptObject();
        Populate(scriptObject, document.RootElement);
        scriptObject.Add("metadata", request.Metadata.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase));

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var rendered = await template.RenderAsync(context);
        var content = Encoding.UTF8.GetBytes(rendered);

        return new ScribanGeneratedReportResult(
            request.RequestId,
            $"{request.RequestId}.{NormalizeExtension(request.OutputFormat)}",
            ResolveContentType(request.OutputFormat),
            NormalizeOutputFormat(request.OutputFormat),
            content,
            request.Metadata);
    }

    private async Task<Template> ResolveTemplateAsync(IReportRequest request, JsonElement root, CancellationToken cancellationToken)
    {
        var templateParser = _templateParserRegistry.Resolve(request.TemplateFormat ?? "scriban");

        if (request.Template is { } template && !template.IsEmpty)
        {
            var parsedTemplate = await templateParser.ParseAsync(template, cancellationToken);
            if (parsedTemplate.CompiledTemplate is Template scribanTemplate)
            {
                return scribanTemplate;
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

    private static void Populate(ScriptObject scriptObject, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            scriptObject.Add("value", ToValue(element));
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            scriptObject.Add(property.Name, ToValue(property.Value));
        }
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

    private sealed class ScribanGeneratedReportResult(
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
