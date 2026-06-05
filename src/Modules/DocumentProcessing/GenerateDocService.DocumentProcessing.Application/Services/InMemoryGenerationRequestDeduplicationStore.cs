using System.Collections.Concurrent;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryGenerationRequestDeduplicationStore : IGenerationRequestDeduplicationStore
{
    private readonly ConcurrentDictionary<string, string> _reservations = new(StringComparer.OrdinalIgnoreCase);

    public Task<GenerationRequestReservation> TryReserveAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default)
    {
        var reservedTaskId = _reservations.GetOrAdd(fingerprint, taskId);
        return Task.FromResult(new GenerationRequestReservation(reservedTaskId, string.Equals(reservedTaskId, taskId, StringComparison.OrdinalIgnoreCase)));
    }

    public Task ReleaseAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default)
    {
        if (_reservations.TryGetValue(fingerprint, out var reservedTaskId)
            && string.Equals(reservedTaskId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            _reservations.TryRemove(fingerprint, out _);
        }

        return Task.CompletedTask;
    }
}
