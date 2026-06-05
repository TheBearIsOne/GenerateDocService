namespace GenerateDocService.DocumentProcessing.Application.Messaging;

public sealed record GenerateDocumentRequested(
    string TaskId,
    string CorrelationId,
    string RequestId,
    string Engine,
    string InputFormat,
    string OutputFormat,
    string? TemplateFormat,
    byte[] Payload,
    byte[]? Template,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset RequestedAtUtc);
