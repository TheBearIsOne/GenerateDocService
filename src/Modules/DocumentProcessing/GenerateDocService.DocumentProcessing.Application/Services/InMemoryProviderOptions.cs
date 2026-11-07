namespace GenerateDocService.DocumentProcessing.Application.Services;

/// <summary>
/// Bounds the in-memory cache and deduplication providers so they match the Redis equivalents
/// instead of growing for the lifetime of the process.
/// </summary>
public sealed class InMemoryProviderOptions
{
    public const string SectionName = "DocumentProcessing:InMemory";

    public int GeneratedDocumentTtlMinutes { get; set; } = 60;
    public int MaxCachedDocuments { get; set; } = 1_000;
    public int DeduplicationTtlMinutes { get; set; } = 15;

    public DateTimeOffset GeneratedDocumentExpiry(DateTimeOffset now) => Expiry(now, GeneratedDocumentTtlMinutes);

    public DateTimeOffset DeduplicationExpiry(DateTimeOffset now) => Expiry(now, DeduplicationTtlMinutes);

    /// <summary>A non-positive TTL means entries never expire on their own.</summary>
    private static DateTimeOffset Expiry(DateTimeOffset now, int ttlMinutes)
        => ttlMinutes > 0 ? now.AddMinutes(ttlMinutes) : DateTimeOffset.MaxValue;
}
