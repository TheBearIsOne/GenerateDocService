using System.Text;
using BenchmarkDotNet.Attributes;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.Engine.Abstractions;
using GenerateDocService.Engine.DotLiquid;
using GenerateDocService.Engine.MiniExcel;
using GenerateDocService.Engine.MiniWord;
using GenerateDocService.Engine.QuestPdf;
using GenerateDocService.Engine.Scriban;

namespace GenerateDocService.SampleClient.Benchmarks;

/// <summary>
/// In-process microbenchmarks for the generation hot paths, complementing the NBomber profiles in
/// tests/Performance which measure the service end to end over HTTP. Nothing here needs a running
/// API, Docker, or the external FOP converter.
/// </summary>
/// <remarks>
/// Every payload, template and request is built in <c>[GlobalSetup]</c> so the measured methods
/// allocate nothing but what the engine itself allocates.
/// Run in Release only; BenchmarkDotNet rejects Debug builds:
///   dotnet run -c Release --project src/Clients/GenerateDocService.SampleClient -- benchmark
/// </remarks>
[MemoryDiagnoser]
public class EngineGenerationBenchmarks
{
    private const string CustomerPayload = """{"document":"hello","customer":{"name":"Ada Lovelace"}}""";
    private const string Template = "Document: {{ document }} for {{ customer.name }}";
    private const string HtmlTemplate = "<h1>{{ document }}</h1><p>Customer: {{ customer.name }}</p>";
    private const string PdfPayload = """{"title":"Quarterly Report","document":"Revenue growth is positive"}""";
    private const string RowsPayload = """
        [{"Name":"Alice","Age":30,"City":"London"},{"Name":"Bob","Age":25,"City":"Paris"},{"Name":"Carol","Age":41,"City":"Berlin"}]
        """;
    private const string InvoicePayload = """{"title":"Invoice INV-2026-001","customer":"Ada Lovelace","total":2501.1}""";
    private const string WordPayload = """{"Name":"Ada Lovelace","Score":99}""";

    private readonly ScribanDocumentGenerationEngine _scriban = new();
    private readonly DotLiquidDocumentGenerationEngine _dotLiquid = new();
    private readonly QuestPdfDocumentGenerationEngine _questPdf = new();
    private readonly MiniExcelDocumentGenerationEngine _miniExcel = new();
    private readonly MiniWordDocumentGenerationEngine _miniWord = new();

    private GenerateDocumentRequest _scribanText = null!;
    private GenerateDocumentRequest _scribanHtml = null!;
    private GenerateDocumentRequest _dotLiquidText = null!;
    private GenerateDocumentRequest _miniExcelTable = null!;
    private GenerateDocumentRequest _miniExcelTemplate = null!;
    private GenerateDocumentRequest _miniWordTemplate = null!;
    private GenerateDocumentRequest _questPdfRequest = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scribanText = Build("scriban", "txt", "scriban", CustomerPayload, Utf8(Template));
        _scribanHtml = Build("scriban", "html", "html", CustomerPayload, Utf8(HtmlTemplate));
        _dotLiquidText = Build("dotliquid", "txt", "liquid", CustomerPayload, Utf8(Template));
        _miniExcelTable = Build("miniexcel", "xlsx", null, RowsPayload, null);
        _miniExcelTemplate = Build(
            "miniexcel", "xlsx", "xlsx", InvoicePayload,
            OfficeFixtures.CreateXlsxTemplate("{{title}}", "Customer: {{customer}}", "Total: {{total}}"));
        _miniWordTemplate = Build(
            "miniword", "docx", "docx", WordPayload,
            OfficeFixtures.CreateDocxTemplate("Student: {{Name}}, Score: {{Score}}"));
        _questPdfRequest = Build("questpdf", "pdf", null, PdfPayload, null);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("text")]
    public Task<long> ScribanJsonToText() => RunAsync(_scriban, _scribanText);

    [Benchmark]
    [BenchmarkCategory("text")]
    public Task<long> ScribanJsonToHtml() => RunAsync(_scriban, _scribanHtml);

    [Benchmark]
    [BenchmarkCategory("text")]
    public Task<long> DotLiquidJsonToText() => RunAsync(_dotLiquid, _dotLiquidText);

    [Benchmark]
    [BenchmarkCategory("office")]
    public Task<long> MiniExcelJsonToWorkbook() => RunAsync(_miniExcel, _miniExcelTable);

    [Benchmark]
    [BenchmarkCategory("office")]
    public Task<long> MiniExcelJsonToTemplatedWorkbook() => RunAsync(_miniExcel, _miniExcelTemplate);

    [Benchmark]
    [BenchmarkCategory("office")]
    public Task<long> MiniWordJsonToDocument() => RunAsync(_miniWord, _miniWordTemplate);

    [Benchmark]
    [BenchmarkCategory("pdf")]
    public Task<long> QuestPdfJsonToPdf() => RunAsync(_questPdf, _questPdfRequest);

    private static GenerateDocumentRequest Build(
        string engine,
        string outputFormat,
        string? templateFormat,
        string payload,
        byte[]? template)
        => new(
            requestId: "benchmark",
            engine: engine,
            inputFormat: "json",
            outputFormat: outputFormat,
            templateFormat: templateFormat,
            payload: Encoding.UTF8.GetBytes(payload),
            template: template);

    private static byte[]? Utf8(string? text) => text is null ? null : Encoding.UTF8.GetBytes(text);

    private static async Task<long> RunAsync(IDocumentGenerationEngine engine, GenerateDocumentRequest request)
    {
        var result = await engine.GenerateAsync(request);

        // Engines return their own IDisposable result wrappers; IReportResult does not extend it.
        (result as IDisposable)?.Dispose();

        return result.ContentLength;
    }
}

