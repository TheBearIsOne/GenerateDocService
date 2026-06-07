using System.Net.Http.Json;
using NBomber.Contracts;
using NBomber.CSharp;

var baseUrl = args.Length > 0 ? args[0] : "https://localhost:7001";
var syncScenarioName = args.Length > 1 ? args[1] : "sync_scriban_explicit";
var asyncScenarioName = args.Length > 2 ? args[2] : "async_scriban_explicit";
var reportRoot = args.Length > 3 ? args[3] : Path.Combine(ResolveRepositoryRoot(), "artifacts", "perf");
var reportFolder = Path.Combine(
    reportRoot,
    $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{SanitizeName(syncScenarioName)}-{SanitizeName(asyncScenarioName)}");

Directory.CreateDirectory(reportFolder);

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl)
};

var syncScenario = CreateSyncScenario(httpClient, GetScenarioProfile(syncScenarioName));
var asyncScenario = CreateAsyncScenario(httpClient, GetScenarioProfile(asyncScenarioName));

NBomberRunner
    .RegisterScenarios(
        syncScenario.WithLoadSimulations(Simulation.KeepConstant(copies: 5, during: TimeSpan.FromSeconds(20))),
        asyncScenario.WithLoadSimulations(Simulation.KeepConstant(copies: 3, during: TimeSpan.FromSeconds(20))))
    .WithReportFolder(reportFolder)
    .Run();

Console.WriteLine($"NBomber reports folder: {reportFolder}");

static ScenarioProps CreateSyncScenario(HttpClient httpClient, ScenarioProfile profile)
    => Scenario.Create(profile.Name, async context =>
    {
        var request = profile.CreateRequest("sync", context.InvocationNumber);

        using var response = await httpClient.PostAsJsonAsync("/api/v1/documents/sync", request, context.ScenarioCancellationToken);
        return response.IsSuccessStatusCode
            ? Response.Ok(statusCode: response.StatusCode.ToString())
            : Response.Fail(statusCode: response.StatusCode.ToString(), message: await response.Content.ReadAsStringAsync(context.ScenarioCancellationToken));
    });

static ScenarioProps CreateAsyncScenario(HttpClient httpClient, ScenarioProfile profile)
    => Scenario.Create(profile.Name, async context =>
    {
        var request = profile.CreateRequest("async", context.InvocationNumber);

        using var createResponse = await httpClient.PostAsJsonAsync("/api/v1/documents/async", request, context.ScenarioCancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            return Response.Fail(statusCode: createResponse.StatusCode.ToString(), message: await createResponse.Content.ReadAsStringAsync(context.ScenarioCancellationToken));
        }

        var taskStatus = await createResponse.Content.ReadFromJsonAsync<TaskStatusDto>(cancellationToken: context.ScenarioCancellationToken);
        if (taskStatus is null)
        {
            return Response.Fail(message: "Task status payload was null.");
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var statusResponse = await httpClient.GetAsync($"/api/v1/tasks/{taskStatus.TaskId}", context.ScenarioCancellationToken);
            if (!statusResponse.IsSuccessStatusCode)
            {
                return Response.Fail(statusCode: statusResponse.StatusCode.ToString(), message: await statusResponse.Content.ReadAsStringAsync(context.ScenarioCancellationToken));
            }

            var current = await statusResponse.Content.ReadFromJsonAsync<TaskStatusDto>(cancellationToken: context.ScenarioCancellationToken);
            if (current?.Status == 2 && !string.IsNullOrWhiteSpace(current.DownloadUrl))
            {
                using var downloadResponse = await httpClient.GetAsync(current.DownloadUrl, context.ScenarioCancellationToken);
                return downloadResponse.IsSuccessStatusCode
                    ? Response.Ok(statusCode: downloadResponse.StatusCode.ToString())
                    : Response.Fail(statusCode: downloadResponse.StatusCode.ToString(), message: await downloadResponse.Content.ReadAsStringAsync(context.ScenarioCancellationToken));
            }

            if (current?.Status == 3)
            {
                return Response.Fail(message: current.Error ?? "Async generation failed.");
            }

            await Task.Delay(100, context.ScenarioCancellationToken);
        }

        return Response.Fail(message: "Async generation did not complete in time.");
    });

