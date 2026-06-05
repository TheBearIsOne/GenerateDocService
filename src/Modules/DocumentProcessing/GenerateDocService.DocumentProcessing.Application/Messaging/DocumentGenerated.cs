namespace GenerateDocService.DocumentProcessing.Application.Messaging;

public sealed record DocumentGenerated(
    string TaskId,
    string CorrelationId,
    string RequestId,
    string Engine,
    string OutputFormat,
    string FileName,
    string ContentType,
    long ContentLength,
    string? Checksum,
    string StoragePath,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CompletedAtUtc);
