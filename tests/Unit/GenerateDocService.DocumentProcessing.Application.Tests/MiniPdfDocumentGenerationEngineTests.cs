using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.MiniExcel;
using GenerateDocService.Engine.MiniPdf;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class MiniPdfDocumentGenerationEngineTests
{
    [Fact]
    public void CanHandle_ShouldAcceptSupportedSourceDocumentsWithoutTemplate()
    {
        var engine = new MiniPdfDocumentGenerationEngine();

        engine.CanHandle("docx", "pdf").Should().BeTrue();
        engine.CanHandle("XLSX", "PDF").Should().BeTrue();
        engine.CanHandle("docx", "pdf", "docx").Should().BeFalse();
        engine.CanHandle("json", "pdf").Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_ShouldHonorCancellation()
    {
        var engine = new MiniPdfDocumentGenerationEngine();
        var request = new GenerateDocumentRequest(
            requestId: "minipdf-test",
            engine: "minipdf",
            inputFormat: "docx",
            outputFormat: "pdf",
            templateFormat: null,
            payload: [1, 2, 3],
            template: null);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var action = async () => await engine.GenerateAsync(request, cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateAsync_ShouldConvertGeneratedSpreadsheetAndWritePdfFile()
    {
        var spreadsheetEngine = new MiniExcelDocumentGenerationEngine();
        var spreadsheetRequest = new GenerateDocumentRequest(
            requestId: "minipdf-source",
            engine: "miniexcel",
            inputFormat: "json",
            outputFormat: "xlsx",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("[{\"Product\":\"Paper\",\"Quantity\":12}]"),
            template: null);
        var spreadsheet = await spreadsheetEngine.GenerateAsync(spreadsheetRequest);
        using var spreadsheetContent = new MemoryStream();
        await spreadsheet.Content.CopyToAsync(spreadsheetContent);
        var spreadsheetBytes = spreadsheetContent.ToArray();
        await TestArtifactWriter.WriteAsync("miniexcel/minipdf-source.xlsx", spreadsheetBytes);

        var engine = new MiniPdfDocumentGenerationEngine();
        var request = new GenerateDocumentRequest(
            requestId: "minipdf-from-xlsx",
            engine: "minipdf",
            inputFormat: "xlsx",
            outputFormat: "pdf",
            templateFormat: null,
            payload: spreadsheetBytes,
            template: null);

        var result = await engine.GenerateAsync(request);
        using var pdfContent = new MemoryStream();
        await result.Content.CopyToAsync(pdfContent);
        var pdfBytes = pdfContent.ToArray();

        pdfBytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdfBytes, 0, 4).Should().Be("%PDF");
        Encoding.UTF8.GetString(pdfBytes).Should().Contain("Paper");
        Encoding.UTF8.GetString(pdfBytes).Should().Contain("Quantity");

        var outputPath = await TestArtifactWriter.WriteAsync("minipdf/minipdf-from-xlsx.pdf", pdfBytes);

        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().Be(pdfBytes.LongLength);
    }
}