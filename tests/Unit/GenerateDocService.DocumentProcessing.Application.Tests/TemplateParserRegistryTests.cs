using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.Engine.DotLiquid;
using GenerateDocService.Engine.Scriban;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class TemplateParserRegistryTests
{
    private static TemplateParserRegistry CreateRegistry()
        => new([new ScribanTemplateParser(), new DotLiquidTemplateParser()]);

    [Theory]
    [InlineData("html")]
    [InlineData("txt")]
    [InlineData("md")]
    [InlineData("markdown")]
    public void Resolve_ShouldPreferTheRequestingEngineParser_ForContestedFormats(string templateFormat)
    {
        var registry = CreateRegistry();

        registry.Resolve(templateFormat, "scriban").Name.Should().Be("scriban");
        registry.Resolve(templateFormat, "dotliquid").Name.Should().Be("dotliquid");
    }

    [Fact]
    public void Resolve_ShouldFallBackToNameOrder_WhenNoPreferenceIsGivenOrMatches()
    {
        var registry = CreateRegistry();

        // Documents the pre-existing tie-break so a change to it is deliberate rather than accidental.
        registry.Resolve("html").Name.Should().Be("dotliquid");
        registry.Resolve("html", "questpdf").Name.Should().Be("dotliquid");
    }

    [Fact]
    public void Resolve_ShouldIgnoreThePreference_ForAnUncontestedFormat()
    {
        var registry = CreateRegistry();

        registry.Resolve("scriban", "dotliquid").Name.Should().Be("scriban");
        registry.Resolve("liquid", "scriban").Name.Should().Be("dotliquid");
    }

    [Fact]
    public void Resolve_ShouldThrow_ForAnUnknownFormat()
    {
        var registry = CreateRegistry();

        var act = () => registry.Resolve("razor", "scriban");

        act.Should().Throw<InvalidOperationException>().WithMessage("*razor*");
    }

    [Fact]
    public async Task ScribanEngine_ShouldRenderHtml_WhenTemplateFormatIsHtml()
    {
        // This combination returned HTTP 500 before parser resolution considered the engine.
        var engine = new ScribanDocumentGenerationEngine(CreateRegistry());

        var result = await engine.GenerateAsync(new Models.GenerateDocumentRequest(
            requestId: "scriban-html",
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "html",
            templateFormat: "html",
            payload: System.Text.Encoding.UTF8.GetBytes("""{"document":"hello","customer":{"name":"Ada Lovelace"}}"""),
            template: System.Text.Encoding.UTF8.GetBytes("<h1>{{ document }}</h1><p>{{ customer.name }}</p>"),
            metadata: null));

        result.OutputFormat.Should().Be("html");

        using var reader = new StreamReader(result.Content);
        (await reader.ReadToEndAsync()).Should().Be("<h1>hello</h1><p>Ada Lovelace</p>");
    }

    [Fact]
    public async Task DotLiquidEngine_ShouldRenderHtml_WhenTemplateFormatIsHtml()
    {
        var engine = new DotLiquidDocumentGenerationEngine(CreateRegistry());

        var result = await engine.GenerateAsync(new Models.GenerateDocumentRequest(
            requestId: "dotliquid-html",
            engine: "dotliquid",
            inputFormat: "json",
            outputFormat: "html",
            templateFormat: "html",
            payload: System.Text.Encoding.UTF8.GetBytes("""{"document":"hello","customer":{"name":"Ada Lovelace"}}"""),
            template: System.Text.Encoding.UTF8.GetBytes("<h1>{{ document }}</h1><p>{{ customer.name }}</p>"),
            metadata: null));

        result.OutputFormat.Should().Be("html");

        using var reader = new StreamReader(result.Content);
        (await reader.ReadToEndAsync()).Should().Be("<h1>hello</h1><p>Ada Lovelace</p>");
    }
}
