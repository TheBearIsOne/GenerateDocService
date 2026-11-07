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

    public ITemplateParser Resolve(string templateFormat, string? preferredParserName = null)
    {
        var candidates = FindCandidates(templateFormat);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException($"Template parser for format '{templateFormat}' is not registered.");
        }

        // Scriban and DotLiquid both claim txt, html, md and markdown, and FindCandidates sorts by
        // name, so without this the alphabetically first parser always won and the other engine
        // rejected the compiled template at generation time.
        if (preferredParserName is not null)
        {
            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate.Name, preferredParserName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return candidates.First();
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
