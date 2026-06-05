using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class DocumentArtifactDownloadService(
    IDocumentGenerationTaskRepository repository,
    IDocumentArtifactStore artifactStore)
{
    public async Task<Models.StoredDocumentArtifact?> GetAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var task = await repository.GetAsync(taskId, cancellationToken);
        if (task is null || task.Status != GenerationTaskStatus.Completed || string.IsNullOrWhiteSpace(task.ResultStoragePath))
        {
            return null;
        }

        return await artifactStore.GetAsync(task.ResultStoragePath, cancellationToken);
    }
}
