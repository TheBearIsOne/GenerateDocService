using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class DocumentGenerationEngineRegistry(IEnumerable<IDocumentGenerationEngine> engines) : IDocumentGenerationEngineRegistry
{
    private readonly IReadOnlyDictionary<string, IDocumentGenerationEngine> _engines = engines
        .GroupBy(engine => engine.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

    public IDocumentGenerationEngine Resolve(string engineName)
    {
        if (_engines.TryGetValue(engineName, out var engine))
        {
            return engine;
        }

        throw new InvalidOperationException($"Document generation engine '{engineName}' is not registered.");
    }

    public IReadOnlyCollection<IDocumentGenerationEngine> GetAll() => _engines.Values.ToArray();

    public IReadOnlyCollection<IDocumentGenerationEngine> FindCandidates(string inputFormat, string outputFormat, string? templateFormat = null)
        => _engines.Values
            .Where(engine => engine.CanHandle(inputFormat, outputFormat, templateFormat))
            .ToArray();
}
