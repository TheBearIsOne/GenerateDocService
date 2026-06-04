using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;

public interface IDocumentGenerationEngineRegistry
{
    IDocumentGenerationEngine Resolve(string engineName);
    IReadOnlyCollection<IDocumentGenerationEngine> GetAll();
    IReadOnlyCollection<IDocumentGenerationEngine> FindCandidates(string inputFormat, string outputFormat, string? templateFormat = null);
}
