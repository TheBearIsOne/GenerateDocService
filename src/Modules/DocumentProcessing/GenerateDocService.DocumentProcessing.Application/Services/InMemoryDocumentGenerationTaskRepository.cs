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
