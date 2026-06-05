using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class TemplateParserRegistry : ITemplateParserRegistry
{
    private readonly IReadOnlyCollection<ITemplateParser> _parsers;
    private readonly IReadOnlyDictionary<string, ITemplateParser> _parserMap;

    public TemplateParserRegistry(IEnumerable<ITemplateParser> parsers)
    {
        _parsers = parsers.ToArray();
        _parserMap = BuildParserMap(_parsers);
    }

    public ITemplateParser Resolve(string templateFormat)
    {
        var candidates = FindCandidates(templateFormat);
        if (candidates.Count > 0)
        {
            return candidates.First();
        }

        throw new InvalidOperationException($"Template parser for format '{templateFormat}' is not registered.");
    }

    public IReadOnlyCollection<ITemplateParser> GetAll() => _parsers;

    public IReadOnlyCollection<ITemplateParser> FindCandidates(string templateFormat)
        => _parsers
            .Where(parser => parser.CanHandle(templateFormat))
            .OrderBy(parser => parser.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyDictionary<string, ITemplateParser> BuildParserMap(IEnumerable<ITemplateParser> parsers)
    {
        var duplicates = parsers
            .GroupBy(parser => parser.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Duplicate template parsers are registered: {string.Join(", ", duplicates)}.");
        }

        return parsers.ToDictionary(parser => parser.Name, StringComparer.OrdinalIgnoreCase);
    }
}
