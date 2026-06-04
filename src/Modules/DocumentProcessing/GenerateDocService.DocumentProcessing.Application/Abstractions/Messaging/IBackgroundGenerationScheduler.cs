using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;

public interface IBackgroundGenerationScheduler
{
    Task<string> EnqueueAsync(IReportRequest request, CancellationToken cancellationToken = default);
}
