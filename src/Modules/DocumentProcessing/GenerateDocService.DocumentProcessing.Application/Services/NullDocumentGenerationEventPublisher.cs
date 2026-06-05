using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class NullDocumentGenerationEventPublisher : IDocumentGenerationEventPublisher
{
    public Task PublishGeneratedAsync(DocumentGenerated message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task PublishFailedAsync(DocumentGenerationFailed message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