[MemoryDiagnoser]
public class TemplateParserCacheBenchmarks
{
    private const string Template = "Document: {{ document }} for {{ customer.name }}";

    private byte[] _templateBytes = [];
    private ScribanTemplateParser _scribanCold = null!;
    private ScribanTemplateParser _scribanWarm = null!;
    private DotLiquidTemplateParser _liquidCold = null!;
    private DotLiquidTemplateParser _liquidWarm = null!;

    [GlobalSetup]
    public void Setup()
    {
        _templateBytes = Encoding.UTF8.GetBytes(Template);

        // The parameterless constructors use NullCompiledTemplateCache, so every call recompiles.
        _scribanCold = new ScribanTemplateParser();
        _liquidCold = new DotLiquidTemplateParser();

        _scribanWarm = new ScribanTemplateParser(new InMemoryCompiledTemplateCache());
        _liquidWarm = new DotLiquidTemplateParser(new InMemoryCompiledTemplateCache());

        // Prime the warm parsers so the benchmark measures the hit path, not the first compile.
        _scribanWarm.ParseAsync(_templateBytes).GetAwaiter().GetResult();
        _liquidWarm.ParseAsync(_templateBytes).GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("scriban")]
    public Task<TemplateParseResult> ScribanParseCacheMiss() => _scribanCold.ParseAsync(_templateBytes);

    [Benchmark]
    [BenchmarkCategory("scriban")]
    public Task<TemplateParseResult> ScribanParseCacheHit() => _scribanWarm.ParseAsync(_templateBytes);

    [Benchmark]
    [BenchmarkCategory("dotliquid")]
    public Task<TemplateParseResult> DotLiquidParseCacheMiss() => _liquidCold.ParseAsync(_templateBytes);

    [Benchmark]
    [BenchmarkCategory("dotliquid")]
    public Task<TemplateParseResult> DotLiquidParseCacheHit() => _liquidWarm.ParseAsync(_templateBytes);
}

[MemoryDiagnoser]
public class RequestPipelineBenchmarks
{
    private const int LargePayloadBytes = 1024 * 1024;

    private GenerateDocumentRequest _smallRequest = null!;
    private GenerateDocumentRequest _largeRequest = null!;
    private string _largeBase64 = null!;
    private byte[] _largePayloadBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallRequest = BuildRequest("""{"document":"hello","customer":{"name":"Ada Lovelace"}}""");

        var rows = new StringBuilder("[");
        for (var i = 0; rows.Length < LargePayloadBytes; i++)
        {
            if (i > 0)
            {
                rows.Append(',');
            }

            rows.Append("{\"Name\":\"Customer ").Append(i).Append("\",\"Age\":").Append(i % 90).Append('}');
        }

        var largePayload = rows.Append(']').ToString();
        _largeRequest = BuildRequest(largePayload);
        _largePayloadBytes = Encoding.UTF8.GetBytes(largePayload);
        _largeBase64 = Convert.ToBase64String(_largePayloadBytes);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("fingerprint")]
    public string FingerprintSmallRequest() => DocumentRequestFingerprintProvider.Create(_smallRequest);

    [Benchmark]
    [BenchmarkCategory("fingerprint")]
    public string FingerprintOneMegabyteRequest() => DocumentRequestFingerprintProvider.Create(_largeRequest);

    [Benchmark]
    [BenchmarkCategory("transport")]
    public int EncodeOneMegabyteAsBase64() => Convert.ToBase64String(_largePayloadBytes).Length;

    [Benchmark]
    [BenchmarkCategory("transport")]
    public int DecodeOneMegabyteFromBase64() => Convert.FromBase64String(_largeBase64).Length;

    private static GenerateDocumentRequest BuildRequest(string payload)
        => new(
            requestId: "benchmark",
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes("Document: {{ document }}"),
            metadata: new Dictionary<string, string> { ["client"] = "benchmark" });
}
