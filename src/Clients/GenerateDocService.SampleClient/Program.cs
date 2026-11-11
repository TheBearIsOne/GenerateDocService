// Usage: dotnet run --project src/Clients/GenerateDocService.SampleClient -- [baseAddress] [selection]
//
//   baseAddress  API root, default http://localhost:5287
//   selection    optional example id, "all", or "engines"; omit to get an interactive menu
//
// Set GDS_SAMPLE_TOKEN to attach a bearer token, which the Docker stack requires.
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;

var baseAddress = args.Length > 0 ? args[0] : "http://localhost:8081";
var selection = args.Length > 1 ? args[1] : null;
var token = Environment.GetEnvironmentVariable("GDS_SAMPLE_TOKEN");

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseAddress)
};

if (!string.IsNullOrWhiteSpace(token))
{
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}

Console.WriteLine($"GenerateDocService sample client -> {baseAddress}");
Console.WriteLine($"Bearer token: {(string.IsNullOrWhiteSpace(token) ? "not set" : "attached")}");
Console.WriteLine();

var examples = BuildExamples();

if (string.Equals(selection, "engines", StringComparison.OrdinalIgnoreCase))
{
    await ShowEnginesAsync(httpClient);
    return 0;
}

if (!string.IsNullOrWhiteSpace(selection))
{
    await ShowEnginesAsync(httpClient);

    if (!string.Equals(selection, "all", StringComparison.OrdinalIgnoreCase))
    {
        var match = FindExample(examples, selection);
        if (match is null)
        {
            Console.WriteLine($"Unknown example '{selection}'. Valid values: {string.Join(", ", examples.Select(e => e.Id))}, all, engines");
            return 1;
        }

        await match.Run(httpClient);
        return 0;
    }

    foreach (var example in examples)
    {
        await example.Run(httpClient);
    }

    return 0;
}

await ShowEnginesAsync(httpClient);
PrintMenu(examples);

