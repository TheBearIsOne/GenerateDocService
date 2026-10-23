using System.Net.Http.Headers;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.Engine.Fop;

[DocumentEngine(
    "fop",
    InputFormats = ["fo", "xsl-fo"],
    OutputFormats = ["pdf"],
    TemplateFormats = [],
    Priority = 300)]
public sealed class FopDocumentGenerationEngine(HttpClient httpClient) : IDocumentGenerationEngine
{
    public const string HttpClientName = "fop-converter";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string Name => "fop";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => (string.Equals(inputFormat, "fo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(inputFormat, "xsl-fo", StringComparison.OrdinalIgnoreCase))
           && string.Equals(outputFormat, "pdf", StringComparison.OrdinalIgnoreCase)
           && string.IsNullOrWhiteSpace(templateFormat);

    public async Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException($"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat} with template '{request.TemplateFormat}'.");
        }

        using var content = new MultipartFormDataContent();
        using var primaryDocument = new ByteArrayContent(request.Payload.ToArray());
        primaryDocument.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        content.Add(primaryDocument, "primaryDocument", $"{request.RequestId}.fo");

        const string payloadType = "XSL_FO";
        const string outputFormat = "PDF";
        var requestUri = $"api/v1/convert/sync?payloadType={payloadType}&outputFormat={outputFormat}&correlationId={Uri.EscapeDataString(request.RequestId)}";

        using var response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var pdf = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (pdf.Length == 0)
        {
            throw new InvalidOperationException("FOP converter returned an empty document.");
        }

        return new FopReportResult(request.RequestId, pdf, request.Metadata);
    }

    private sealed class FopReportResult(
        string requestId,
        byte[] content,
        IReadOnlyDictionary<string, string> metadata) : IReportResult, IDisposable
    {
        private readonly MemoryStream _content = new(content, writable: false);

        public string RequestId { get; } = requestId;
        public string FileName { get; } = $"{requestId}.pdf";
        public string ContentType { get; } = "application/pdf";
        public string OutputFormat { get; } = "pdf";
        public long ContentLength { get; } = content.LongLength;
        public Stream Content => _content;
        public string? Checksum => null;
        public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

        public void Dispose()
        {
            _content.Dispose();
        }
    }
}