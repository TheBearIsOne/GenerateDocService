using System.Text;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.Engine.Abstractions;
using GenerateDocService.Engine.Scriban;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class ScribanDocumentGenerationEngineTests
{
    [Fact]
    public async Task GenerateAsync_ShouldRenderTemplateFromJsonPayload()
    {
        IDocumentGenerationEngine engine = new ScribanDocumentGenerationEngine(new TemplateParserRegistry([new ScribanTemplateParser()]));
        var request = new GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes("{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}"),
            template: Encoding.UTF8.GetBytes("Document: {{ document }} for {{ customer.name }}"),
            metadata: new Dictionary<string, string>
            {
                ["client"] = "test"
            });

        var result = await engine.GenerateAsync(request);
        using var reader = new StreamReader(result.Content, Encoding.UTF8, leaveOpen: true);
        result.Content.Position = 0;
        var content = await reader.ReadToEndAsync();

        content.Should().Be("Document: hello for Ada Lovelace");
        result.ContentType.Should().Be("text/plain");
        result.OutputFormat.Should().Be("txt");
    }

    [Fact]
    public void CanHandle_ShouldReturnTrue_ForSupportedFormats()
    {
        IDocumentGenerationEngine engine = new ScribanDocumentGenerationEngine(new TemplateParserRegistry([new ScribanTemplateParser()]));

        var canHandle = engine.CanHandle("json", "html", "scriban");

        canHandle.Should().BeTrue();
    }

    [Fact]
    public async Task ParseAsync_ShouldCompileScribanTemplate()
    {
        ITemplateParser parser = new ScribanTemplateParser(new InMemoryCompiledTemplateCache());

        var result = await parser.ParseAsync(Encoding.UTF8.GetBytes("Hello {{ name }}"));

        result.TemplateFormat.Should().Be("scriban");
        result.CompiledTemplate.Should().BeOfType<Scriban.Template>();
        result.Metadata.Should().ContainKey("parser").WhoseValue.Should().Be("scriban");
    }

    [Fact]
    public async Task ParseAsync_ShouldReuseCompiledTemplateFromCache()
    {
        var cache = new InMemoryCompiledTemplateCache();
        ITemplateParser parser = new ScribanTemplateParser(cache);
        var template = Encoding.UTF8.GetBytes("Hello {{ name }}");

        var first = await parser.ParseAsync(template);
        var second = await parser.ParseAsync(template);

        ReferenceEquals(first.CompiledTemplate, second.CompiledTemplate).Should().BeTrue();
    }
}
