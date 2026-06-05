using System.Text;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.Engine.Abstractions;
using GenerateDocService.Engine.DotLiquid;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class DotLiquidDocumentGenerationEngineTests
{
    [Fact]
    public async Task GenerateAsync_ShouldRenderTemplateFromJsonPayload()
    {
        IDocumentGenerationEngine engine = new DotLiquidDocumentGenerationEngine(new TemplateParserRegistry([new DotLiquidTemplateParser()]));
        var request = new GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: "dotliquid",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "liquid",
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
    public async Task ParseAsync_ShouldCompileDotLiquidTemplate()
    {
        ITemplateParser parser = new DotLiquidTemplateParser(new InMemoryCompiledTemplateCache());

        var result = await parser.ParseAsync(Encoding.UTF8.GetBytes("Hello {{ name }}"));

        result.TemplateFormat.Should().Be("liquid");
        result.CompiledTemplate.Should().BeOfType<DotLiquid.Template>();
        result.Metadata.Should().ContainKey("parser").WhoseValue.Should().Be("dotliquid");
    }
}
