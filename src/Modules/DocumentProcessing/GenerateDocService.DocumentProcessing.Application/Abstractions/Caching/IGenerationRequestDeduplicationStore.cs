using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;

public interface IGenerationRequestDeduplicationStore
{
    Task<GenerationRequestReservation> TryReserveAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default);

    Task ReleaseAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default);
}
