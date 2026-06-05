using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class AsyncDocumentGenerationService(
    IGenerationRequestDeduplicationStore deduplicationStore,
    IDocumentGenerationEngineRegistry registry,
    IDocumentGenerationTaskRepository repository,
    IBackgroundGenerationScheduler scheduler,
    DocumentGenerationMetrics metrics)
{
    public async Task<TaskStatusResponse> EnqueueAsync(GenerateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var resolvedRequest = ResolveRequest(request);
        metrics.RecordAsyncRequest(resolvedRequest.Engine, resolvedRequest.OutputFormat);

        var fingerprint = DocumentRequestFingerprintProvider.Create(resolvedRequest);
        var taskId = Guid.NewGuid().ToString("N");
        var reservation = await deduplicationStore.TryReserveAsync(fingerprint, taskId, cancellationToken);

        if (!reservation.IsOwner)
        {
            var existingTask = await repository.GetAsync(reservation.TaskId, cancellationToken);
            if (existingTask is not null && existingTask.Status is not GenerationTaskStatus.Failed)
            {
                return Map(existingTask);
            }

            await deduplicationStore.ReleaseAsync(fingerprint, reservation.TaskId, cancellationToken);
            reservation = await deduplicationStore.TryReserveAsync(fingerprint, taskId, cancellationToken);

            if (!reservation.IsOwner)
            {
                var retriedTask = await repository.GetAsync(reservation.TaskId, cancellationToken);
                if (retriedTask is not null)
                {
                    return Map(retriedTask);
                }
            }
        }

        var enrichedRequest = resolvedRequest.WithMetadata(DocumentRequestFingerprintProvider.MetadataKey, fingerprint);

        var task = new DocumentGenerationTask(
            taskId: reservation.TaskId,
            requestId: enrichedRequest.RequestId,
            engine: enrichedRequest.Engine,
            outputFormat: enrichedRequest.OutputFormat);

        try
        {
            await repository.AddAsync(task, cancellationToken);
            await scheduler.EnqueueAsync(ToMessage(task.TaskId, enrichedRequest), cancellationToken);
        }
        catch
        {
            await deduplicationStore.ReleaseAsync(fingerprint, task.TaskId, cancellationToken);
            throw;
        }

        return Map(task);
    }

    private GenerateDocumentRequest ResolveRequest(GenerateDocumentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Engine))
        {
            _ = registry.Resolve(request.Engine);
            return request;
        }

        var candidate = registry.FindCandidateDescriptors(request.InputFormat, request.OutputFormat, request.TemplateFormat)
            .FirstOrDefault();

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No document generation engine can handle {request.InputFormat} -> {request.OutputFormat} with template '{request.TemplateFormat ?? "none"}'.");
        }

        return request.WithEngine(candidate.Name);
    }

    private static GenerateDocumentRequested ToMessage(string taskId, GenerateDocumentRequest request)
    {
        var correlationId = request.Metadata.TryGetValue("correlationId", out var value)
            ? value
            : request.RequestId;

        return new GenerateDocumentRequested(
            taskId,
            correlationId,
            request.RequestId,
            request.Engine,
            request.InputFormat,
            request.OutputFormat,
            request.TemplateFormat,
            request.Payload.ToArray(),
            request.Template?.ToArray(),
            new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
    }

    private static TaskStatusResponse Map(DocumentGenerationTask task)
        => new(
            task.TaskId,
            task.Status,
            task.ResultFileName,
            task.ResultStoragePath,
            task.Error,
            task.CreatedAtUtc,
            task.UpdatedAtUtc);
}
