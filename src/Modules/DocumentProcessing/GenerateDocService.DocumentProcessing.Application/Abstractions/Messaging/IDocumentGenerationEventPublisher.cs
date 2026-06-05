using GenerateDocService.DocumentProcessing.Application.Messaging;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;

public interface IDocumentGenerationEventPublisher
{
    Task PublishGeneratedAsync(DocumentGenerated message, CancellationToken cancellationToken = default);

    Task PublishFailedAsync(DocumentGenerationFailed message, CancellationToken cancellationToken = default);
}
