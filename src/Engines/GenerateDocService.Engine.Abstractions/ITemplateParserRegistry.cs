namespace GenerateDocService.Engine.Abstractions;

public interface ITemplateParserRegistry
{
    ITemplateParser Resolve(string templateFormat);

    IReadOnlyCollection<ITemplateParser> GetAll();

    IReadOnlyCollection<ITemplateParser> FindCandidates(string templateFormat);
}
