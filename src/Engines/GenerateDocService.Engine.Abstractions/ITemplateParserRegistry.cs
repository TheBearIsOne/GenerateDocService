namespace GenerateDocService.Engine.Abstractions;

public interface ITemplateParserRegistry
{
    /// <summary>
    /// Resolves the parser for a template format. Several parsers can claim the same format, so
    /// <paramref name="preferredParserName"/> lets the requesting engine break the tie; without it
    /// the choice falls back to parser name order, which is arbitrary from a caller's point of view.
    /// </summary>
    ITemplateParser Resolve(string templateFormat, string? preferredParserName = null);

    IReadOnlyCollection<ITemplateParser> GetAll();

    IReadOnlyCollection<ITemplateParser> FindCandidates(string templateFormat);
}
