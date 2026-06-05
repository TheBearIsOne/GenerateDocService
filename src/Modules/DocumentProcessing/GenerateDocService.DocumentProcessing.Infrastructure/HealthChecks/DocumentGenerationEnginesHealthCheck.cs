using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GenerateDocService.DocumentProcessing.Infrastructure.HealthChecks;

public sealed class DocumentGenerationEnginesHealthCheck(IDocumentGenerationEngineRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var descriptors = registry.GetDescriptors();
        if (descriptors.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("No document generation engines are registered."));
        }

        var data = descriptors.ToDictionary(
            descriptor => descriptor.Name,
            descriptor => (object)$"{descriptor.ImplementationType.Name} ({string.Join(",", descriptor.OutputFormats)})",
            StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(HealthCheckResult.Healthy("Document generation engines are registered.", data));
    }
}
