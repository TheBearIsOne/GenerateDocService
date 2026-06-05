using System.Text;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.Abstractions;
using GenerateDocService.Engine.QuestPdf;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class QuestPdfDocumentGenerationEngineTests
{
    [Fact]
    public async Task GenerateAsync_ShouldProducePdfDocument()
    {
        IDocumentGenerationEngine engine = new QuestPdfDocumentGenerationEngine();
        var request = new GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: "questpdf",
            inputFormat: "json",
            outputFormat: "pdf",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("{\"title\":\"Quarterly Report\",\"document\":\"hello\"}"),
            template: null,
            metadata: new Dictionary<string, string>
            {
                ["client"] = "test"
            });

        var result = await engine.GenerateAsync(request);
        result.Content.Position = 0;
        using var stream = new MemoryStream();
        await result.Content.CopyToAsync(stream);
        var bytes = stream.ToArray();

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes.Take(4).ToArray()).Should().Be("%PDF");
        result.ContentType.Should().Be("application/pdf");
        result.OutputFormat.Should().Be("pdf");
    }

    [Fact]
    public void CanHandle_ShouldReturnTrue_ForJsonToPdf()
    {
        IDocumentGenerationEngine engine = new QuestPdfDocumentGenerationEngine();

        var canHandle = engine.CanHandle("json", "pdf");

        canHandle.Should().BeTrue();
    }
}
