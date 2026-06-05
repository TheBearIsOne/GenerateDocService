using System.Text.Json;
using GenerateDocService.Engine.Abstractions;
using Microsoft.Extensions.Options;
using Scriban;
using StackExchange.Redis;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Caching;

public sealed class RedisCompiledTemplateCache(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<DocumentProcessingCacheOptions> options) : ICompiledTemplateCache
{
    public async Task<TemplateParseResult?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        var database = connectionMultiplexer.GetDatabase();
        var key = BuildKey(cacheKey);
        var value = await database.StringGetAsync(key);
        if (!value.HasValue)
        {
            return null;
        }

        var cachedTemplate = JsonSerializer.Deserialize<CachedCompiledTemplate>((string)value!);
        if (cachedTemplate is null)
        {
            return null;
        }

        var compiledTemplate = Template.Parse(cachedTemplate.TemplateText);
        if (compiledTemplate.HasErrors)
        {
            throw new InvalidOperationException($"Cached Scriban template parsing failed: {string.Join("; ", compiledTemplate.Messages.Select(static message => message.Message))}");
        }

        return new TemplateParseResult(
            cachedTemplate.TemplateFormat,
            compiledTemplate,
            cachedTemplate.Metadata);
    }

    public Task SetAsync(string cacheKey, TemplateParseResult result, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (result.CompiledTemplate is not Template compiledTemplate)
        {
            return Task.CompletedTask;
        }

        var database = connectionMultiplexer.GetDatabase();
        var key = BuildKey(cacheKey);
        var cachedTemplate = new CachedCompiledTemplate(
            result.TemplateFormat,
            compiledTemplate.Page?.ToString() ?? string.Empty,
            result.Metadata);
        var effectiveTtl = ttl > TimeSpan.Zero
            ? ttl
            : TimeSpan.FromMinutes(options.Value.Redis.CompiledTemplateTtlMinutes);
        var payload = JsonSerializer.Serialize(cachedTemplate);
        return database.StringSetAsync(key, payload, effectiveTtl);
    }

    private string BuildKey(string cacheKey)
        => $"{options.Value.Redis.KeyPrefix}:compiled-templates:{cacheKey}";

    private sealed record CachedCompiledTemplate(
        string TemplateFormat,
        string TemplateText,
        IReadOnlyDictionary<string, string>? Metadata);
}
