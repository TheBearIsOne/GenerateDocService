namespace GenerateDocService.DocumentProcessing.Infrastructure.Storage;

public sealed class DocumentProcessingStorageOptions
{
    public const string SectionName = "DocumentProcessing:Storage";

    public string Provider { get; set; } = "InMemory";

    public ObjectStorageOptions ObjectStorage { get; set; } = new();

    public bool IsObjectStorageProvider()
        => string.Equals(Provider, "ObjectStorage", StringComparison.OrdinalIgnoreCase);
}

public sealed class ObjectStorageOptions
{
    public string Endpoint { get; set; } = "http://localhost:9000";

    public string BucketName { get; set; } = "documents";

    public string AccessKey { get; set; } = "minioadmin";

    public string SecretKey { get; set; } = "minioadmin";

    public bool UseSsl { get; set; }

    public bool CreateBucketIfMissing { get; set; } = true;
}
