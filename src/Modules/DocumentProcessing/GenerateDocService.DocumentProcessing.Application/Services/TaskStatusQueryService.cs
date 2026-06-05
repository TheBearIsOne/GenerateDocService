using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class TaskStatusQueryService(IDocumentGenerationTaskRepository repository)
{
    public async Task<TaskStatusResponse?> GetAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var task = await repository.GetAsync(taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        return new TaskStatusResponse(
            task.TaskId,
            task.Status,
            task.ResultFileName,
            task.ResultStoragePath,
            task.Error,
            task.CreatedAtUtc,
            task.UpdatedAtUtc);
    }
}
