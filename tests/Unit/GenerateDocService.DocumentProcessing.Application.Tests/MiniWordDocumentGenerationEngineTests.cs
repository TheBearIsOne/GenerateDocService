using System.Globalization;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.MiniWord;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class MiniWordDocumentGenerationEngineTests
{
    [Fact]
    public void CanHandle_ShouldRequireADocxTemplateFormat()
    {
        var engine = new MiniWordDocumentGenerationEngine();

        engine.CanHandle("json", "docx", "docx").Should().BeTrue();
        engine.CanHandle("json", "docx", "miniword").Should().BeTrue();
        engine.CanHandle("json", "docx").Should().BeFalse();
        engine.CanHandle("json", "pdf", "docx").Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRejectAnEmptyTemplate()
    {
        var engine = new MiniWordDocumentGenerationEngine();
        var request = CreateRequest(template: []);

        var action = async () => await engine.GenerateAsync(request);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires a non-empty .docx template*");
    }

    [Fact]
    public async Task GenerateAsync_ShouldHonorCancellation()
    {
        var engine = new MiniWordDocumentGenerationEngine();
        var request = CreateRequest(template: []);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var action = async () => await engine.GenerateAsync(request, cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateAsync_ShouldFormatFractionalNumbersInvariantly_UnderAnyAmbientCulture()
    {
        // MiniWord renders values with the ambient culture, so this produced 1234,56 on a ru-RU host.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

            var engine = new MiniWordDocumentGenerationEngine();
            var request = new GenerateDocumentRequest(
                requestId: "miniword-culture",
                engine: "miniword",
                inputFormat: "json",
                outputFormat: "docx",
                templateFormat: "docx",
                payload: Encoding.UTF8.GetBytes("""{"total":1234.56,"qty":3}"""),
                template: CreateDocxTemplate("Total: {{total}} Qty: {{qty}}"));

            var result = await engine.GenerateAsync(request);
            using var stream = new MemoryStream();
            await result.Content.CopyToAsync(stream);
            var documentXml = ReadDocumentXml(stream.ToArray());

            documentXml.Should().Contain("Total: 1234.56");
            documentXml.Should().NotContain("1234,56");
            documentXml.Should().Contain("Qty: 3");

            if (result is IDisposable disposable) disposable.Dispose();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static GenerateDocumentRequest CreateRequest(byte[] template)
        => new(
            requestId: "miniword-test",
            engine: "miniword",
            inputFormat: "json",
            outputFormat: "docx",
            templateFormat: "docx",
            payload: Encoding.UTF8.GetBytes("{\"name\":\"Ada\"}"),
            template: template);

    private static byte[] CreateDocxTemplate(string textContent)
    {
        var paragraphs = string.Join(
            string.Empty,
            textContent.Split('\n').Select(line =>
                $"<w:p><w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(line)}</w:t></w:r></w:p>"));

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
        }

        return stream.ToArray();
    }

    private static string ReadDocumentXml(byte[] docxBytes)
    {
        using var stream = new MemoryStream(docxBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }
}