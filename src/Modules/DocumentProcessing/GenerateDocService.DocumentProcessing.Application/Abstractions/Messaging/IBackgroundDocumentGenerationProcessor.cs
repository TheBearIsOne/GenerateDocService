using GenerateDocService.DocumentProcessing.Application.Messaging;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;

public interface IBackgroundDocumentGenerationProcessor
{
    Task ProcessAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default);
}
