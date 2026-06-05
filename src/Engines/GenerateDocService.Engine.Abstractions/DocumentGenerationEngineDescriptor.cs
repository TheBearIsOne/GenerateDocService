using System.Reflection;

namespace GenerateDocService.Engine.Abstractions;

public sealed record DocumentGenerationEngineDescriptor(
    string Name,
    Type ImplementationType,
    IReadOnlyCollection<string> InputFormats,
    IReadOnlyCollection<string> OutputFormats,
    IReadOnlyCollection<string> TemplateFormats,
    int Priority)
{
    public static DocumentGenerationEngineDescriptor FromEngine(IDocumentGenerationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var implementationType = engine.GetType();
        var attribute = implementationType.GetCustomAttribute<DocumentEngineAttribute>();

        return new DocumentGenerationEngineDescriptor(
            attribute?.Name ?? engine.Name,
            implementationType,
            attribute?.InputFormats ?? [],
            attribute?.OutputFormats ?? [],
            attribute?.TemplateFormats ?? [],
            attribute?.Priority ?? 0);
    }
}
