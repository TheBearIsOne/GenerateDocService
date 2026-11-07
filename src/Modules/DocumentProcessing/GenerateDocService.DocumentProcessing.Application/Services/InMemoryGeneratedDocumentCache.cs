using System.Collections.Concurrent;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Models;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryGeneratedDocumentCache : IGeneratedDocumentCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly InMemoryProviderOptions _options;
    private readonly TimeProvider _timeProvider;

    public InMemoryGeneratedDocumentCache(
        IOptions<InMemoryProviderOptions>? options = null,
        TimeProvider? timeProvider = null)
    {
        _options = options?.Value ?? new InMemoryProviderOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<CachedGeneratedDocument?> GetAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        if (_documents.TryGetValue(fingerprint, out var entry))
        {
            if (entry.ExpiresAtUtc > now)
            {
                return Task.FromResult<CachedGeneratedDocument?>(entry.Document);
            }

            _documents.TryRemove(new KeyValuePair<string, CacheEntry>(fingerprint, entry));
        }

        return Task.FromResult<CachedGeneratedDocument?>(null);
    }

    public Task SetAsync(string fingerprint, CachedGeneratedDocument document, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        _documents[fingerprint] = new CacheEntry(document, now, _options.GeneratedDocumentExpiry(now));

        Evict(now);

        return Task.CompletedTask;
    }

    private void Evict(DateTimeOffset now)
    {
        foreach (var pair in _documents)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _documents.TryRemove(pair);
            }
        }

        var limit = _options.MaxCachedDocuments;
        var excess = _documents.Count - limit;
        if (limit <= 0 || excess <= 0)
        {
            return;
        }

        foreach (var oldest in _documents.OrderBy(pair => pair.Value.StoredAtUtc).Take(excess).ToArray())
        {
            _documents.TryRemove(oldest);
        }
    }

    private sealed record CacheEntry(
        CachedGeneratedDocument Document,
        DateTimeOffset StoredAtUtc,
        DateTimeOffset ExpiresAtUtc);
}
