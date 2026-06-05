using System.Diagnostics;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class BackgroundDocumentGenerationProcessor(
    IGeneratedDocumentCache documentCache,
    IDocumentGenerationEngineRegistry registry,
    IDocumentGenerationTaskRepository repository,
    IDocumentArtifactStore artifactStore,
    IDocumentGenerationEventPublisher eventPublisher,
    DocumentGenerationMetrics metrics) : IBackgroundDocumentGenerationProcessor
{
    public async Task ProcessAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default)
    {
        var task = await repository.GetAsync(message.TaskId, cancellationToken)
            ?? throw new InvalidOperationException($"Document generation task '{message.TaskId}' was not found.");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            task.MarkProcessing();
            await repository.UpdateAsync(task, cancellationToken);

            var fingerprint = message.Metadata.TryGetValue(DocumentRequestFingerprintProvider.MetadataKey, out var fingerprintValue)
                ? fingerprintValue
                : null;

            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                var cachedDocument = await documentCache.GetAsync(fingerprint, cancellationToken);
                if (cachedDocument is not null)
                {
                    task.MarkCompleted(cachedDocument.FileName, cachedDocument.Artifact.StoragePath);
                    await repository.UpdateAsync(task, cancellationToken);

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
                        cancellationToken);

                    return;
                }
            }

            var engine = registry.Resolve(message.Engine);
            using var result = await GenerateAsync(engine, message, cancellationToken);
            var artifact = await artifactStore.SaveAsync(result, cancellationToken);

            if (!string.IsNullOrWhiteSpace(fingerprint))
            {
                await documentCache.SetAsync(
                    fingerprint,
                    CachedGeneratedDocument.FromResult(result, artifact),
                    cancellationToken);
            }

            task.MarkCompleted(result.FileName, artifact.StoragePath);
            await repository.UpdateAsync(task, cancellationToken);

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
                cancellationToken);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            metrics.RecordFailed(message.Engine, message.OutputFormat, stopwatch.Elapsed);

            task.MarkFailed(exception.Message);
            await repository.UpdateAsync(task, cancellationToken);

            await eventPublisher.PublishFailedAsync(
                new DocumentGenerationFailed(
                    message.TaskId,
                    message.CorrelationId,
                    message.RequestId,
                    message.Engine,
                    message.OutputFormat,
                    exception.Message,
                    message.Metadata,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
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
