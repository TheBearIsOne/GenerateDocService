namespace GenerateDocService.DocumentProcessing.Infrastructure.Caching;

public sealed class DocumentProcessingCacheOptions
{
    public const string SectionName = "DocumentProcessing:Caching";

    public string Provider { get; set; } = "InMemory";

    public RedisCacheOptions Redis { get; set; } = new();

    public bool IsRedisProvider()
        => string.Equals(Provider, "Redis", StringComparison.OrdinalIgnoreCase);
}

public sealed class RedisCacheOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";

    public string KeyPrefix { get; set; } = "generatedocservice";

    public int GeneratedDocumentTtlMinutes { get; set; } = 60;

    public int DeduplicationTtlMinutes { get; set; } = 15;

    public int CompiledTemplateTtlMinutes { get; set; } = 30;
}
