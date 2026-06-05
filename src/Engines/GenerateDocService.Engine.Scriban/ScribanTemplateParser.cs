using System.Text;
using System.Security.Cryptography;
using GenerateDocService.Engine.Abstractions;
using Scriban;

namespace GenerateDocService.Engine.Scriban;

public sealed class ScribanTemplateParser : ITemplateParser
{
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(30);
    private readonly ICompiledTemplateCache _compiledTemplateCache;

    public ScribanTemplateParser()
        : this(new NullCompiledTemplateCache())
    {
    }

    public ScribanTemplateParser(ICompiledTemplateCache compiledTemplateCache)
    {
        _compiledTemplateCache = compiledTemplateCache ?? throw new ArgumentNullException(nameof(compiledTemplateCache));
    }

    public string Name => "scriban";

    public bool CanHandle(string templateFormat)
        => string.Equals(templateFormat, "scriban", StringComparison.OrdinalIgnoreCase)
           || string.Equals(templateFormat, "txt", StringComparison.OrdinalIgnoreCase)
           || string.Equals(templateFormat, "html", StringComparison.OrdinalIgnoreCase)
           || string.Equals(templateFormat, "md", StringComparison.OrdinalIgnoreCase)
           || string.Equals(templateFormat, "markdown", StringComparison.OrdinalIgnoreCase);

    public Task<TemplateParseResult> ParseAsync(ReadOnlyMemory<byte> template, CancellationToken cancellationToken = default)
        => ParseCoreAsync(template, cancellationToken);

    private async Task<TemplateParseResult> ParseCoreAsync(ReadOnlyMemory<byte> template, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = CreateCacheKey(template);
        var cachedTemplate = await _compiledTemplateCache.GetAsync(cacheKey, cancellationToken);
        if (cachedTemplate is not null)
        {
            return cachedTemplate;
        }

        var templateText = Encoding.UTF8.GetString(template.Span);
        var compiledTemplate = Template.Parse(templateText);
        if (compiledTemplate.HasErrors)
        {
            throw new InvalidOperationException($"Scriban template parsing failed: {string.Join("; ", compiledTemplate.Messages.Select(static message => message.Message))}");
        }

        var result = new TemplateParseResult(
            TemplateFormat: "scriban",
            CompiledTemplate: compiledTemplate,
            Metadata: new Dictionary<string, string>
            {
                ["parser"] = Name
            });

        await _compiledTemplateCache.SetAsync(cacheKey, result, DefaultCacheTtl, cancellationToken);
        return result;
    }

    private string CreateCacheKey(ReadOnlyMemory<byte> template)
        => $"{Name}:{Convert.ToHexString(SHA256.HashData(template.Span))}";

    private sealed class NullCompiledTemplateCache : ICompiledTemplateCache
    {
        public Task<TemplateParseResult?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
            => Task.FromResult<TemplateParseResult?>(null);

        public Task SetAsync(string cacheKey, TemplateParseResult result, TimeSpan ttl, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