while (true)
{
    PrintHelp();

    Console.Write("Select an example: ");
    var input = Console.ReadLine();

    if (input is null)
    {
        Console.WriteLine("No interactive input available; pass an example id as the second argument.");
        return 0;
    }

    var choice = input.Trim();
    if (choice.Length == 0)
    {
        continue;
    }

    if (string.Equals(choice, "q", StringComparison.OrdinalIgnoreCase)
        || string.Equals(choice, "quit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(choice, "exit", StringComparison.OrdinalIgnoreCase))
    {
        return 0;
    }

    if (string.Equals(choice, "e", StringComparison.OrdinalIgnoreCase)
        || string.Equals(choice, "engines", StringComparison.OrdinalIgnoreCase))
    {
        await ShowEnginesAsync(httpClient);
        continue;
    }

    if (string.Equals(choice, "m", StringComparison.OrdinalIgnoreCase)
        || string.Equals(choice, "menu", StringComparison.OrdinalIgnoreCase))
    {
        PrintMenu(examples);
        continue;
    }

    if (string.Equals(choice, "all", StringComparison.OrdinalIgnoreCase)
        || choice == "0")
    {
        foreach (var example in examples)
        {
            await example.Run(httpClient);
        }

        continue;
    }

    var selected = FindExample(examples, choice);
    if (selected is null)
    {
        Console.WriteLine($"Unknown choice '{choice}'. Type a number, 'all', 'engines', or 'q'.");
        Console.WriteLine();
        continue;
    }

    await selected.Run(httpClient);
}

static List<EngineExample> BuildExamples()
{
    const string customerPayload = """{"document":"hello","customer":{"name":"Ada Lovelace"}}""";

    /*
    File.WriteAllBytes("template.docx", OfficeFixtures.CreateXlsxTemplateRows(
                        ["{{title}}"],
                        ["Customer:", "{{customer}}", "Date:", "{{date}}"],
                        ["Item", "Qty", "Unit price"],
                        ["{{items.name}}", "{{items.qty}}", "{{items.price}}"]));
    */

    return
    [
        new EngineExample(
            "1",
            "fake",
            "json -> txt",
            "Echoes the request metadata and payload back as text",
            client => RunSyncAsync(client, TextRequest("fake", "json", "txt", null, customerPayload, null), "fake")),

        new EngineExample(
            "2",
            "scriban",
            "json -> txt",
            "Template rendering through sync and async submit/poll/download flows",
            async client =>
            {
                var request = TextRequest(
                    "scriban", "json", "txt", "scriban", customerPayload,
                    "Document: {{ document }} for {{ customer.name }}");

                await RunSyncAsync(client, request, "scriban text");
            }),

        new EngineExample(
            "3",
            "scriban",
            "json -> html",
            "Renders an HTML fragment from the same JSON payload",
            client => RunSyncAsync(
                client,
                TextRequest(
                    "scriban", "json", "html", "html", customerPayload,
                    "<h1>{{ document }}</h1><p>Customer: {{ customer.name }}</p>"),
                "scriban html")),

        new EngineExample(
            "4",
            "scriban",
            "json -> md",
            "Renders Markdown from the same JSON payload",
            client => RunSyncAsync(
                client,
                TextRequest(
                    "scriban", "json", "md", "md", customerPayload,
                    "# {{ document }}\n\nCustomer: **{{ customer.name }}**"),
                "scriban markdown")),

        new EngineExample(
            "5",
            "dotliquid",
            "json -> txt",
            "Liquid syntax rendering of the same payload",
            client => RunSyncAsync(
                client,
                TextRequest(
                    "dotliquid", "json", "txt", "liquid", customerPayload,
                    "Document: {{ document }} for {{ customer.name }}"),
                "dotliquid text")),

        new EngineExample(
            "6",
            "questpdf",
            "json -> pdf",
            "Builds a PDF document from JSON, saved to the output folder",
            client => RunSyncAsync(
                client,
                TextRequest(
                    "questpdf", "json", "pdf", null,
                    """{"title":"Quarterly Report","document":"Revenue growth is positive"}""",
                    null),
                "questpdf")),

        new EngineExample(
            "7",
            "miniexcel",
            "json -> xlsx",
            "Table path: a JSON array becomes a worksheet, saved to the output folder",
            client => RunSyncAsync(
                client,
                TextRequest(
                    "miniexcel", "json", "xlsx", null,
                    """[{"Name":"Alice","Age":30,"City":"London"},{"Name":"Bob","Age":25,"City":"Paris"}]""",
                    null),
                "miniexcel table")),

        new EngineExample(
            "8",
            "fop",
            "xsl-fo -> pdf",
            "Calls the external FOP converter, so it needs that stack running",
            client => RunSyncAsync(client, TextRequest("fop", "xsl-fo", "pdf", null, FopExample.XslFoPayload, null), "fop")),

        new EngineExample(
            "9",
            "(auto)",
            "json -> txt",
            "Omits the engine so the API picks the highest-priority match",
            client => RunSyncAsync(
                client,
                TextRequest(
                    null, "json", "txt", "scriban", customerPayload,
                    "Document: {{ document }} for {{ customer.name }}"),
                "auto-selected engine")),

        new EngineExample(
            "10",
            "(auto)",
            "json -> pdf",
            "Auto-selection resolving to questpdf for a PDF output",
            client => RunSyncAsync(
                client,
                TextRequest(
                    null, "json", "pdf", null,
                    """{"title":"Auto-selected PDF","document":"Chosen by capability and priority"}""",
                    null),
                "auto-selected pdf")),

        new EngineExample(
            "11",
            "miniword",
            "json -> docx",
            "Populates a binary .docx template sent as base64",
            client => RunSyncAsync(
                client,
                BinaryRequest(
                    "miniword", "json", "docx", "docx",
                    payload: """{"Name":"Ada Lovelace","Score":99}""",
                    templateBase64: Convert.ToBase64String(
                        OfficeFixtures.CreateDocxTemplate("Student: {{Name}}, Score: {{Score}}"))),
                "miniword")),

        new EngineExample(
            "12",
            "miniexcel",
            "json -> xlsx",
            "Template path: populates a binary .xlsx template sent as base64",
            client => RunSyncAsync(
                client,
                BinaryRequest(
                    "miniexcel", "json", "xlsx", "xlsx",
                    payload: """{"title":"Invoice INV-2026-001","customer":"Ada Lovelace"}""",
                    templateBase64: Convert.ToBase64String(
                        OfficeFixtures.CreateXlsxTemplate("{{title}}", "Customer: {{customer}}"))),
                "miniexcel template")),

        new EngineExample(
            "13",
            "minipdf",
            "xlsx -> pdf",
            "Chains a generated workbook into PDF conversion through a binary payload",
            async client =>
            {
                var workbook = await PostForBytesAsync(
                    client,
                    TextRequest(
                        "miniexcel", "json", "xlsx", null,
                        """[{"Name":"Alice","Age":30},{"Name":"Bob","Age":25}]""",
                        null),
                    "minipdf source workbook");

                if (workbook is null)
                {
                    return;
                }

                await RunSyncAsync(
                    client,
                    BinaryRequest("minipdf", "xlsx", "pdf", null, payloadBase64: Convert.ToBase64String(workbook)),
                    "minipdf");
            }),

        new EngineExample(
            "14",
            "miniword",
            "json -> docx",
            "Invoice: a multi-paragraph .docx template filled from one JSON object",
            client => RunSyncAsync(
                client,
                BinaryRequest(
                    "miniword", "json", "docx", "docx",
                    payload: """
                        {"title":"INV-2026-001","customer":"Ada Lovelace","date":"2026-11-07","item":"Analytical Engine","quantity":2,"unitPrice":1250.55,"total":2501.1,"notes":"Thank you for your business."}
                        """,
                    templateBase64: Convert.ToBase64String(
                        OfficeFixtures.CreateDocxTemplate(OfficeFixtures.InvoiceDocxTemplate))),
                "miniword invoice")),

        new EngineExample(
            "14.1",
            "miniword",
            "json -> docx",
            "Invoice: a multi-paragraph .docx template filled from one JSON object, template: word file docx",
            client => RunSyncAsync(
                client,
                BinaryRequest(
                    "miniword", "json", "docx", "docx",
                    payload: """
                        {"title":"INV-2026-001","customer":"Ada Lovelace","date":"2026-11-07","item":"Analytical Engine","quantity":2,"unitPrice":1250.55,"total":2501.1,"notes":"Thank you for your business."}
                        """,
                    templateBase64: Convert.ToBase64String(
                        OfficeFixtures.TemplateRows("template.docx"))),
                "miniword invoice")),

        new EngineExample(
            "15",
            "miniexcel",
            "json -> xlsx",
            "Invoice: scalar placeholders plus a repeating {{items.*}} data row",
            client => RunSyncAsync(
                client,
                BinaryRequest(
                    "miniexcel", "json", "xlsx", "xlsx",
                    payload: OfficeFixtures.InvoiceXlsxPayload,
                    templateBase64: Convert.ToBase64String(OfficeFixtures.CreateXlsxTemplateRows(
                        ["{{title}}"],
                        ["Customer:", "{{customer}}", "Date:", "{{date}}"],
                        ["Item", "Qty", "Unit price"],
                        ["{{items.name}}", "{{items.qty}}", "{{items.price}}"]))),
                "miniexcel invoice"))

        ,

        new EngineExample(
            "15.1",
            "miniexcel",
            "json -> xlsx",
            "Invoice: scalar placeholders plus a repeating {{items.*}} data row",
            client => RunSyncAsync(
                client,
                BinaryRequest(
                    "miniexcel", "json", "xlsx", "xlsx",
                    payload: OfficeFixtures.InvoiceXlsxPayload,
                    templateBase64: Convert.ToBase64String(OfficeFixtures.TemplateRows("template.xlsx"))),
                "miniexcel invoice"))
    ];
}

static GenerateDocumentHttpRequest BinaryRequest(
    string? engine,
    string inputFormat,
    string outputFormat,
    string? templateFormat,
    string? payload = null,
    string? payloadBase64 = null,
    string? templateBase64 = null)
    => new(
        RequestId: Guid.NewGuid().ToString("N"),
        Engine: engine,
        InputFormat: inputFormat,
        OutputFormat: outputFormat,
        TemplateFormat: templateFormat,
        Payload: payload,
        Template: null,
        Metadata: new Dictionary<string, string>
        {
            ["client"] = "sample"
        },
        PayloadBase64: payloadBase64,
        TemplateBase64: templateBase64);

static GenerateDocumentHttpRequest TextRequest(
    string? engine,
    string inputFormat,
    string outputFormat,
    string? templateFormat,
    string payload,
    string? template)
    => new(
        RequestId: Guid.NewGuid().ToString("N"),
        Engine: engine,
        InputFormat: inputFormat,
        OutputFormat: outputFormat,
        TemplateFormat: templateFormat,
        Payload: payload,
        Template: template,
        Metadata: new Dictionary<string, string>
        {
            ["client"] = "sample"
        });

static EngineExample? FindExample(List<EngineExample> examples, string choice)
    => examples.FirstOrDefault(example =>
        string.Equals(example.Id, choice, StringComparison.OrdinalIgnoreCase)
        || string.Equals(example.Engine, choice, StringComparison.OrdinalIgnoreCase));

static async Task ShowEnginesAsync(HttpClient httpClient)
{
    PrintSection("Registered engines");

    try
    {
        var response = await httpClient.GetAsync("/api/v1/engines");
        Console.WriteLine($"Status: {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            await PrintBodyAsync(response);
            Console.WriteLine();
            return;
        }

        var engines = await response.Content.ReadFromJsonAsync<DocumentEngineHttpResponse[]>();
        if (engines is null || engines.Length == 0)
        {
            Console.WriteLine("No engines returned by the API.");
            Console.WriteLine();
            return;
        }

        foreach (var engine in engines.OrderByDescending(e => e.Priority))
        {
            Console.WriteLine($"- {engine.Name} (priority: {engine.Priority})");
            Console.WriteLine($"  input: {string.Join(", ", engine.InputFormats)}");
            Console.WriteLine($"  output: {string.Join(", ", engine.OutputFormats)}");
            Console.WriteLine($"  templates: {(engine.TemplateFormats.Count == 0 ? "(none)" : string.Join(", ", engine.TemplateFormats))}");
        }
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Could not reach the API: {exception.Message}");
    }

    Console.WriteLine();
}

static async Task RunSyncAsync(HttpClient httpClient, GenerateDocumentHttpRequest request, string scenario)
{
    PrintSection($"{scenario} (sync)");
    Console.WriteLine($"POST /api/v1/documents/sync  engine={request.Engine ?? "(auto)"}  {request.InputFormat} -> {request.OutputFormat}");

    try
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/documents/sync", request);
        Console.WriteLine($"Status: {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            await PrintBodyAsync(response);
            Console.WriteLine();
        }
        else
        {
            await ReportContentAsync(response, $"document.{request.OutputFormat}");
        }
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Request failed: {exception.Message}");
    }

    await RunAsyncFlowAsync(httpClient, request, scenario);
    Console.WriteLine();
}

static async Task<byte[]?> PostForBytesAsync(HttpClient httpClient, GenerateDocumentHttpRequest request, string scenario)
{
    PrintSection($"{scenario} (sync)");
    Console.WriteLine($"POST /api/v1/documents/sync  engine={request.Engine ?? "(auto)"}  {request.InputFormat} -> {request.OutputFormat}");

    try
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/documents/sync", request);
        Console.WriteLine($"Status: {(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            await PrintBodyAsync(response);
            Console.WriteLine();
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Console.WriteLine($"Received {bytes.Length} bytes");
        Console.WriteLine();

        return bytes;
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Request failed: {exception.Message}");
        Console.WriteLine();
        return null;
    }
}

static async Task RunAsyncFlowAsync(HttpClient httpClient, GenerateDocumentHttpRequest request, string scenario)
{
    PrintSection($"{scenario} (async)");
    Console.WriteLine($"POST /api/v1/documents/async  engine={request.Engine ?? "(auto)"}  {request.InputFormat} -> {request.OutputFormat}");

    try
    {
        var submit = await httpClient.PostAsJsonAsync("/api/v1/documents/async", request);
        Console.WriteLine($"Submit status: {(int)submit.StatusCode}");

        if (!submit.IsSuccessStatusCode)
        {
            await PrintBodyAsync(submit);
            Console.WriteLine();
            return;
        }

        var accepted = await submit.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();
        if (accepted is null)
        {
            Console.WriteLine("Async payload was empty.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"TaskId: {accepted.TaskId}");
        Console.WriteLine($"Initial status: {accepted.Status}");

        var final = await WaitForCompletionAsync(httpClient, accepted.TaskId);
        Console.WriteLine($"Final status: {final.Status}");

        if (final.Status == GenerationTaskStatus.Failed)
        {
            Console.WriteLine($"Error: {final.Error}");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"Result file: {final.ResultFileName}");

        if (string.IsNullOrWhiteSpace(final.DownloadUrl))
        {
            Console.WriteLine("No download URL was returned.");
            Console.WriteLine();
            return;
        }

        var download = await httpClient.GetAsync(final.DownloadUrl);
        Console.WriteLine($"Download status: {(int)download.StatusCode}");

        if (!download.IsSuccessStatusCode)
        {
            await PrintBodyAsync(download);
            Console.WriteLine();
            return;
        }

        await ReportContentAsync(download, final.ResultFileName ?? $"document.{request.OutputFormat}");
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Request failed: {exception.Message}");
    }

    Console.WriteLine();
}

static async Task<TaskStatusHttpResponse> WaitForCompletionAsync(HttpClient httpClient, string taskId)
{
    // Generation may retry transient faults before settling, so allow more than the minimum.
    TaskStatusHttpResponse? last = null;

    for (var attempt = 0; attempt < 120; attempt++)
    {
        var response = await httpClient.GetAsync($"/api/v1/tasks/{taskId}");
        if (response.IsSuccessStatusCode)
        {
            last = await response.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();
            if (last is not null
                && last.Status is not GenerationTaskStatus.Queued and not GenerationTaskStatus.Processing)
            {
                return last;
            }
        }

        await Task.Delay(250);
    }

    return last ?? throw new TimeoutException($"Task '{taskId}' did not report a status in time.");
}

static async Task ReportContentAsync(HttpResponseMessage response, string fallbackFileName)
{
    var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
    var bytes = await response.Content.ReadAsByteArrayAsync();

    Console.WriteLine($"Content-Type: {contentType}  bytes: {bytes.Length}");

    if (IsTextual(contentType))
    {
        Console.WriteLine();
        Console.WriteLine(Encoding.UTF8.GetString(bytes));
        return;
    }

    var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
        ?? response.Content.Headers.ContentDisposition?.FileName
        ?? fallbackFileName;

    var path = SaveOutput(fileName.Trim('"'), bytes);
    Console.WriteLine($"Saved to: {path}");

    if (bytes.Length >= 4)
    {
        Console.WriteLine($"Header bytes: {BitConverter.ToString(bytes, 0, 4)}");
    }
}

static bool IsTextual(string contentType)
{
    // Office media types embed "xml" in their vendor tree, for example
    // application/vnd.openxmlformats-officedocument.spreadsheetml.sheet, so a substring search
    // would dump a binary workbook to the console. Match precisely instead.
    if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
        || contentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase);
}

static string SaveOutput(string fileName, byte[] bytes)
{
    var directory = Path.Combine(AppContext.BaseDirectory, "output");
    Directory.CreateDirectory(directory);

    var safeName = string.Concat(fileName.Split(Path.GetInvalidFileNameChars()));
    var path = Path.Combine(directory, safeName);

    File.WriteAllBytes(path, bytes);

    return path;
}

static async Task PrintBodyAsync(HttpResponseMessage response)
{
    var body = await response.Content.ReadAsStringAsync();
    if (!string.IsNullOrWhiteSpace(body))
    {
        Console.WriteLine($"Response: {body}");
    }
}

static void PrintMenu(List<EngineExample> examples)
{
    PrintSection("Examples");

    foreach (var example in examples)
    {
        Console.WriteLine($"  {example.Id,2}. {example.Engine,-11} {example.Title,-14} {example.Detail}");
    }
}

static void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("  0 or all  run every example      e  list engines      m  redisplay menu      q  quit");
    Console.WriteLine();
}

static void PrintSection(string title)
{
    Console.WriteLine(title);
    Console.WriteLine(new string('-', title.Length));
}

internal static class FopExample
{
    internal const string XslFoPayload = """
        <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format">
          <fo:layout-master-set>
            <fo:simple-page-master master-name="default" page-height="11in" page-width="8.5in" margin="0.75in">
              <fo:region-body/>
            </fo:simple-page-master>
          </fo:layout-master-set>
          <fo:page-sequence master-reference="default">
            <fo:flow flow-name="xsl-region-body">
              <fo:block font-size="18pt" font-weight="bold">GenerateDocService</fo:block>
              <fo:block font-size="12pt" space-before="12pt">Rendered by Apache FOP through the external converter.</fo:block>
              <fo:block font-size="10pt" space-before="12pt">Submitted by the sample client.</fo:block>
            </fo:flow>
          </fo:page-sequence>
        </fo:root>
        """;
}

internal sealed record EngineExample(
    string Id,
    string Engine,
    string Title,
    string Detail,
    Func<HttpClient, Task> Run);

/// <summary>
/// Builds the minimal Office packages the template-driven engines need, so the binary examples are
/// self-contained and do not depend on fixture files on disk.
/// </summary>
internal static class OfficeFixtures
{
    internal const string InvoiceDocxTemplate = """
        INVOICE {{title}}

        Customer: {{customer}}
        Date:     {{date}}

        Item:       {{item}}
        Quantity:   {{quantity}}
        Unit price: {{unitPrice}}
        Total:      {{total}}

        {{notes}}
        """;

    internal const string InvoiceXlsxPayload = """
        {"title":"Invoice INV-2026-001","customer":"Ada Lovelace","date":"2026-11-07","items":[{"name":"Widget","qty":2,"price":9.5},{"name":"Gadget","qty":5,"price":19.99},{"name":"Doohickey","qty":1,"price":4.25}]}
        """;

    internal static byte[] TemplateRows(string path)
    {
        return File.ReadAllBytes(path);
    }

    internal static byte[] CreateDocxTemplate(string textContent)
    {
        // One paragraph per line, so a multi-line template renders as separate lines in Word rather
        // than as a single run containing literal newlines.
        var paragraphs = string.Join(
            string.Empty,
            textContent.Split('\n').Select(line =>
                $"<w:p><w:r><w:t xml:space=\"preserve\">{Escape(line)}</w:t></w:r></w:p>"));

        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "word/document.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>{paragraphs}</w:body>
                </w:document>
                """);
            WriteEntry(archive, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                </Relationships>
                """);
        }

        return stream.ToArray();
    }

    internal static byte[] CreateXlsxTemplate(params string[] cellValues)
        => CreateXlsxTemplateRows(cellValues.Select(value => new[] { value }).ToArray());

    /// <summary>
    /// Builds a workbook template from an explicit row/column layout, which is what a repeating
    /// <c>{{items.field}}</c> data row needs: the tokens for one record must sit side by side.
    /// </summary>
    internal static byte[] CreateXlsxTemplateRows(params string[][] rows)
    {
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                    <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
                    <Default Extension="xml" ContentType="application/xml" />
                    <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" />
                    <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" />
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                    <sheets><sheet name="Invoice" sheetId="1" r:id="rId1" /></sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
                </Relationships>
                """);

            var rowsXml = new StringBuilder();
            for (var r = 0; r < rows.Length; r++)
            {
                var cells = new StringBuilder();
                for (var c = 0; c < rows[r].Length; c++)
                {
                    cells.Append(
                        $"<c r=\"{(char)('A' + c)}{r + 1}\" t=\"inlineStr\"><is><t>{Escape(rows[r][c])}</t></is></c>");
                }

                rowsXml.Append($"<row r=\"{r + 1}\">{cells}</row>");
            }

            WriteEntry(archive, "xl/worksheets/sheet1.xml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                    <sheetData>{rowsXml}</sheetData>
                </worksheet>
                """);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;
}
