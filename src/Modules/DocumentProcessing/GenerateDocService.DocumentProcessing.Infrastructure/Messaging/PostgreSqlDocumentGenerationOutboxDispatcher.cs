using System.Text.Json;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.DocumentProcessing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Messaging;

public sealed class PostgreSqlDocumentGenerationOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<PostgreSqlDocumentGenerationOutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxDispatchAttempts = 10;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DispatchLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RedeliveryInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (dispatched == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Document generation outbox dispatch pass failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentGenerationDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundGenerationScheduler>();
        var now = DateTimeOffset.UtcNow;
        var redeliverBefore = now.Subtract(RedeliveryInterval);

        var candidates = await dbContext.DocumentGenerationTasks
            .AsNoTracking()
            .Where(task => task.Status == GenerationTaskStatus.Queued
                && task.PendingMessageJson != null
            && (task.MessageSentAtUtc == null || task.MessageSentAtUtc <= redeliverBefore)
                && (task.DispatchLockUntilUtc == null || task.DispatchLockUntilUtc <= now))
            .OrderBy(task => task.CreatedAtUtc)
            .Select(task => task.TaskId)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var dispatched = 0;
        foreach (var taskId in candidates)
        {
            var lockId = Guid.NewGuid();
            var lockUntil = DateTimeOffset.UtcNow.Add(DispatchLease);
            var claimed = await dbContext.DocumentGenerationTasks
                .Where(task => task.TaskId == taskId
                    && task.Status == GenerationTaskStatus.Queued
                    && task.PendingMessageJson != null
                    && (task.DispatchLockUntilUtc == null || task.DispatchLockUntilUtc <= DateTimeOffset.UtcNow))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(task => task.DispatchLockId, (Guid?)lockId)
                    .SetProperty(task => task.DispatchLockUntilUtc, (DateTimeOffset?)lockUntil), cancellationToken);

            if (claimed != 1)
            {
                continue;
            }

            var pending = await dbContext.DocumentGenerationTasks
                .AsNoTracking()
                .Where(task => task.TaskId == taskId && task.DispatchLockId == lockId)
                .Select(task => new PendingDispatch(task.PendingMessageJson!, task.DispatchAttempts))
                .SingleOrDefaultAsync(cancellationToken);

            if (pending is null)
            {
                continue;
            }

            try
            {
                var message = JsonSerializer.Deserialize<GenerateDocumentRequested>(pending.MessageJson)
                    ?? throw new JsonException("The outbox message deserialized to null.");

                await scheduler.EnqueueAsync(message, cancellationToken);
                await MarkDispatchedAsync(dbContext, taskId, lockId, cancellationToken);
                dispatched++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordDispatchFailureAsync(dbContext, taskId, lockId, pending.Attempts + 1, exception, cancellationToken);
            }
        }

        return dispatched;
    }

    private static Task<int> MarkDispatchedAsync(
        DocumentGenerationDbContext dbContext,
        string taskId,
        Guid lockId,
        CancellationToken cancellationToken)
        => dbContext.DocumentGenerationTasks
            .Where(task => task.TaskId == taskId && task.DispatchLockId == lockId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(task => task.MessageSentAtUtc, DateTimeOffset.UtcNow)
                .SetProperty(task => task.DispatchAttempts, 0)
                .SetProperty(task => task.DispatchLockId, (Guid?)null)
                .SetProperty(task => task.DispatchLockUntilUtc, (DateTimeOffset?)null), cancellationToken);

    private async Task RecordDispatchFailureAsync(
        DocumentGenerationDbContext dbContext,
        string taskId,
        Guid lockId,
        int attempts,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (attempts >= MaxDispatchAttempts)
        {
            await dbContext.DocumentGenerationTasks
                .Where(task => task.TaskId == taskId && task.DispatchLockId == lockId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(task => task.Status, GenerationTaskStatus.Failed)
                    .SetProperty(task => task.Error, $"Failed to dispatch generation request after {attempts} attempts: {exception.Message}")
                    .SetProperty(task => task.PendingMessageJson, (string?)null)
                    .SetProperty(task => task.DispatchAttempts, attempts)
                    .SetProperty(task => task.DispatchLockId, (Guid?)null)
                    .SetProperty(task => task.DispatchLockUntilUtc, (DateTimeOffset?)null)
                    .SetProperty(task => task.UpdatedAtUtc, DateTimeOffset.UtcNow), cancellationToken);

            logger.LogError(exception, "Outbox dispatch exhausted {Attempts} attempts for task {TaskId}", attempts, taskId);
            return;
        }

        var retryDelay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempts)));
        await dbContext.DocumentGenerationTasks
            .Where(task => task.TaskId == taskId && task.DispatchLockId == lockId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(task => task.DispatchAttempts, attempts)
                .SetProperty(task => task.DispatchLockId, (Guid?)null)
                .SetProperty(task => task.DispatchLockUntilUtc, DateTimeOffset.UtcNow.Add(retryDelay)), cancellationToken);

        logger.LogWarning(exception, "Outbox dispatch attempt {Attempt} failed for task {TaskId}; retrying after {Delay}", attempts, taskId, retryDelay);
    }

    private sealed record PendingDispatch(string MessageJson, int Attempts);
}
