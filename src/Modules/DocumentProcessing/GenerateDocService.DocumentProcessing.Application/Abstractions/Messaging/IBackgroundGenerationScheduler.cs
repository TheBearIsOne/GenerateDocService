using GenerateDocService.DocumentProcessing.Application.Messaging;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;

public interface IBackgroundGenerationScheduler
{
    Task<string> EnqueueAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default);
}
