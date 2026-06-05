using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;

public interface IGeneratedDocumentCache
{
    Task<CachedGeneratedDocument?> GetAsync(string fingerprint, CancellationToken cancellationToken = default);

    Task SetAsync(string fingerprint, CachedGeneratedDocument document, CancellationToken cancellationToken = default);
}
