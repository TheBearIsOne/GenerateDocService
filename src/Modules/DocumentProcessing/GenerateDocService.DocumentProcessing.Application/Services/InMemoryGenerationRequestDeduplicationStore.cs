using System.Collections.Concurrent;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Models;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class InMemoryGenerationRequestDeduplicationStore : IGenerationRequestDeduplicationStore
{
    private readonly ConcurrentDictionary<string, Reservation> _reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly InMemoryProviderOptions _options;
    private readonly TimeProvider _timeProvider;

    public InMemoryGenerationRequestDeduplicationStore(
        IOptions<InMemoryProviderOptions>? options = null,
        TimeProvider? timeProvider = null)
    {
        _options = options?.Value ?? new InMemoryProviderOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<GenerationRequestReservation> TryReserveAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        // Without expiry a task abandoned mid-flight would pin its fingerprint forever, which is the
        // behaviour the Redis provider already avoids through DeduplicationTtlMinutes.
        PurgeExpired(now);

        var expiresAt = _options.DeduplicationExpiry(now);
        var reservation = _reservations.AddOrUpdate(
            fingerprint,
            _ => new Reservation(taskId, expiresAt),
            (_, existing) => existing.ExpiresAtUtc > now ? existing : new Reservation(taskId, expiresAt));

        return Task.FromResult(new GenerationRequestReservation(
            reservation.TaskId,
            string.Equals(reservation.TaskId, taskId, StringComparison.OrdinalIgnoreCase)));
    }

    public Task ReleaseAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default)
    {
        if (_reservations.TryGetValue(fingerprint, out var reservation)
            && string.Equals(reservation.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
        {
            _reservations.TryRemove(new KeyValuePair<string, Reservation>(fingerprint, reservation));
        }

        return Task.CompletedTask;
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        foreach (var pair in _reservations)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _reservations.TryRemove(pair);
            }
        }
    }

    private sealed record Reservation(string TaskId, DateTimeOffset ExpiresAtUtc);
}
