using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryBackgroundGenerationScheduler(IBackgroundDocumentGenerationProcessor processor) : IBackgroundGenerationScheduler
{
    public Task<string> EnqueueAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default)
    {
        _ = Task.Run(() => processor.ProcessAsync(message, CancellationToken.None), CancellationToken.None);
        return Task.FromResult(message.TaskId);
    }
}
