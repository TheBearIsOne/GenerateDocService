using System.Net.Http.Json;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;

var baseAddress = args.Length > 0 ? args[0] : "https://localhost:7001";

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseAddress)
};

Console.WriteLine($"GenerateDocService sample client -> {baseAddress}");
Console.WriteLine();

await ShowEnginesAsync(httpClient);
await RunScribanScenarioAsync(httpClient);
await RunQuestPdfScenarioAsync(httpClient);

static async Task ShowEnginesAsync(HttpClient httpClient)
{
    PrintSection("Registered engines");

    var response = await httpClient.GetAsync("/api/v1/engines");
    var engines = await response.Content.ReadFromJsonAsync<DocumentEngineHttpResponse[]>();

    Console.WriteLine($"Status: {(int)response.StatusCode}");
    if (engines is null || engines.Length == 0)
    {
        Console.WriteLine("No engines returned by the API.");
        Console.WriteLine();
        return;
    }

    foreach (var engine in engines)
    {
        Console.WriteLine($"- {engine.Name} (priority: {engine.Priority})");
        Console.WriteLine($"  input: {string.Join(", ", engine.InputFormats)}");
        Console.WriteLine($"  output: {string.Join(", ", engine.OutputFormats)}");
        Console.WriteLine($"  templates: {string.Join(", ", engine.TemplateFormats)}");
    }

    Console.WriteLine();
}

static async Task RunScribanScenarioAsync(HttpClient httpClient)
{
    PrintSection("Scriban engine demo");

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
            ["client"] = "sample",
            ["scenario"] = "scriban-sync-async"
        });

    var asyncResponse = await httpClient.PostAsJsonAsync("/api/v1/documents/async", request);
    var asyncPayload = await asyncResponse.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();
    Console.WriteLine($"Async submit status: {(int)asyncResponse.StatusCode}");
    if (asyncPayload is null)
    {
        Console.WriteLine("Async payload was empty.");
    }
    else
    {
        Console.WriteLine($"TaskId: {asyncPayload.TaskId}");
        Console.WriteLine($"Initial status: {asyncPayload.Status}");

        var completed = await WaitForCompletionAsync(httpClient, asyncPayload.TaskId);
        Console.WriteLine($"Completed status: {completed.Status}");
        Console.WriteLine($"Result file: {completed.ResultFileName}");

        if (!string.IsNullOrWhiteSpace(completed.DownloadUrl))
        {
            var downloadResponse = await httpClient.GetAsync(completed.DownloadUrl);
            var downloadedContent = await downloadResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Async download status: {(int)downloadResponse.StatusCode}");
            Console.WriteLine($"Async content: {downloadedContent}");
        }
    }

    Console.WriteLine();

    var syncResponse = await httpClient.PostAsJsonAsync("/api/v1/documents/sync", request);
    var syncContent = await syncResponse.Content.ReadAsStringAsync();
    Console.WriteLine($"Sync status: {(int)syncResponse.StatusCode}");
    Console.WriteLine($"Sync content: {syncContent}");
    Console.WriteLine();
}

static async Task RunQuestPdfScenarioAsync(HttpClient httpClient)
{
    PrintSection("QuestPDF engine demo");

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
            ["client"] = "sample",
            ["scenario"] = "questpdf-sync"
        });

    var syncResponse = await httpClient.PostAsJsonAsync("/api/v1/documents/sync", request);
    Console.WriteLine($"Sync status: {(int)syncResponse.StatusCode}");

    var bytes = await syncResponse.Content.ReadAsByteArrayAsync();
    Console.WriteLine($"PDF bytes: {bytes.Length}");
    if (bytes.Length >= 4)
    {
        Console.WriteLine($"PDF header: {System.Text.Encoding.ASCII.GetString(bytes, 0, 4)}");
    }

    var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
    Directory.CreateDirectory(outputDirectory);
    var outputPath = Path.Combine(outputDirectory, "questpdf-sample.pdf");
    await File.WriteAllBytesAsync(outputPath, bytes);
    Console.WriteLine($"Saved PDF to: {outputPath}");
    Console.WriteLine();
}

static async Task<TaskStatusHttpResponse> WaitForCompletionAsync(HttpClient httpClient, string taskId)
{
    for (var attempt = 0; attempt < 20; attempt++)
    {
        var response = await httpClient.GetAsync($"/api/v1/tasks/{taskId}");
        var payload = await response.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();

        if (payload is not null && payload.Status is not GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Queued
            && payload.Status is not GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Processing)
        {
            return payload;
        }

        await Task.Delay(100);
    }

    throw new TimeoutException($"Task '{taskId}' did not complete in time.");
}

static void PrintSection(string title)
{
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
}
