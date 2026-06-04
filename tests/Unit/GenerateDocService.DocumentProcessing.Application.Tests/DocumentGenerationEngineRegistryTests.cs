using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class DocumentGenerationEngineRegistryTests
{
    [Fact]
    public void Resolve_ShouldReturnRegisteredEngine()
    {
        var registry = new DocumentGenerationEngineRegistry(new[] { new FakeDocumentGenerationEngine() });

        var engine = registry.Resolve("fake");

        engine.Name.Should().Be("fake");
    }
}
