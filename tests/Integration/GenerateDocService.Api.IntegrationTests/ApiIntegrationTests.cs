using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

public sealed class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task GetEngines_ShouldReturnRegisteredEngines()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/engines");
        var payload = await response.Content.ReadFromJsonAsync<DocumentEngineHttpResponse[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.Should().NotBeNull();
        payload!.Select(engine => engine.Name).Should().Contain(["fake", "scriban", "questpdf"]);
    }

    [Fact]
    public async Task AsyncGeneration_ShouldExposeCorrelationId_StatusAndDownloadFlow()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "integration-corr-123");

        var request = new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}",
            Metadata: new Dictionary<string, string>
            {
                ["client"] = "integration-test"
            });

        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/async", request);
        var accepted = await createResponse.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();

        createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        createResponse.Headers.TryGetValues("X-Correlation-Id", out var correlationValues).Should().BeTrue();
        correlationValues!.Single().Should().Be("integration-corr-123");
        accepted.Should().NotBeNull();

        var completed = await WaitForCompletionAsync(client, accepted!.TaskId);
        completed.Status.Should().Be(GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Completed);
        completed.DownloadUrl.Should().NotBeNullOrWhiteSpace();

        var downloadResponse = await client.GetAsync(completed.DownloadUrl!);
        var content = await downloadResponse.Content.ReadAsStringAsync();

        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        downloadResponse.Headers.TryGetValues("X-Correlation-Id", out var downloadCorrelationValues).Should().BeTrue();
        downloadCorrelationValues!.Single().Should().Be("integration-corr-123");
        content.Should().Be("Document: hello for Ada Lovelace");
    }

    [Fact]
    public async Task SyncGeneration_WithQuestPdf_ShouldReturnPdfFile()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "questpdf",
            InputFormat: "json",
            OutputFormat: "pdf",
            TemplateFormat: null,
            Payload: "{\"title\":\"Quarterly Report\",\"document\":\"Revenue growth is positive\"}",
            Template: null,
            Metadata: new Dictionary<string, string>
            {
                ["client"] = "integration-test",
                ["scenario"] = "questpdf-sync"
            });

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);
        var content = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        content.Should().NotBeEmpty();
        content.Length.Should().BeGreaterThan(4);
        Encoding.ASCII.GetString(content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task SyncGeneration_WithoutEngine_ShouldAutoSelectMatchingEngine()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: null,
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}",
            Metadata: new Dictionary<string, string>
            {
                ["client"] = "integration-test",
                ["scenario"] = "auto-engine-selection-sync"
            });

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Document: hello for Ada Lovelace");
    }

    private HttpClient CreateClient()
        => _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task<TaskStatusHttpResponse> WaitForCompletionAsync(HttpClient client, string taskId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await client.GetAsync($"/api/v1/tasks/{taskId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var payload = await response.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();
            payload.Should().NotBeNull();

            if (payload!.Status == GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Completed)
            {
                return payload;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Task '{taskId}' did not complete in time.");
    }
}
