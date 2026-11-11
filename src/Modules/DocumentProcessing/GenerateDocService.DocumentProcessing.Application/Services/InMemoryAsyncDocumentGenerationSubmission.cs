using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryAsyncDocumentGenerationSubmission(
    IDocumentGenerationTaskRepository repository,
    IBackgroundGenerationScheduler scheduler) : IAsyncDocumentGenerationSubmission
{
    public async Task SubmitAsync(
        DocumentGenerationTask task,
        GenerateDocumentRequested message,
        CancellationToken cancellationToken = default)
    {
        await repository.AddAsync(task, cancellationToken);

        try
        {
            await scheduler.EnqueueAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            task.MarkFailed($"Failed to enqueue document generation: {exception.Message}");
            await repository.UpdateAsync(task, CancellationToken.None);
            throw;
        }
    }
}