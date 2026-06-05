using System.Net.Http.Json;
using NBomber.Contracts;
using NBomber.CSharp;

var baseUrl = args.Length > 0 ? args[0] : "https://localhost:7001";

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl)
};

var syncScenario = Scenario.Create("sync_scriban_generation", async context =>
{
    var request = new
    {
        requestId = Guid.NewGuid().ToString("N"),
        engine = "scriban",
        inputFormat = "json",
        outputFormat = "txt",
        templateFormat = "scriban",
        payload = "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
        template = "Document: {{ document }} for {{ customer.name }}",
        metadata = new Dictionary<string, string>
        {
            ["client"] = "nbomber",
            ["scenario"] = "sync"
        }
    };

    using var response = await httpClient.PostAsJsonAsync("/api/v1/documents/sync", request, context.CancellationToken);
    return response.IsSuccessStatusCode
        ? Response.Ok(statusCode: (int)response.StatusCode)
        : Response.Fail(statusCode: (int)response.StatusCode, error: await response.Content.ReadAsStringAsync(context.CancellationToken));
});

var asyncScenario = Scenario.Create("async_scriban_generation", async context =>
{
    var request = new
    {
        requestId = Guid.NewGuid().ToString("N"),
        engine = "scriban",
        inputFormat = "json",
        outputFormat = "txt",
        templateFormat = "scriban",
        payload = "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
        template = "Document: {{ document }} for {{ customer.name }}",
        metadata = new Dictionary<string, string>
        {
            ["client"] = "nbomber",
            ["scenario"] = "async"
        }
    };

    using var createResponse = await httpClient.PostAsJsonAsync("/api/v1/documents/async", request, context.CancellationToken);
    if (!createResponse.IsSuccessStatusCode)
    {
        return Response.Fail(statusCode: (int)createResponse.StatusCode, error: await createResponse.Content.ReadAsStringAsync(context.CancellationToken));
    }

    var taskStatus = await createResponse.Content.ReadFromJsonAsync<TaskStatusDto>(cancellationToken: context.CancellationToken);
    if (taskStatus is null)
    {
        return Response.Fail(error: "Task status payload was null.");
    }

    for (var attempt = 0; attempt < 20; attempt++)
    {
        using var statusResponse = await httpClient.GetAsync($"/api/v1/tasks/{taskStatus.TaskId}", context.CancellationToken);
        if (!statusResponse.IsSuccessStatusCode)
        {
            return Response.Fail(statusCode: (int)statusResponse.StatusCode, error: await statusResponse.Content.ReadAsStringAsync(context.CancellationToken));
        }

        var current = await statusResponse.Content.ReadFromJsonAsync<TaskStatusDto>(cancellationToken: context.CancellationToken);
        if (current?.Status == 2 && !string.IsNullOrWhiteSpace(current.DownloadUrl))
        {
            using var downloadResponse = await httpClient.GetAsync(current.DownloadUrl, context.CancellationToken);
            return downloadResponse.IsSuccessStatusCode
                ? Response.Ok(statusCode: (int)downloadResponse.StatusCode)
                : Response.Fail(statusCode: (int)downloadResponse.StatusCode, error: await downloadResponse.Content.ReadAsStringAsync(context.CancellationToken));
        }

        if (current?.Status == 3)
        {
            return Response.Fail(error: current.Error ?? "Async generation failed.");
        }

        await Task.Delay(100, context.CancellationToken);
    }

    return Response.Fail(error: "Async generation did not complete in time.");
});

NBomberRunner
    .RegisterScenarios(
        syncScenario.WithLoadSimulations(Simulation.KeepConstant(copies: 5, during: TimeSpan.FromSeconds(20))),
        asyncScenario.WithLoadSimulations(Simulation.KeepConstant(copies: 3, during: TimeSpan.FromSeconds(20))))
    .Run();

internal sealed record TaskStatusDto(
    string TaskId,
    int Status,
    string? ResultFileName,
    string? ResultStoragePath,
    string? DownloadUrl,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
