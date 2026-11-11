using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using GenerateDocService.DocumentProcessing.Infrastructure.Storage;

namespace GenerateDocService.DocumentProcessing.Infrastructure.HealthChecks;

public sealed class MinioBucketHealthCheck(
    IMinioClient minioClient,
    IOptions<DocumentProcessingStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var storage = options.Value.ObjectStorage;

        try
        {
            var exists = await minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(storage.BucketName),
                cancellationToken);

            if (!exists && !storage.CreateBucketIfMissing)
            {
                return HealthCheckResult.Unhealthy($"MinIO bucket '{storage.BucketName}' does not exist.");
            }

            return HealthCheckResult.Healthy(
                exists ? "MinIO bucket is available." : "MinIO is available; the bucket will be created on first write.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MinIO is unavailable or rejected the configured credentials.", exception);
        }
    }
}
