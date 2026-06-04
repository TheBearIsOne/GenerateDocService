using System.Net.Http.Json;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;

var baseAddress = args.Length > 0 ? args[0] : "https://localhost:7001";

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseAddress)
};

var request = new GenerateDocumentHttpRequest(
    RequestId: Guid.NewGuid().ToString("N"),
    Engine: "fake",
    InputFormat: "json",
    OutputFormat: "txt",
    TemplateFormat: null,
    Payload: "{\"document\":\"hello\"}",
    Template: null,
    Metadata: new Dictionary<string, string>
    {
        ["client"] = "sample"
    });

var asyncResponse = await httpClient.PostAsJsonAsync("/api/v1/documents/async", request);
Console.WriteLine($"Async status: {(int)asyncResponse.StatusCode}");
Console.WriteLine(await asyncResponse.Content.ReadAsStringAsync());

var syncResponse = await httpClient.PostAsJsonAsync("/api/v1/documents/sync", request);
Console.WriteLine($"Sync status: {(int)syncResponse.StatusCode}");
Console.WriteLine(await syncResponse.Content.ReadAsStringAsync());
