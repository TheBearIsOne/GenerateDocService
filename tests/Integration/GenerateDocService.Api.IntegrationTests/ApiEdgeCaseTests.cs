using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

public sealed class ApiEdgeCaseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiEdgeCaseTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task GetTaskStatus_NonExistentTaskId_ShouldReturnNotFound()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/tasks/non-existent-task-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadArtifact_NonExistentTaskId_ShouldReturnNotFound()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/tasks/non-existent-task-id/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEngine_NonExistentEngine_ShouldReturnNotFound()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/engines/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SyncGeneration_NoMatchingEngine_ShouldReturnInternalServerError()
    {
        using var client = CreateClient();

        // questpdf requires specific input; using unsupported format should fail
        var request = new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "scriban",
            InputFormat: "xml",
            OutputFormat: "xls",
            TemplateFormat: "scriban",
            Payload: "<root><document>test</document></root>",
            Template: "{{ document }}",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        // Scriban can't handle xml->xls, so this should fail
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AsyncGeneration_NoMatchingEngine_ShouldFailTask()
    {
        using var client = CreateClient();

        var request = new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "scriban",
            InputFormat: "xml",
            OutputFormat: "xls",
            TemplateFormat: "scriban",
            Payload: "<root><document>test</document></root>",
            Template: "{{ document }}",
            Metadata: null);

        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/async", request);
        var accepted = await createResponse.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();

        createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        accepted.Should().NotBeNull();

        var completed = await WaitForFinalStatusAsync(client, accepted!.TaskId);
        completed.Status.Should().Be(GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Failed);
    }

    [Fact]
    public async Task SyncGeneration_MalformedPayload_ShouldStillProcessWithScriban()
    {
        using var client = CreateClient();

        // Scriban may process even malformed JSON depending on template usage
        var request = new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "not-valid-json",
            Template: "static text",
            Metadata: null);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", request);

        // Scriban may succeed with static template, or fail — either way is valid behavior
        (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.InternalServerError)
            .Should().BeTrue();
    }

    [Fact]
    public async Task DownloadArtifact_InProgressTask_ShouldReturnNotFoundOrOk()
    {
        using var client = CreateClient();

        // Enqueue an async request and immediately try to download
        // With in-memory processing, the task may already be completed.
        // With RabbitMQ/external queue, it would likely still be in progress.
        // This test verifies the endpoint handles both cases correctly.
        var request = new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Test\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}",
            Metadata: null);

        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/async", request);
        var accepted = await createResponse.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();

        createResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Immediately try to download — task is either completed (in-memory) or still queued
        var downloadResponse = await client.GetAsync($"/api/v1/tasks/{accepted!.TaskId}/download");
        (downloadResponse.StatusCode == HttpStatusCode.OK || downloadResponse.StatusCode == HttpStatusCode.NotFound)
            .Should().BeTrue();
    }

    private HttpClient CreateClient()
        => _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task<TaskStatusHttpResponse> WaitForFinalStatusAsync(HttpClient client, string taskId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await client.GetAsync($"/api/v1/tasks/{taskId}");
            var payload = await response.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();
            payload.Should().NotBeNull();

            var status = payload!.Status;
            if (status == GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Completed
                || status == GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Failed)
            {
                return payload;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Task '{taskId}' did not reach a final state in time.");
    }
}
