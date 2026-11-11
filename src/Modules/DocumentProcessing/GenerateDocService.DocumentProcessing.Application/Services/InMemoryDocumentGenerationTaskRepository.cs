using System.Collections.Concurrent;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryDocumentGenerationTaskRepository : IDocumentGenerationTaskRepository
{
    private readonly ConcurrentDictionary<string, DocumentGenerationTask> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _taskLock = new();

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

    public Task<bool> TryMarkProcessingAsync(string taskId, CancellationToken cancellationToken = default)
    {
        lock (_taskLock)
        {
            if (!_tasks.TryGetValue(taskId, out var task) || task.Status != GenerationTaskStatus.Queued)
            {
                return Task.FromResult(false);
            }

            task.MarkProcessing();
            return Task.FromResult(true);
        }
    }

    public Task UpdateAsync(DocumentGenerationTask task, CancellationToken cancellationToken = default)
    {
        _tasks[task.TaskId] = task;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryRemove(taskId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentGenerationTask>> GetExpiredTasksAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        var expired = _tasks.Values
            .Where(t => t.UpdatedAtUtc.HasValue && t.UpdatedAtUtc.Value < olderThan)
            .ToList();

        return Task.FromResult<IReadOnlyList<DocumentGenerationTask>>(expired);
    }
}
