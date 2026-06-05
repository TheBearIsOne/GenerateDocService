namespace GenerateDocService.DocumentProcessing.Presentation.Contracts;

public sealed record DocumentEngineHttpResponse(
    string Name,
    IReadOnlyCollection<string> InputFormats,
    IReadOnlyCollection<string> OutputFormats,
    IReadOnlyCollection<string> TemplateFormats,
    int Priority,
    string ImplementationType);
