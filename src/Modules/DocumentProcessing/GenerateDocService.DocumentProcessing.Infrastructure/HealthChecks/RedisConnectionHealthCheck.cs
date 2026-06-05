using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace GenerateDocService.DocumentProcessing.Infrastructure.HealthChecks;

public sealed class RedisConnectionHealthCheck(IConnectionMultiplexer connectionMultiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = connectionMultiplexer.GetDatabase();
            await database.PingAsync();
            return HealthCheckResult.Healthy("Redis connection is available.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Redis connection is unavailable.", exception);
        }
    }
}
