using System.IO.Compression;
using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.MiniExcel;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class MiniExcelEngineTests
{
    [Fact]
    public async Task GenerateAsync_ShouldCreateValidXlsxFromJsonArray()
    {
        // Arrange
        var engine = new MiniExcelDocumentGenerationEngine();
        var payload = """
            [
                {"Name": "Alice", "Age": 30, "City": "Moscow"},
                {"Name": "Bob", "Age": 25, "City": "Saint Petersburg"}
            ]
            """;

        var request = new GenerateDocumentRequest(
            requestId: "test-1",
            engine: "miniexcel",
            inputFormat: "json",
            outputFormat: "xlsx",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes(payload),
            template: null,
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("test-1.xlsx");
        result.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        result.OutputFormat.Should().Be("xlsx");
        result.ContentLength.Should().BeGreaterThan(0);

        // Verify it's a valid ZIP/XLSX file (XLSX is a ZIP archive)
        var content = result.Content;
        var artifactBytes = content.ReadFully();
        var outputPath = await TestArtifactWriter.WriteAsync("miniexcel/miniexcel-generated.xlsx", artifactBytes);
        var header = new byte[4];
        Array.Copy(artifactBytes, header, header.Length);
        // PK signature for ZIP files
        header[0].Should().Be(0x50); // 'P'
        header[1].Should().Be(0x4B); // 'K'
        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().Be(artifactBytes.LongLength);

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldCreateXlsxFromSingleJsonObject()
    {
        // Arrange
        var engine = new MiniExcelDocumentGenerationEngine();
        var payload = """
            {"Name": "Charlie", "Score": 95.5, "Passed": true}
            """;

        var request = new GenerateDocumentRequest(
            requestId: "test-2",
            engine: "miniexcel",
            inputFormat: "json",
            outputFormat: "xlsx",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes(payload),
            template: null,
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ContentLength.Should().BeGreaterThan(0);
        result.FileName.Should().Be("test-2.xlsx");

        // Verify ZIP signature
        var content = result.Content;
        var header = new byte[2];
        var bytesRead = content.Read(header, 0, 2);
        bytesRead.Should().Be(2);
        header[0].Should().Be(0x50);
        header[1].Should().Be(0x4B);

        if (result is IDisposable disposable2) disposable2.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldCreateXlsxFromJsonObjectWithArray()
    {
        // Arrange
        var engine = new MiniExcelDocumentGenerationEngine();
        var payload = """
            {
                "title": "Employee List",
                "data": [
                    {"Name": "Alice", "Department": "Engineering"},
                    {"Name": "Bob", "Department": "Marketing"},
                    {"Name": "Charlie", "Department": "Sales"}
                ]
            }
            """;

        var request = new GenerateDocumentRequest(
            requestId: "test-3",
            engine: "miniexcel",
            inputFormat: "json",
            outputFormat: "xlsx",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes(payload),
            template: null,
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ContentLength.Should().BeGreaterThan(0);

        // Verify ZIP signature
        var content = result.Content;
        var header = new byte[2];
        var bytesRead = content.Read(header, 0, 2);
        bytesRead.Should().Be(2);
        header[0].Should().Be(0x50);
        header[1].Should().Be(0x4B);

        if (result is IDisposable disposable3) disposable3.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldPopulateXlsxTemplatePlaceholders()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var template = CreateXlsxTemplate("{{title}}", "Customer: {{customer}}");
        var request = new GenerateDocumentRequest(
            requestId: "miniexcel-template",
            engine: "miniexcel",
            inputFormat: "json",
            outputFormat: "xlsx",
            templateFormat: "xlsx",
            payload: Encoding.UTF8.GetBytes("""{"title":"Invoice INV-2026-001","customer":"Ada Lovelace"}"""),
            template: template,
            metadata: new Dictionary<string, string>());

        var result = await engine.GenerateAsync(request);
        var workbookBytes = result.Content.ReadFully();
        var outputPath = await TestArtifactWriter.WriteAsync("miniexcel/miniexcel-template-generated.xlsx", workbookBytes);
        var worksheetXml = ReadWorkbookXml(workbookBytes);

        worksheetXml.Should().Contain("Invoice INV-2026-001");
        worksheetXml.Should().Contain("Customer: Ada Lovelace");
        worksheetXml.Should().NotContain("{{title}}");
        worksheetXml.Should().NotContain("{{customer}}");
        File.Exists(outputPath).Should().BeTrue();

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public void CanHandle_ShouldReturnTrueForJsonToXlsx()
    {
        var engine = new MiniExcelDocumentGenerationEngine();

        engine.CanHandle("json", "xlsx").Should().BeTrue();
        engine.CanHandle("JSON", "XLSX").Should().BeTrue();
        engine.CanHandle("json", "Xlsx").Should().BeTrue();
    }

    [Fact]
    public void CanHandle_ShouldReturnFalseForOtherFormats()
    {
        var engine = new MiniExcelDocumentGenerationEngine();

        engine.CanHandle("json", "pdf").Should().BeFalse();
        engine.CanHandle("xml", "xlsx").Should().BeFalse();
        engine.CanHandle("csv", "xlsx").Should().BeFalse();
    }

        private static byte[] CreateXlsxTemplate(params string[] cellValues)
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

                        var rows = string.Join(string.Empty, cellValues.Select((value, index) => $"<row r=\"{index + 1}\"><c r=\"A{index + 1}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c></row>"));
                        WriteEntry(archive, "xl/worksheets/sheet1.xml", $"""
                                <?xml version="1.0" encoding="UTF-8"?>
                                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                                    <sheetData>{rows}</sheetData>
                                </worksheet>
                                """);
                }

                return stream.ToArray();
        }

        private static string ReadWorkbookXml(byte[] workbookBytes)
        {
                using var stream = new MemoryStream(workbookBytes, writable: false);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                return reader.ReadToEnd();
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
        }
}
