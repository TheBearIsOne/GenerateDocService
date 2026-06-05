using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GenerateDocService.DocumentProcessing.Infrastructure.HealthChecks;

public sealed class TcpEndpointHealthCheck(string host, int port) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            using var registration = cancellationToken.Register(static state => ((TcpClient)state!).Dispose(), client);
            await client.ConnectAsync(host, port, cancellationToken);
            return HealthCheckResult.Healthy($"TCP endpoint '{host}:{port}' is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy($"TCP endpoint '{host}:{port}' is unreachable.", exception);
        }
    }
}
