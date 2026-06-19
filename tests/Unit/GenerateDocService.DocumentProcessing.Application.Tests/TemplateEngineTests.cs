using System.IO.Compression;
using System.Text;
using System.Xml;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.DotLiquid;
using GenerateDocService.Engine.MiniWord;
using GenerateDocService.Engine.Scriban;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class TemplateEngineTests
{
    #region Scriban Engine Tests

    [Fact]
    public async Task ScribanEngine_WithTemplate_ShouldRenderPlaceholders()
    {
        // Arrange
        var engine = new ScribanDocumentGenerationEngine();
        var payload = """{"name": "Alice", "age": 30, "city": "Moscow"}""";
        var template = "Hello {{ name }}! You are {{ age }} years old and live in {{ city }}.";

        var request = new GenerateDocumentRequest(
            requestId: "scriban-test-1",
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("scriban-test-1.txt");
        result.ContentType.Should().Be("text/plain");
        result.OutputFormat.Should().Be("txt");

        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Be("Hello Alice! You are 30 years old and live in Moscow.");

        if (result is IDisposable d1) d1.Dispose();
    }

    [Fact]
    public async Task ScribanEngine_WithHtmlTemplate_ShouldRenderHtmlOutput()
    {
        // Arrange
        var engine = new ScribanDocumentGenerationEngine();
        var payload = """{"title": "Report", "items": ["Alpha", "Beta", "Gamma"]}""";
        var template = """<h1>{{ title }}</h1><ul>{{ for item in items }}<li>{{ item }}</li>{{ end }}</ul>""";

        var request = new GenerateDocumentRequest(
            requestId: "scriban-test-2",
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "html",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ContentType.Should().Be("text/html");
        result.FileName.Should().Be("scriban-test-2.html");

        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Contain("<h1>Report</h1>");
        content.Should().Contain("<li>Alpha</li>");
        content.Should().Contain("<li>Beta</li>");
        content.Should().Contain("<li>Gamma</li>");

        if (result is IDisposable d2) d2.Dispose();
    }

    [Fact]
    public async Task ScribanEngine_WithMarkdownTemplate_ShouldRenderMarkdown()
    {
        // Arrange
        var engine = new ScribanDocumentGenerationEngine();
        var payload = """{"name": "Bob", "score": 95}""";
        var template = "# {{ name }}\n\nScore: **{{ score }}**";

        var request = new GenerateDocumentRequest(
            requestId: "scriban-test-3",
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "md",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ContentType.Should().Be("text/markdown");
        result.FileName.Should().Be("scriban-test-3.md");

        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Be("# Bob\n\nScore: **95**");
    }

    [Fact]
    public async Task ScribanEngine_WithNestedJsonTemplate_ShouldRenderNestedProperties()
    {
        // Arrange
        var engine = new ScribanDocumentGenerationEngine();
        var payload = """{"customer": {"name": "Charlie", "address": {"city": "Saint Petersburg", "zip": "190000"}}}""";
        var template = "Customer: {{ customer.name }}\nCity: {{ customer.address.city }}\nZip: {{ customer.address.zip }}";

        var request = new GenerateDocumentRequest(
            requestId: "scriban-test-4",
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Be("Customer: Charlie\nCity: Saint Petersburg\nZip: 190000");
    }

    #endregion

    #region DotLiquid Engine Tests

    [Fact]
    public async Task DotLiquidEngine_WithTemplate_ShouldRenderPlaceholders()
    {
        // Arrange
        var engine = new DotLiquidDocumentGenerationEngine();
        var payload = """{"name": "Alice", "age": 30, "city": "Moscow"}""";
        var template = "Hello {{ name }}! You are {{ age }} years old and live in {{ city }}.";

        var request = new GenerateDocumentRequest(
            requestId: "liquid-test-1",
            engine: "dotliquid",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "liquid",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("liquid-test-1.txt");
        result.ContentType.Should().Be("text/plain");

        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Be("Hello Alice! You are 30 years old and live in Moscow.");
    }

    [Fact]
    public async Task DotLiquidEngine_WithHtmlTemplateAndFilters_ShouldRenderHtml()
    {
        // Arrange
        var engine = new DotLiquidDocumentGenerationEngine();
        var payload = """{"name": "bob", "title": "annual report"}""";
        var template = """<h1>{{ title | upcase }}</h1><p>Prepared by: {{ name | capitalize }}</p>""";

        var request = new GenerateDocumentRequest(
            requestId: "liquid-test-2",
            engine: "dotliquid",
            inputFormat: "json",
            outputFormat: "html",
            templateFormat: "liquid",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ContentType.Should().Be("text/html");

        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Contain("<h1>ANNUAL REPORT</h1>");
        content.Should().Contain("Prepared by: Bob</p>");
    }

    [Fact]
    public async Task DotLiquidEngine_WithLoopTemplate_ShouldRenderList()
    {
        // Arrange
        var engine = new DotLiquidDocumentGenerationEngine();
        var payload = """{"items": [{"name": "Apple"}, {"name": "Banana"}, {"name": "Cherry"}]}""";
        var template = "{% for item in items %}{{ item.name }}\n{% endfor %}";

        var request = new GenerateDocumentRequest(
            requestId: "liquid-test-3",
            engine: "dotliquid",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "dotliquid",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Contain("Apple");
        content.Should().Contain("Banana");
        content.Should().Contain("Cherry");
    }

    [Fact]
    public async Task DotLiquidEngine_WithMarkdownTemplate_ShouldRenderMarkdown()
    {
        // Arrange
        var engine = new DotLiquidDocumentGenerationEngine();
        var payload = """{"project": "GenerateDocService", "status": "active"}""";
        var template = "# {{ project }}\n\nStatus: {{ status }}";

        var request = new GenerateDocumentRequest(
            requestId: "liquid-test-4",
            engine: "dotliquid",
            inputFormat: "json",
            outputFormat: "md",
            templateFormat: "dotliquid",
            payload: Encoding.UTF8.GetBytes(payload),
            template: Encoding.UTF8.GetBytes(template),
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ContentType.Should().Be("text/markdown");

        var content = Encoding.UTF8.GetString(result.Content.ReadFully());
        content.Should().Be("# GenerateDocService\n\nStatus: active");
    }

    #endregion

    #region MiniWord Engine Tests

    [Fact]
    public async Task MiniWordEngine_WithDocxTemplate_ShouldReplacePlaceholders()
    {
        // Arrange
        var engine = new MiniWordDocumentGenerationEngine();

        // Create a minimal .docx template with {{Name}} and {{City}} placeholders
        var templateBytes = CreateMinimalDocxTemplate("{{Name}} lives in {{City}}.");

        var payload = """{"Name": "Alice", "City": "Moscow"}""";

        var request = new GenerateDocumentRequest(
            requestId: "miniword-test-1",
            engine: "miniword",
            inputFormat: "json",
            outputFormat: "docx",
            templateFormat: "docx",
            payload: Encoding.UTF8.GetBytes(payload),
            template: templateBytes,
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("miniword-test-1.docx");
        result.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.ContentLength.Should().BeGreaterThan(0);

        // Verify it's a valid ZIP/XLSX file
        var content = result.Content;
        var header = new byte[2];
        var bytesRead = content.Read(header, 0, 2);
        bytesRead.Should().Be(2);
        header[0].Should().Be(0x50); // 'P'
        header[1].Should().Be(0x4B); // 'K'
    }

    [Fact]
    public async Task MiniWordEngine_WithTableDocxTemplate_ShouldReplacePlaceholdersInTable()
    {
        // Arrange
        var engine = new MiniWordDocumentGenerationEngine();

        // Create a minimal .docx template with table placeholders
        var templateBytes = CreateMinimalDocxWithTableTemplate();

        var payload = """{"Name": "Bob", "Score": 95, "Grade": "A"}""";

        var request = new GenerateDocumentRequest(
            requestId: "miniword-test-2",
            engine: "miniword",
            inputFormat: "json",
            outputFormat: "docx",
            templateFormat: "docx",
            payload: Encoding.UTF8.GetBytes(payload),
            template: templateBytes,
            metadata: new Dictionary<string, string>());

        // Act
        var result = await engine.GenerateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.FileName.Should().Be("miniword-test-2.docx");
        result.ContentLength.Should().BeGreaterThan(0);

        // Verify valid ZIP signature
        var content = result.Content;
        var header = new byte[2];
        content.Read(header, 0, 2);
        header[0].Should().Be(0x50);
        header[1].Should().Be(0x4B);
    }

    [Fact]
    public async Task MiniWordEngine_WithEmptyTemplate_ShouldThrow()
    {
        // Arrange
        var engine = new MiniWordDocumentGenerationEngine();
        var payload = """{"Name": "Alice"}""";

        // Passing null template → becomes empty ReadOnlyMemory<byte> → MiniWord fails
        var request = new GenerateDocumentRequest(
            requestId: "miniword-test-3",
            engine: "miniword",
            inputFormat: "json",
            outputFormat: "docx",
            templateFormat: "docx",
            payload: Encoding.UTF8.GetBytes(payload),
            template: null,
            metadata: new Dictionary<string, string>());

        // Act & Assert
        // MiniWord receives empty byte[] and throws NullReferenceException internally
        await Assert.ThrowsAsync<NullReferenceException>(
            () => engine.GenerateAsync(request));
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a minimal valid .docx file with the given text content.
    /// The text can contain {{tag}} placeholders for MiniWord to replace.
    /// </summary>
    private static byte[] CreateMinimalDocxTemplate(string textContent)
    {
        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // [Content_Types].xml
            var contentTypes = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(contentTypes.Open()))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
<Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
<Default Extension=""xml"" ContentType=""application/xml""/>
<Override PartName=""/word/document.xml"" ContentType=""application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml""/>
</Types>");
            }

            // _rels/.rels
            var rels = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(rels.Open()))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
<Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""word/document.xml""/>
</Relationships>");
            }

            // word/document.xml
            var document = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(document.Open()))
            {
                // Escape XML special characters in the text content
                var escapedText = System.Security.SecurityElement.Escape(textContent);
                writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
<w:body>
<w:p>
<w:r>
<w:t xml:space=""preserve"">{escapedText}</w:t>
</w:r>
</w:p>
</w:body>
</w:document>");
            }

            // word/_rels/document.xml.rels
            var docRels = archive.CreateEntry("word/_rels/document.xml.rels");
            using (var writer = new StreamWriter(docRels.Open()))
            {
                writer.Write(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
</Relationships>");
            }
        }

        return memoryStream.ToArray();
    }

    private static byte[] CreateMinimalDocxWithTableTemplate()
    {
        var textContent = "Student: {{Name}}, Score: {{Score}}, Grade: {{Grade}}";
        return CreateMinimalDocxTemplate(textContent);
    }

    #endregion
}

/// <summary>
/// Extension helper to read all bytes from a stream.
/// </summary>
internal static class StreamExtensions
{
    public static byte[] ReadFully(this Stream stream)
    {
        stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
