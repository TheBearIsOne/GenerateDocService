using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// Every combination below returned HTTP 500 for scriban before parser resolution took the
/// requesting engine into account: both parsers claim txt, html, md and markdown, and the registry
/// picked the alphabetically first, so scriban was handed a DotLiquid template.
/// </summary>
public sealed class TemplateFormatSelectionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TemplateFormatSelectionIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Theory]
    [InlineData("scriban", "txt")]
    [InlineData("scriban", "html")]
    [InlineData("scriban", "md")]
    [InlineData("dotliquid", "txt")]
    [InlineData("dotliquid", "html")]
    [InlineData("dotliquid", "md")]
    public async Task SharedTemplateFormats_ShouldRenderWithTheRequestedEngine(string engine, string format)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: engine,
            InputFormat: "json",
            OutputFormat: format,
            TemplateFormat: format,
            Payload: """{"document":"hello","customer":{"name":"Ada Lovelace"}}""",
            Template: "Document: {{ document }} for {{ customer.name }}",
            Metadata: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Document: hello for Ada Lovelace");
    }
}
