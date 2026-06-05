using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class DocumentGenerationEngineRegistry : IDocumentGenerationEngineRegistry
{
    private readonly IReadOnlyCollection<IDocumentGenerationEngine> _engineCollection;
    private readonly IReadOnlyDictionary<string, IDocumentGenerationEngine> _engines;
    private readonly IReadOnlyDictionary<string, DocumentGenerationEngineDescriptor> _descriptors;

    public DocumentGenerationEngineRegistry(IEnumerable<IDocumentGenerationEngine> engines)
    {
        _engineCollection = engines.ToArray();
        _engines = BuildEngineMap(_engineCollection);
        _descriptors = _engineCollection
            .Select(DocumentGenerationEngineDescriptor.FromEngine)
            .ToDictionary(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IDocumentGenerationEngine Resolve(string engineName)
    {
        if (_engines.TryGetValue(engineName, out var engine))
        {
            return engine;
        }

        throw new InvalidOperationException($"Document generation engine '{engineName}' is not registered.");
    }

    public IReadOnlyCollection<IDocumentGenerationEngine> GetAll() => _engineCollection;

    public IReadOnlyCollection<DocumentGenerationEngineDescriptor> GetDescriptors() => _descriptors.Values.ToArray();

    public DocumentGenerationEngineDescriptor? FindDescriptor(string engineName)
        => _descriptors.TryGetValue(engineName, out var descriptor)
            ? descriptor
            : null;

    public IReadOnlyCollection<IDocumentGenerationEngine> FindCandidates(string inputFormat, string outputFormat, string? templateFormat = null)
        => _engineCollection
            .Where(engine => engine.CanHandle(inputFormat, outputFormat, templateFormat))
            .ToArray();

    public IReadOnlyCollection<DocumentGenerationEngineDescriptor> FindCandidateDescriptors(string inputFormat, string outputFormat, string? templateFormat = null)
        => _engineCollection
            .Select(engine => new
            {
                Engine = engine,
                Descriptor = _descriptors[engine.Name]
            })
            .Where(item => item.Engine.CanHandle(inputFormat, outputFormat, templateFormat))
            .Select(item => item.Descriptor)
            .OrderByDescending(descriptor => descriptor.Priority)
            .ThenBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyDictionary<string, IDocumentGenerationEngine> BuildEngineMap(IEnumerable<IDocumentGenerationEngine> engines)
    {
        var duplicates = engines
            .GroupBy(engine => engine.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Duplicate document generation engines are registered: {string.Join(", ", duplicates)}.");
        }

        return engines
            .ToDictionary(engine => engine.Name, StringComparer.OrdinalIgnoreCase);
    }
}
