using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;
using GenerateDocService.Engine.Abstractions;
using GenerateDocService.Engine.QuestPdf;
using GenerateDocService.Engine.Scriban;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public void GetDescriptors_ShouldReturnEngineMetadata()
    {
        var registry = new DocumentGenerationEngineRegistry(new[] { new FakeDocumentGenerationEngine() });

        var descriptor = registry.FindDescriptor("fake");

        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(FakeDocumentGenerationEngine));
        descriptor.InputFormats.Should().Contain("json");
        descriptor.OutputFormats.Should().Contain("txt");
        descriptor.TemplateFormats.Should().Contain("scriban");
    }

    [Fact]
    public void AddDocumentGenerationEngines_ShouldRegisterAttributedEnginesFromAssembly()
    {
        var services = new ServiceCollection();

        services.AddDocumentGenerationEngines(typeof(FakeDocumentGenerationEngine).Assembly);

        using var provider = services.BuildServiceProvider();
        var engines = provider.GetServices<GenerateDocService.Engine.Abstractions.IDocumentGenerationEngine>();

        engines.Should().ContainSingle(engine => engine.Name == "fake");
    }

    [Fact]
    public void Constructor_ShouldThrowWhenDuplicateEngineNamesAreRegistered()
    {
        var duplicate = new DuplicateFakeDocumentGenerationEngine();

        var action = () => new DocumentGenerationEngineRegistry(new GenerateDocService.Engine.Abstractions.IDocumentGenerationEngine[]
        {
            new FakeDocumentGenerationEngine(),
            duplicate
        });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate document generation engines*");
    }

    [Fact]
    public void TemplateParserRegistry_ShouldResolveRegisteredParser()
    {
        ITemplateParserRegistry registry = new TemplateParserRegistry([new ScribanTemplateParser()]);

        var parser = registry.Resolve("scriban");

        parser.Name.Should().Be("scriban");
    }

    [Fact]
    public async Task SyncDocumentGenerationService_ShouldAutoSelectHighestPriorityEngine()
    {
        var registry = new DocumentGenerationEngineRegistry([
            new FakeDocumentGenerationEngine(),
            new ScribanDocumentGenerationEngine(new TemplateParserRegistry([new ScribanTemplateParser()]))
        ]);
        using var metrics = new DocumentGenerationMetrics();
        var service = new SyncDocumentGenerationService(registry, metrics);
        var request = new GenerateDocService.DocumentProcessing.Application.Models.GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: string.Empty,
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: System.Text.Encoding.UTF8.GetBytes("{\"document\":\"hello\"}"),
            template: System.Text.Encoding.UTF8.GetBytes("Document: {{ document }}"),
            metadata: null);

        var result = await service.GenerateAsync(request);

        result.Content.Position = 0;
        using var reader = new StreamReader(result.Content, System.Text.Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync();

        content.Should().Be("Document: hello");
    }

    [Fact]
    public void FindCandidateDescriptors_ShouldReturnHighestPriorityFirst()
    {
        var registry = new DocumentGenerationEngineRegistry([
            new FakeDocumentGenerationEngine(),
            new QuestPdfDocumentGenerationEngine()
        ]);

        var descriptors = registry.FindCandidateDescriptors("json", "pdf");

        descriptors.Should().NotBeEmpty();
        descriptors.First().Name.Should().Be("questpdf");
    }

    [GenerateDocService.Engine.Abstractions.DocumentEngine(
        "fake",
        InputFormats = new[] { "json" },
        OutputFormats = new[] { "txt" })]
    private sealed class DuplicateFakeDocumentGenerationEngine : GenerateDocService.Engine.Abstractions.IDocumentGenerationEngine
    {
        public string Name => "fake";

        public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
            => true;

        public Task<GenerateDocService.Engine.Abstractions.IReportResult> GenerateAsync(
            GenerateDocService.Engine.Abstractions.IReportRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
