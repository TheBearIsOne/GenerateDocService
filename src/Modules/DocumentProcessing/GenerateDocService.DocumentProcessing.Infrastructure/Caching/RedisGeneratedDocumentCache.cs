using System.Text.Json;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Caching;

public sealed class RedisGeneratedDocumentCache(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<DocumentProcessingCacheOptions> options) : IGeneratedDocumentCache
{
    public async Task<CachedGeneratedDocument?> GetAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        var database = connectionMultiplexer.GetDatabase();
        var key = BuildKey("documents", fingerprint);
        var value = await database.StringGetAsync(key);
        if (!value.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<CachedGeneratedDocument>((string)value!);
    }

    public Task SetAsync(string fingerprint, CachedGeneratedDocument document, CancellationToken cancellationToken = default)
    {
        var database = connectionMultiplexer.GetDatabase();
        var key = BuildKey("documents", fingerprint);
        var ttl = TimeSpan.FromMinutes(options.Value.Redis.GeneratedDocumentTtlMinutes);
        var payload = JsonSerializer.Serialize(document);

        return database.StringSetAsync(key, payload, ttl);
    }

    private string BuildKey(string category, string fingerprint)
        => $"{options.Value.Redis.KeyPrefix}:{category}:{fingerprint}";
}