static ScenarioProfile GetScenarioProfile(string scenarioName)
    => scenarioName.ToLowerInvariant() switch
    {
        "sync_scriban_explicit" => new ScenarioProfile(
            Name: "sync_scriban_explicit",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "sync_scriban_warm" => new ScenarioProfile(
            Name: "sync_scriban_warm",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "sync_scriban_cold" => new ScenarioProfile(
            Name: "sync_scriban_cold",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}",
            VaryTemplatePerInvocation: true),

        "async_scriban_explicit" => new ScenarioProfile(
            Name: "async_scriban_explicit",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "sync_scriban_auto" => new ScenarioProfile(
            Name: "sync_scriban_auto",
            Engine: null,
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "async_scriban_auto" => new ScenarioProfile(
            Name: "async_scriban_auto",
            Engine: null,
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "sync_dotliquid_explicit" => new ScenarioProfile(
            Name: "sync_dotliquid_explicit",
            Engine: "dotliquid",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "dotliquid",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "async_dotliquid_explicit" => new ScenarioProfile(
            Name: "async_dotliquid_explicit",
            Engine: "dotliquid",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "dotliquid",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "sync_dotliquid_auto" => new ScenarioProfile(
            Name: "sync_dotliquid_auto",
            Engine: null,
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "dotliquid",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "async_dotliquid_auto" => new ScenarioProfile(
            Name: "async_dotliquid_auto",
            Engine: null,
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "dotliquid",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "sync_dotliquid_warm" => new ScenarioProfile(
            Name: "sync_dotliquid_warm",
            Engine: "dotliquid",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "dotliquid",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}"),

        "sync_dotliquid_cold" => new ScenarioProfile(
            Name: "sync_dotliquid_cold",
            Engine: "dotliquid",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "dotliquid",
            Payload: "{\"document\":\"hello\",\"customer\":{\"name\":\"Ada Lovelace\"}}",
            Template: "Document: {{ document }} for {{ customer.name }}",
            VaryTemplatePerInvocation: true),

        "sync_questpdf" => new ScenarioProfile(
            Name: "sync_questpdf",
            Engine: "questpdf",
            InputFormat: "json",
            OutputFormat: "pdf",
            TemplateFormat: null,
            Payload: "{\"title\":\"Quarterly Report\",\"document\":\"Revenue growth is positive\"}",
            Template: null),

        _ => throw new ArgumentOutOfRangeException(nameof(scenarioName), scenarioName, "Unsupported NBomber scenario profile.")
    };

static string ResolveRepositoryRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "GenerateDocService.slnx")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static string SanitizeName(string value)
    => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));

internal sealed record TaskStatusDto(
    string TaskId,
    int Status,
    string? ResultFileName,
    string? ResultStoragePath,
    string? DownloadUrl,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

internal sealed record ScenarioProfile(
    string Name,
    string? Engine,
    string InputFormat,
    string OutputFormat,
    string? TemplateFormat,
    string Payload,
    string? Template,
    bool VaryTemplatePerInvocation = false)
{
    public object CreateRequest(string executionMode, long invocationNumber)
        => new
        {
            requestId = Guid.NewGuid().ToString("N"),
            engine = Engine,
            inputFormat = InputFormat,
            outputFormat = OutputFormat,
            templateFormat = TemplateFormat,
            payload = Payload,
            template = BuildTemplate(invocationNumber),
            metadata = new Dictionary<string, string>
            {
                ["client"] = "nbomber",
                ["scenario"] = Name,
                ["mode"] = executionMode,
                ["invocation"] = invocationNumber.ToString()
            }
        };

    private string? BuildTemplate(long invocationNumber)
        => Template is null
            ? null
            : VaryTemplatePerInvocation
                ? $"{Template}\ncache-buster:{invocationNumber}"
                : Template;
}
