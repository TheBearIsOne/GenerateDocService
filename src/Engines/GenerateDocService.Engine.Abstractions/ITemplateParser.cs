namespace GenerateDocService.Engine.Abstractions;

public interface ITemplateParser
{
    string Name { get; }

    bool CanHandle(string templateFormat);

    Task<TemplateParseResult> ParseAsync(
        ReadOnlyMemory<byte> template,
        CancellationToken cancellationToken = default);
}

public sealed record TemplateParseResult(
    string TemplateFormat,
    object CompiledTemplate,
    IReadOnlyDictionary<string, string>? Metadata = null);
