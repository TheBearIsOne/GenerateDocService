using System.IO.Compression;
using System.Xml.Linq;
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
           && string.Equals(outputFormat, "pdf", StringComparison.OrdinalIgnoreCase)
           && string.IsNullOrWhiteSpace(templateFormat);

    public Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException($"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat}.");
        }

        var sourceBytes = request.Payload.ToArray();
        if (string.Equals(request.InputFormat, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            sourceBytes = NormalizeXlsxStringCells(sourceBytes);
        }

        using var inputStream = new MemoryStream(sourceBytes, writable: false);
        var pdfBytes = MiniPdfConverter.ConvertToPdf(inputStream);

        var fileName = $"{request.RequestId}.pdf";

        return Task.FromResult<IReportResult>(new MiniPdfReportResult(
            request.RequestId,
            fileName,
            pdfBytes,
            request.Metadata));
    }

    private static byte[] NormalizeXlsxStringCells(byte[] sourceBytes)
    {
        using var sourceStream = new MemoryStream(sourceBytes, writable: false);
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);
        using var outputStream = new MemoryStream();
        using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sourceEntry in sourceArchive.Entries)
            {
                var outputEntry = outputArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
                using var outputEntryStream = outputEntry.Open();

                if (!sourceEntry.FullName.StartsWith("xl/", StringComparison.Ordinal)
                    || !sourceEntry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var sourceEntryStream = sourceEntry.Open();
                    sourceEntryStream.CopyTo(outputEntryStream);
                    continue;
                }

                using var worksheetStream = sourceEntry.Open();
                var worksheet = XDocument.Load(worksheetStream);
                var spreadsheetNamespace = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                if (worksheet.Root?.Name.Namespace != spreadsheetNamespace)
                {
                    worksheet.Save(outputEntryStream);
                    continue;
                }

                worksheet.Root.SetAttributeValue(XNamespace.Xmlns + "x", null);
                worksheet.Root.SetAttributeValue("xmlns", spreadsheetNamespace.NamespaceName);

                if (!sourceEntry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal))
                {
                    worksheet.Save(outputEntryStream);
                    continue;
                }

                var stringCells = worksheet
                    .Descendants(spreadsheetNamespace + "c")
                    .Where(cell => string.Equals((string?)cell.Attribute("t"), "str", StringComparison.Ordinal)
                                   && cell.Element(spreadsheetNamespace + "f") is null)
                    .ToArray();

                foreach (var cell in stringCells)
                {
                    var value = cell.Element(spreadsheetNamespace + "v");
                    if (value is null)
                    {
                        continue;
                    }

                    cell.SetAttributeValue("t", "inlineStr");
                    value.ReplaceWith(new XElement(
                        spreadsheetNamespace + "is",
                        new XElement(spreadsheetNamespace + "t", value.Value)));
                }

                worksheet.Save(outputEntryStream);
            }
        }

        return outputStream.ToArray();
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
