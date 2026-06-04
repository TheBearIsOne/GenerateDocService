using System.Collections.Concurrent;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryDocumentGenerationTaskRepository : IDocumentGenerationTaskRepository
{
    private readonly ConcurrentDictionary<string, DocumentGenerationTask> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public Task<DocumentGenerationTask> AddAsync(DocumentGenerationTask task, CancellationToken cancellationToken = default)
    {
        _tasks[task.TaskId] = task;
        return Task.FromResult(task);
    }

    public Task<DocumentGenerationTask?> GetAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue(taskId, out var task);
        return Task.FromResult(task);
    }

    public Task UpdateAsync(DocumentGenerationTask task, CancellationToken cancellationToken = default)
    {
        _tasks[task.TaskId] = task;
        return Task.CompletedTask;
    }
}
