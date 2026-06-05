using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;

public interface IDocumentGenerationEngineRegistry
{
    IDocumentGenerationEngine Resolve(string engineName);
    IReadOnlyCollection<IDocumentGenerationEngine> GetAll();
    IReadOnlyCollection<DocumentGenerationEngineDescriptor> GetDescriptors();
    DocumentGenerationEngineDescriptor? FindDescriptor(string engineName);
    IReadOnlyCollection<IDocumentGenerationEngine> FindCandidates(string inputFormat, string outputFormat, string? templateFormat = null);
    IReadOnlyCollection<DocumentGenerationEngineDescriptor> FindCandidateDescriptors(string inputFormat, string outputFormat, string? templateFormat = null);
}
