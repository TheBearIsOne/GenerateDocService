using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;

public interface IDocumentGenerationTaskRepository
{
    Task<DocumentGenerationTask> AddAsync(DocumentGenerationTask task, CancellationToken cancellationToken = default);
    Task<DocumentGenerationTask?> GetAsync(string taskId, CancellationToken cancellationToken = default);
    Task UpdateAsync(DocumentGenerationTask task, CancellationToken cancellationToken = default);
}
