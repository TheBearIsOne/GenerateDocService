using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class SyncDocumentGenerationService(IDocumentGenerationEngineRegistry registry)
{
    public async Task<GeneratedDocumentResult> GenerateAsync(GenerateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var engine = registry.Resolve(request.Engine);
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
