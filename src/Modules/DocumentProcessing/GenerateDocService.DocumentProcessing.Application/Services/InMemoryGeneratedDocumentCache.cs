using System.Collections.Concurrent;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryGeneratedDocumentCache : IGeneratedDocumentCache
{
    private readonly ConcurrentDictionary<string, CachedGeneratedDocument> _documents = new(StringComparer.OrdinalIgnoreCase);

    public Task<CachedGeneratedDocument?> GetAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        _documents.TryGetValue(fingerprint, out var document);
        return Task.FromResult(document);
    }

    public Task SetAsync(string fingerprint, CachedGeneratedDocument document, CancellationToken cancellationToken = default)
    {
        _documents[fingerprint] = document;
        return Task.CompletedTask;
    }
}
