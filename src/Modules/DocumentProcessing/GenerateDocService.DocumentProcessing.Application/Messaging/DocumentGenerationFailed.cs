namespace GenerateDocService.DocumentProcessing.Application.Messaging;

public sealed record DocumentGenerationFailed(
    string TaskId,
    string CorrelationId,
    string RequestId,
    string Engine,
    string OutputFormat,
    string Error,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset FailedAtUtc);
