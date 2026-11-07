using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence;

public sealed record ArtifactCleanupResult(int CompletedRemoved, int FailedRemoved, int StuckFailed)
{
    public bool IsEmpty => CompletedRemoved == 0 && FailedRemoved == 0 && StuckFailed == 0;
}

/// <summary>
/// Applies artifact and task retention policy in a single pass. Split out from
/// <see cref="ArtifactCleanupBackgroundService"/> so the policy is testable without a scheduler.
/// </summary>
public sealed class ArtifactRetentionCleaner(
    IDocumentGenerationTaskRepository taskRepository,
    IDocumentArtifactStore artifactStore,
    IOptions<ArtifactRetentionOptions> retentionOptions,
    ILogger<ArtifactRetentionCleaner> logger)
{
    public async Task<ArtifactCleanupResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var options = retentionOptions.Value;

        var completed = await CleanupCompletedTasksAsync(options.RetentionDays, cancellationToken);
        var failed = await CleanupFailedTasksAsync(options.FailedTaskRetentionDays, cancellationToken);
        var stuck = await ReapStuckProcessingTasksAsync(options.StuckProcessingTaskSeconds, cancellationToken);

        return new ArtifactCleanupResult(completed, failed, stuck);
    }

    private async Task<int> CleanupCompletedTasksAsync(int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var completedTasks = (await taskRepository.GetExpiredTasksAsync(cutoff, cancellationToken))
            .Where(t => t.Status == GenerationTaskStatus.Completed && !string.IsNullOrWhiteSpace(t.ResultStoragePath))
            .ToList();

        foreach (var task in completedTasks)
        {
            try
            {
                await artifactStore.DeleteAsync(task.ResultStoragePath!, cancellationToken);
                await taskRepository.DeleteAsync(task.TaskId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete artifact for task {TaskId} at {StoragePath}.",
                    task.TaskId,
                    task.ResultStoragePath);
            }
        }

        return completedTasks.Count;
    }

    private async Task<int> CleanupFailedTasksAsync(int failedRetentionDays, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-failedRetentionDays);
        var failedTasks = (await taskRepository.GetExpiredTasksAsync(cutoff, cancellationToken))
            .Where(t => t.Status == GenerationTaskStatus.Failed)
            .ToList();

        foreach (var task in failedTasks)
        {
            try
            {
                await taskRepository.DeleteAsync(task.TaskId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to delete failed task {TaskId}.", task.TaskId);
            }
        }

        return failedTasks.Count;
    }

    private async Task<int> ReapStuckProcessingTasksAsync(int stuckProcessingTaskSeconds, CancellationToken cancellationToken)
    {
        if (stuckProcessingTaskSeconds <= 0)
        {
            return 0;
        }

        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-stuckProcessingTaskSeconds);
        var stuckTasks = (await taskRepository.GetExpiredTasksAsync(cutoff, cancellationToken))
            .Where(t => t.Status == GenerationTaskStatus.Processing)
            .ToList();

        foreach (var task in stuckTasks)
        {
            try
            {
                // Failing rather than deleting gives clients a terminal state to poll and lets the
                // failed-task retention window remove the record later.
                task.MarkFailed(
                    $"Generation was abandoned: the task was still processing after {stuckProcessingTaskSeconds} seconds.");
                await taskRepository.UpdateAsync(task, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to reap stuck task {TaskId}.", task.TaskId);
            }
        }

        return stuckTasks.Count;
    }
}
