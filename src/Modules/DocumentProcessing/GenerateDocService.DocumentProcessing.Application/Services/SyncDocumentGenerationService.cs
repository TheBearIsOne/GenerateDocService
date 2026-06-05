using System.Diagnostics;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class SyncDocumentGenerationService(
    IDocumentGenerationEngineRegistry registry,
    DocumentGenerationMetrics metrics)
{
    public async Task<GeneratedDocumentResult> GenerateAsync(GenerateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var resolvedRequest = ResolveRequest(request);
        metrics.RecordSyncRequest(resolvedRequest.Engine, resolvedRequest.OutputFormat);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var engine = registry.Resolve(resolvedRequest.Engine);
            var result = await engine.GenerateAsync(resolvedRequest, cancellationToken);

            GeneratedDocumentResult generatedResult;
            if (result is GeneratedDocumentResult typedResult)
            {
                generatedResult = typedResult;
            }
            else
            {
                using var stream = new MemoryStream();
                await result.Content.CopyToAsync(stream, cancellationToken);

                generatedResult = new GeneratedDocumentResult(
                    result.RequestId,
                    result.FileName,
                    result.ContentType,
                    result.OutputFormat,
                    stream.ToArray(),
                    result.Metadata);
            }

            stopwatch.Stop();
            metrics.RecordCompleted(resolvedRequest.Engine, resolvedRequest.OutputFormat, stopwatch.Elapsed);

            return generatedResult;
        }
        catch
        {
            stopwatch.Stop();
            metrics.RecordFailed(resolvedRequest.Engine, resolvedRequest.OutputFormat, stopwatch.Elapsed);
            throw;
        }
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
}
