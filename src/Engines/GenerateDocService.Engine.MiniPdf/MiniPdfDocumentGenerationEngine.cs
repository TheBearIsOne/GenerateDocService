using System.Text;
using System.Text.Json;
using GenerateDocService.Engine.Abstractions;
using MiniPdfConverter = MiniSoftware.MiniPdf;

namespace GenerateDocService.Engine.MiniPdf;

[DocumentEngine(
    "minipdf",
    InputFormats = ["docx", "xlsx"],
    OutputFormats = ["pdf"],
    TemplateFormats = [],
    Priority = 250)]
public sealed class MiniPdfDocumentGenerationEngine : IDocumentGenerationEngine
{
    public string Name => "minipdf";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => (string.Equals(inputFormat, "docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(inputFormat, "xlsx", StringComparison.OrdinalIgnoreCase))
           && string.Equals(outputFormat, "pdf", StringComparison.OrdinalIgnoreCase);

    public Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException($"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat}.");
        }

        var sourceBytes = request.Payload.ToArray();

        using var inputStream = new MemoryStream(sourceBytes, writable: false);
        var pdfBytes = MiniPdfConverter.ConvertToPdf(inputStream);

        var fileName = $"{request.RequestId}.pdf";

        return Task.FromResult<IReportResult>(new MiniPdfReportResult(
            request.RequestId,
            fileName,
            pdfBytes,
            request.Metadata));
    }

    private sealed class MiniPdfReportResult(
        string requestId,
        string fileName,
        byte[] content,
        IReadOnlyDictionary<string, string> metadata) : IReportResult, IDisposable
    {
        private readonly MemoryStream _content = new(content, writable: false);

        public string RequestId { get; } = requestId;
        public string FileName { get; } = fileName;
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
