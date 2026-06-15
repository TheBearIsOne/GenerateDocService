using System.Collections.Concurrent;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryDocumentArtifactStore : IDocumentArtifactStore
{
    private readonly ConcurrentDictionary<string, StoredDocumentArtifact> _artifacts = new(StringComparer.OrdinalIgnoreCase);

    public Task<DocumentArtifactReference> SaveAsync(GeneratedDocumentResult document, CancellationToken cancellationToken = default)
    {
        var storagePath = $"artifacts/{document.RequestId}/{document.FileName}";
        _artifacts[storagePath] = new StoredDocumentArtifact(
            document.FileName,
            document.ContentType,
            document.OutputFormat,
            document.ToByteArray(),
            document.Checksum,
            storagePath,
            document.Metadata);

        return Task.FromResult(new DocumentArtifactReference(
            Provider: "in-memory",
            StoragePath: storagePath,
            Container: "artifacts",
            ObjectKey: $"{document.RequestId}/{document.FileName}"));
    }

    public Task<StoredDocumentArtifact?> GetAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        _artifacts.TryGetValue(storagePath, out var artifact);
        return Task.FromResult(artifact);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        _artifacts.TryRemove(storagePath, out _);
        return Task.CompletedTask;
    }
}
