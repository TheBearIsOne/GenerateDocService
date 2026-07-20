using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence;

public sealed class ArtifactCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ArtifactRetentionOptions> retentionOptions,
    ILogger<ArtifactCleanupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = retentionOptions.Value;
        var interval = TimeSpan.FromHours(options.CleanupIntervalHours);

        logger.LogInformation(
            "Artifact cleanup started. Retention: {RetentionDays}d (completed), {FailedRetentionDays}d (failed). Interval: {Interval}h",
            options.RetentionDays,
            options.FailedTaskRetentionDays,
            options.CleanupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var taskRepository = scope.ServiceProvider.GetRequiredService<IDocumentGenerationTaskRepository>();
                var artifactStore = scope.ServiceProvider.GetRequiredService<IDocumentArtifactStore>();

                var completedCount = await CleanupCompletedTasksAsync(taskRepository, artifactStore, options.RetentionDays, stoppingToken);
                var failedCount = await CleanupFailedTasksAsync(taskRepository, options.FailedTaskRetentionDays, stoppingToken);

                if (completedCount > 0 || failedCount > 0)
                {
                    logger.LogInformation(
                        "Artifact cleanup completed. Removed {CompletedCount} completed and {FailedCount} failed tasks.",
                        completedCount,
                        failedCount);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Artifact cleanup failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task<int> CleanupCompletedTasksAsync(
        IDocumentGenerationTaskRepository taskRepository,
        IDocumentArtifactStore artifactStore,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        var expiredTasks = await taskRepository.GetExpiredTasksAsync(cutoff, cancellationToken);

        var completedTasks = expiredTasks
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

    private async Task<int> CleanupFailedTasksAsync(
        IDocumentGenerationTaskRepository taskRepository,
        int failedRetentionDays,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-failedRetentionDays);
        var expiredTasks = await taskRepository.GetExpiredTasksAsync(cutoff, cancellationToken);

        var failedTasks = expiredTasks
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
                logger.LogWarning(
                    exception,
                    "Failed to delete failed task {TaskId}.",
                    task.TaskId);
            }
        }

        return failedTasks.Count;
    }
}
