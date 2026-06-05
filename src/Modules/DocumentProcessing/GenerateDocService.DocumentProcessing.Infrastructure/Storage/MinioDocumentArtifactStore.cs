using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.Models;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Storage;

public sealed class MinioDocumentArtifactStore(
    IMinioClient minioClient,
    IOptions<DocumentProcessingStorageOptions> options) : IDocumentArtifactStore
{
    public async Task<DocumentArtifactReference> SaveAsync(GeneratedDocumentResult document, CancellationToken cancellationToken = default)
    {
        var bucketName = options.Value.ObjectStorage.BucketName;
        if (options.Value.ObjectStorage.CreateBucketIfMissing)
        {
            await EnsureBucketExistsAsync(bucketName, cancellationToken);
        }

        var objectKey = $"{document.RequestId}/{document.FileName}";
        var content = document.ToByteArray();
        await using var stream = new MemoryStream(content, writable: false);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithStreamData(stream)
            .WithObjectSize(content.LongLength)
            .WithContentType(document.ContentType);

        await minioClient.PutObjectAsync(putObjectArgs, cancellationToken);

        return new DocumentArtifactReference(
            Provider: "minio",
            StoragePath: $"{bucketName}/{objectKey}",
            Container: bucketName,
            ObjectKey: objectKey);
    }

    public async Task<StoredDocumentArtifact?> GetAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var separatorIndex = storagePath.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == storagePath.Length - 1)
        {
            return null;
        }

        var bucketName = storagePath[..separatorIndex];
        var objectKey = storagePath[(separatorIndex + 1)..];

        await using var stream = new MemoryStream();

        var stat = await minioClient.StatObjectAsync(
            new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey),
            cancellationToken);

        await minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectKey)
                .WithCallbackStream(sourceStream => sourceStream.CopyTo(stream)),
            cancellationToken);

        var fileName = Path.GetFileName(objectKey);
        var outputFormat = Path.GetExtension(fileName).TrimStart('.');

        return new StoredDocumentArtifact(
            fileName,
            stat.ContentType ?? "application/octet-stream",
            outputFormat,
            stream.ToArray(),
            stat.ETag,
            storagePath);
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken cancellationToken)
    {
        var bucketExists = await minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucketName),
            cancellationToken);

        if (bucketExists)
        {
            return;
        }

        await minioClient.MakeBucketAsync(
            new MakeBucketArgs().WithBucket(bucketName),
            cancellationToken);
    }
}
