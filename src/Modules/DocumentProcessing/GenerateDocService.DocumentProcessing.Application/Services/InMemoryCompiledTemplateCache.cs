using System.Collections.Concurrent;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryCompiledTemplateCache : ICompiledTemplateCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public Task<TemplateParseResult?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(cacheKey, out var entry))
        {
            if (entry.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return Task.FromResult<TemplateParseResult?>(entry.Result);
            }

            _entries.TryRemove(cacheKey, out _);
        }

        return Task.FromResult<TemplateParseResult?>(null);
    }

    public Task SetAsync(string cacheKey, TemplateParseResult result, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(ttl);
        _entries[cacheKey] = new CacheEntry(result, expiresAtUtc);
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(TemplateParseResult Result, DateTimeOffset ExpiresAtUtc);
}
