using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

public sealed class ApiValidationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiValidationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task SyncGeneration_EmptyPayload_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-1",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "",
            Template: "{{ document }}",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncGeneration_EmptyInputFormat_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-2",
            Engine: "scriban",
            InputFormat: "",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"test\"}",
            Template: "{{ document }}",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncGeneration_ExcessiveMetadataEntries_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var excessiveMetadata = Enumerable.Range(0, 51)
            .ToDictionary(i => $"key{i}", i => $"value{i}");

        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-3",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"test\"}",
            Template: "{{ document }}",
            Metadata: excessiveMetadata);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncGeneration_ExcessiveRequestId_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: new string('x', 65),
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"test\"}",
            Template: "{{ document }}",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncGeneration_ExcessiveEngineName_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-5",
            Engine: new string('x', 65),
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"test\"}",
            Template: "{{ document }}",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AsyncGeneration_EmptyOutputFormat_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-6",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"test\"}",
            Template: "{{ document }}",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/async", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncGeneration_ExcessiveTemplateSize_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var largeTemplate = new string('x', 5_242_881); // 5MB + 1 byte

        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-7",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"test\"}",
            Template: largeTemplate,
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncGeneration_WhitespaceOnlyPayload_ShouldReturnInternalServerError()
    {
        using var client = CreateClient();

        // Validator doesn't reject whitespace-only payloads (only empty/null),
        // so Scriban receives invalid JSON and throws → 500
        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-8",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "   ",
            Template: "{{ document }}",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task SyncGeneration_ExcessiveMetadataKeyLength_ShouldReturnBadRequest()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: "test-9",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"test\"}",
            Template: "{{ document }}",
            Metadata: new Dictionary<string, string>
            {
                [new string('k', 129)] = "value"
            });

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient CreateClient()
        => _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
}
