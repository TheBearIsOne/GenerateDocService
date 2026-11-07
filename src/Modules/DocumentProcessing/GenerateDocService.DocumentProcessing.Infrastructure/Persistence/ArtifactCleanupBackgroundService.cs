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
            "Artifact cleanup started. Retention: {RetentionDays}d (completed), {FailedRetentionDays}d (failed). Stuck processing reaper: {StuckSeconds}s. Interval: {Interval}h",
            options.RetentionDays,
            options.FailedTaskRetentionDays,
            options.StuckProcessingTaskSeconds,
            options.CleanupIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var result = await scope.ServiceProvider
                    .GetRequiredService<ArtifactRetentionCleaner>()
                    .RunAsync(stoppingToken);

                if (!result.IsEmpty)
                {
                    logger.LogInformation(
                        "Artifact cleanup completed. Removed {CompletedCount} completed and {FailedCount} failed tasks; failed {StuckCount} abandoned task(s).",
                        result.CompletedRemoved,
                        result.FailedRemoved,
                        result.StuckFailed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Artifact cleanup failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
