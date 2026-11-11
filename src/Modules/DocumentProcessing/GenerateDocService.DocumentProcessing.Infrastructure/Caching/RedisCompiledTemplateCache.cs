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
        RedisValue value;
        try
        {
            value = await database.StringGetAsync(key);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            return null;
        }
        if (!value.HasValue)
        {
            return null;
        }

        CachedCompiledTemplate? cachedTemplate;
        try
        {
            cachedTemplate = JsonSerializer.Deserialize<CachedCompiledTemplate>((string)value!);
        }
        catch (JsonException)
        {
            return null;
        }
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
        return SetIfAvailableAsync(database, key, payload, effectiveTtl);
    }

    private static async Task SetIfAvailableAsync(IDatabase database, RedisKey key, string payload, TimeSpan ttl)
    {
        try
        {
            await database.StringSetAsync(key, payload, ttl);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
        }
    }

    private string BuildKey(string cacheKey)
        => $"{options.Value.Redis.KeyPrefix}:compiled-templates:{cacheKey}";

    private sealed record CachedCompiledTemplate(
        string TemplateFormat,
        string TemplateText,
        IReadOnlyDictionary<string, string>? Metadata);
}
