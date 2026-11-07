using System.Diagnostics;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class BackgroundDocumentGenerationProcessor(
    IGeneratedDocumentCache documentCache,
    IDocumentGenerationEngineRegistry registry,
    IDocumentGenerationTaskRepository repository,
    IDocumentArtifactStore artifactStore,
    IDocumentGenerationEventPublisher eventPublisher,
    DocumentGenerationMetrics metrics,
    TransientFailureClassifier failureClassifier,
    IOptions<GenerationResilienceOptions> resilienceOptions,
    ILogger<BackgroundDocumentGenerationProcessor> logger) : IBackgroundDocumentGenerationProcessor
{
    public async Task ProcessAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default)
    {
        var task = await repository.GetAsync(message.TaskId, cancellationToken)
            ?? throw new InvalidOperationException($"Document generation task '{message.TaskId}' was not found.");

        var options = resilienceOptions.Value;
        var maxAttempts = Math.Max(1, options.MaxAttempts);
        var stopwatch = Stopwatch.StartNew();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.ProcessingTimeoutSeconds > 0)
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(options.ProcessingTimeoutSeconds));
        }

        task.MarkProcessing();
        await repository.UpdateAsync(task, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteAttemptAsync(task, message, stopwatch, deadline.Token);
                return;
            }
            catch (Exception exception) when (
                attempt < maxAttempts
                && !cancellationToken.IsCancellationRequested
                && failureClassifier.IsTransient(exception))
            {
                var delay = options.GetRetryDelay(attempt - 1);

                logger.LogWarning(
                    exception,
                    "Transient failure generating task {TaskId} on attempt {Attempt} of {MaxAttempts}; retrying in {DelayMs} ms",
                    message.TaskId,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                metrics.RecordFailed(message.Engine, message.OutputFormat, stopwatch.Elapsed);

                var error = BuildError(exception, deadline, cancellationToken);

                logger.LogError(
                    exception,
                    "Document generation failed for task {TaskId} after {Attempts} attempt(s)",
                    message.TaskId,
                    attempt);

                task.MarkFailed(error);
                await repository.UpdateAsync(task, cancellationToken);

                await eventPublisher.PublishFailedAsync(
                    new DocumentGenerationFailed(
                        message.TaskId,
                        message.CorrelationId,
                        message.RequestId,
                        message.Engine,
                        message.OutputFormat,
                        error,
                        message.Metadata,
                        DateTimeOffset.UtcNow),
                    cancellationToken);

                return;
            }
        }
    }

    private async Task ExecuteAttemptAsync(
        Domain.Tasks.DocumentGenerationTask task,
        GenerateDocumentRequested message,
        Stopwatch stopwatch,
        CancellationToken attemptToken)
    {
        var fingerprint = message.Metadata.TryGetValue(DocumentRequestFingerprintProvider.MetadataKey, out var fingerprintValue)
            ? fingerprintValue
            : null;

        if (!string.IsNullOrWhiteSpace(fingerprint))
        {
            var cachedDocument = await documentCache.GetAsync(fingerprint, attemptToken);
            if (cachedDocument is not null)
            {
                task.MarkCompleted(cachedDocument.FileName, cachedDocument.Artifact.StoragePath);
                await repository.UpdateAsync(task, attemptToken);

                stopwatch.Stop();
                metrics.RecordCompleted(message.Engine, cachedDocument.OutputFormat, stopwatch.Elapsed);

                await eventPublisher.PublishGeneratedAsync(
                    new DocumentGenerated(
                        message.TaskId,
                        message.CorrelationId,
                        message.RequestId,
                        message.Engine,
                        cachedDocument.OutputFormat,
                        cachedDocument.FileName,
                        cachedDocument.ContentType,
                        cachedDocument.ContentLength,
                        cachedDocument.Checksum,
                        cachedDocument.Artifact.StoragePath,
                        cachedDocument.Metadata,
                        DateTimeOffset.UtcNow),
                    attemptToken);

                return;
            }
        }

        var engine = registry.Resolve(message.Engine);
        using var result = await GenerateAsync(engine, message, attemptToken);
        var artifact = await artifactStore.SaveAsync(result, attemptToken);

        if (!string.IsNullOrWhiteSpace(fingerprint))
        {
            await documentCache.SetAsync(
                fingerprint,
                CachedGeneratedDocument.FromResult(result, artifact),
                attemptToken);
        }

        task.MarkCompleted(result.FileName, artifact.StoragePath);
        await repository.UpdateAsync(task, attemptToken);

        stopwatch.Stop();
        metrics.RecordCompleted(message.Engine, result.OutputFormat, stopwatch.Elapsed);

        await eventPublisher.PublishGeneratedAsync(
            new DocumentGenerated(
                message.TaskId,
                message.CorrelationId,
                result.RequestId,
                message.Engine,
                result.OutputFormat,
                result.FileName,
                result.ContentType,
                result.ContentLength,
                result.Checksum,
                artifact.StoragePath,
                result.Metadata,
                DateTimeOffset.UtcNow),
            attemptToken);
    }

    private static string BuildError(Exception exception, CancellationTokenSource deadline, CancellationToken callerToken)
    {
        if (!callerToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return $"Generation exceeded the processing timeout. {exception.Message}";
        }

        return exception.Message;
    }

    private static async Task<GeneratedDocumentResult> GenerateAsync(
        GenerateDocService.Engine.Abstractions.IDocumentGenerationEngine engine,
        GenerateDocumentRequested message,
        CancellationToken cancellationToken)
    {
        var request = new GenerateDocumentRequest(
            message.RequestId,
            message.Engine,
            message.InputFormat,
            message.OutputFormat,
            message.TemplateFormat,
            message.Payload,
            message.Template,
            message.Metadata);

        var result = await engine.GenerateAsync(request, cancellationToken);
        if (result is GeneratedDocumentResult typedResult)
        {
            return typedResult;
        }

        using var stream = new MemoryStream();
        await result.Content.CopyToAsync(stream, cancellationToken);

        return new GeneratedDocumentResult(
            result.RequestId,
            result.FileName,
            result.ContentType,
            result.OutputFormat,
            stream.ToArray(),
            result.Metadata);
    }
}
