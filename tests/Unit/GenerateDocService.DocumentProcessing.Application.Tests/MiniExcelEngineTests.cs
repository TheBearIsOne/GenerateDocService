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
        var header = new byte[4];
        var bytesRead = content.Read(header, 0, 4);
        bytesRead.Should().Be(4);
        // PK signature for ZIP files
        header[0].Should().Be(0x50); // 'P'
        header[1].Should().Be(0x4B); // 'K'

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
}
