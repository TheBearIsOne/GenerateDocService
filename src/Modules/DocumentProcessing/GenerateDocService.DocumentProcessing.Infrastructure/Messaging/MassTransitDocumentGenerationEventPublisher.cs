using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using MassTransit;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Messaging;

public sealed class MassTransitDocumentGenerationEventPublisher(IBus bus) : IDocumentGenerationEventPublisher
{
    public Task PublishGeneratedAsync(DocumentGenerated message, CancellationToken cancellationToken = default)
        => bus.Publish(message, cancellationToken);

    public Task PublishFailedAsync(DocumentGenerationFailed message, CancellationToken cancellationToken = default)
        => bus.Publish(message, cancellationToken);
}
