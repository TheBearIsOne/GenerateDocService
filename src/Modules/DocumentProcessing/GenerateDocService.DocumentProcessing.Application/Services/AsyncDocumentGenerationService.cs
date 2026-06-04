using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class AsyncDocumentGenerationService(
    IDocumentGenerationEngineRegistry registry,
    IDocumentGenerationTaskRepository repository,
    IBackgroundGenerationScheduler scheduler)
{
    public async Task<TaskStatusResponse> EnqueueAsync(GenerateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        _ = registry.Resolve(request.Engine);

        var task = new DocumentGenerationTask(
            taskId: Guid.NewGuid().ToString("N"),
            requestId: request.RequestId,
            engine: request.Engine,
            outputFormat: request.OutputFormat);

        await repository.AddAsync(task, cancellationToken);
        await scheduler.EnqueueAsync(request, cancellationToken);

        task.MarkProcessing();
        task.MarkCompleted($"{request.RequestId}.{request.OutputFormat}");
        await repository.UpdateAsync(task, cancellationToken);

        return new TaskStatusResponse(
            task.TaskId,
            task.Status,
            task.ResultFileName,
            task.Error,
            task.CreatedAtUtc,
            task.UpdatedAtUtc);
    }
}
