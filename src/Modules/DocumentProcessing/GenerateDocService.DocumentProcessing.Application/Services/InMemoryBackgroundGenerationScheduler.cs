using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryBackgroundGenerationScheduler : IBackgroundGenerationScheduler
{
    public Task<string> EnqueueAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(request.RequestId);
    }
}
