namespace GenerateDocService.DocumentProcessing.Presentation.Contracts;

/// <summary>
/// JSON cannot carry raw bytes, so binary payloads and templates are supplied as base64 in
/// <see cref="PayloadBase64"/> and <see cref="TemplateBase64"/>. Each pair is mutually exclusive:
/// a request sets either the text field or the base64 field, never both.
/// </summary>
public sealed record GenerateDocumentHttpRequest(
    string? RequestId,
    string? Engine,
    string InputFormat,
    string OutputFormat,
    string? TemplateFormat,
    string? Payload,
    string? Template,
    Dictionary<string, string>? Metadata,
    string? PayloadBase64 = null,
    string? TemplateBase64 = null);
