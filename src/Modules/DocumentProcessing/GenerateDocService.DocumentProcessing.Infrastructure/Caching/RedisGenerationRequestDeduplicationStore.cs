using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Caching;

public sealed class RedisGenerationRequestDeduplicationStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<DocumentProcessingCacheOptions> options) : IGenerationRequestDeduplicationStore
{
    public async Task<GenerationRequestReservation> TryReserveAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default)
    {
        var database = connectionMultiplexer.GetDatabase();
        var key = BuildKey(fingerprint);
        var ttl = TimeSpan.FromMinutes(options.Value.Redis.DeduplicationTtlMinutes);
        var acquired = await database.StringSetAsync(key, taskId, ttl, when: When.NotExists);
        if (acquired)
        {
            return new GenerationRequestReservation(taskId, true);
        }

        var existingTaskId = await database.StringGetAsync(key);
        return new GenerationRequestReservation(existingTaskId!, false);
    }

    public async Task ReleaseAsync(string fingerprint, string taskId, CancellationToken cancellationToken = default)
    {
        var database = connectionMultiplexer.GetDatabase();
        var key = BuildKey(fingerprint);
        var existingTaskId = await database.StringGetAsync(key);
        if (existingTaskId.HasValue && string.Equals(existingTaskId!, taskId, StringComparison.OrdinalIgnoreCase))
        {
            await database.KeyDeleteAsync(key);
        }
    }

    private string BuildKey(string fingerprint)
        => $"{options.Value.Redis.KeyPrefix}:dedup:{fingerprint}";
}
