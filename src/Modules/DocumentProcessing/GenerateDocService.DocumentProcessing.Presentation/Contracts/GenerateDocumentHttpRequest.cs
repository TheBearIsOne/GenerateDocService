namespace GenerateDocService.DocumentProcessing.Presentation.Contracts;

public sealed record GenerateDocumentHttpRequest(
    string? RequestId,
    string Engine,
    string InputFormat,
    string OutputFormat,
    string? TemplateFormat,
    string Payload,
    string? Template,
    Dictionary<string, string>? Metadata);
