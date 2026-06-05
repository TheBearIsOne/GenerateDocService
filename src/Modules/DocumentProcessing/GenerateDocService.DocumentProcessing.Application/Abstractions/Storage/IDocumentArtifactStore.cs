using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;

public interface IDocumentArtifactStore
{
    Task<DocumentArtifactReference> SaveAsync(GeneratedDocumentResult document, CancellationToken cancellationToken = default);

    Task<StoredDocumentArtifact?> GetAsync(string storagePath, CancellationToken cancellationToken = default);
}
