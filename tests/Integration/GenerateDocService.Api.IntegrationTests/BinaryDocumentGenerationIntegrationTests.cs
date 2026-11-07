using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// Covers the engines that consume binary Office files. They are only reachable over HTTP through
/// the base64 payload and template fields, so every test here also proves that raw bytes survive
/// the JSON contract, the validator, the mapper, and the async message round trip.
/// </summary>
public sealed class BinaryDocumentGenerationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BinaryDocumentGenerationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task MiniWord_ShouldPopulateABinaryDocxTemplate_SentAsBase64()
    {
        using var client = CreateClient();
        var template = CreateDocxTemplate("Student: {{Name}}, Score: {{Score}}");

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "miniword",
            InputFormat: "json",
            OutputFormat: "docx",
            TemplateFormat: "docx",
            Payload: """{"Name":"Ada Lovelace","Score":99}""",
            Template: null,
            Metadata: null,
            PayloadBase64: null,
            TemplateBase64: Convert.ToBase64String(template)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should()
            .Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var documentXml = ReadZipEntry(await response.Content.ReadAsByteArrayAsync(), "word/document.xml");

        documentXml.Should().Contain("Ada Lovelace");
        documentXml.Should().Contain("99");
        documentXml.Should().NotContain("{{Name}}");
        documentXml.Should().NotContain("{{Score}}");
    }

    [Fact]
    public async Task MiniExcel_ShouldPopulateABinaryXlsxTemplate_SentAsBase64()
    {
        using var client = CreateClient();
        var template = CreateXlsxTemplate("{{title}}", "Customer: {{customer}}");

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "miniexcel",
            InputFormat: "json",
            OutputFormat: "xlsx",
            TemplateFormat: "xlsx",
            Payload: """{"title":"Invoice INV-2026-001","customer":"Ada Lovelace"}""",
            Template: null,
            Metadata: null,
            PayloadBase64: null,
            TemplateBase64: Convert.ToBase64String(template)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var worksheetXml = ReadZipEntry(await response.Content.ReadAsByteArrayAsync(), "xl/worksheets/sheet1.xml");

        worksheetXml.Should().Contain("Invoice INV-2026-001");
        worksheetXml.Should().Contain("Customer: Ada Lovelace");
        worksheetXml.Should().NotContain("{{title}}");
        worksheetXml.Should().NotContain("{{customer}}");
    }

    [Fact]
    public async Task MiniPdf_ShouldConvertABinaryWorkbook_SentAsBase64()
    {
        using var client = CreateClient();

        // Produce a real workbook through the API first, so the conversion input is a genuine
        // MiniExcel document rather than a hand-written fixture.
        var workbookResponse = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "miniexcel",
            InputFormat: "json",
            OutputFormat: "xlsx",
            TemplateFormat: null,
            Payload: """[{"Name":"Alice","Age":30},{"Name":"Bob","Age":25}]""",
            Template: null,
            Metadata: null));

        workbookResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var workbook = await workbookResponse.Content.ReadAsByteArrayAsync();
        workbook.Length.Should().BeGreaterThan(0);

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "minipdf",
            InputFormat: "xlsx",
            OutputFormat: "pdf",
            TemplateFormat: null,
            Payload: null,
            Template: null,
            Metadata: null,
            PayloadBase64: Convert.ToBase64String(workbook)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var pdf = await response.Content.ReadAsByteArrayAsync();
        pdf.Length.Should().BeGreaterThan(1000);
        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task MiniWord_ShouldCarryTheBinaryTemplateThroughTheAsyncPipeline()
    {
        using var client = CreateClient();
        var template = CreateDocxTemplate("Hello {{Name}}");

        var submit = await client.PostAsJsonAsync("/api/v1/documents/async", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "miniword",
            InputFormat: "json",
            OutputFormat: "docx",
            TemplateFormat: "docx",
            Payload: """{"Name":"Ada Lovelace"}""",
            Template: null,
            Metadata: null,
            PayloadBase64: null,
            TemplateBase64: Convert.ToBase64String(template)));

        submit.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await submit.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();
        accepted.Should().NotBeNull();

        var completed = await WaitForFinalStatusAsync(client, accepted!.TaskId);
        completed.Status.Should()
            .Be(GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Completed);
        completed.Error.Should().BeNull();
        completed.DownloadUrl.Should().NotBeNullOrWhiteSpace();

        var download = await client.GetAsync(completed.DownloadUrl!);
        download.StatusCode.Should().Be(HttpStatusCode.OK);

        var documentXml = ReadZipEntry(await download.Content.ReadAsByteArrayAsync(), "word/document.xml");
        documentXml.Should().Contain("Ada Lovelace");
        documentXml.Should().NotContain("{{Name}}");
    }

    [Fact]
    public async Task Request_ShouldBeRejected_WhenBothPayloadFieldsAreSet()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: "both-payloads",
            Engine: "fake",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: null,
            Payload: """{"a":1}""",
            Template: null,
            Metadata: null,
            PayloadBase64: Convert.ToBase64String([1, 2, 3])));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not both");
    }

    [Fact]
    public async Task Request_ShouldBeRejected_WhenBothTemplateFieldsAreSet()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: "both-templates",
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: """{"a":1}""",
            Template: "{{ a }}",
            Metadata: null,
            PayloadBase64: null,
            TemplateBase64: Convert.ToBase64String([1, 2, 3])));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not both");
    }

    [Fact]
    public async Task Request_ShouldBeRejected_WhenBase64IsMalformed()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: "bad-base64",
            Engine: "minipdf",
            InputFormat: "xlsx",
            OutputFormat: "pdf",
            TemplateFormat: null,
            Payload: null,
            Template: null,
            Metadata: null,
            PayloadBase64: "not!valid!base64"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not well-formed base64");
    }

    [Fact]
    public async Task Request_ShouldBeRejected_WhenNeitherPayloadFieldIsSet()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: "no-payload",
            Engine: "fake",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: null,
            Payload: null,
            Template: null,
            Metadata: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("payloadBase64");
    }

    private HttpClient CreateClient()
        => _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task<TaskStatusHttpResponse> WaitForFinalStatusAsync(HttpClient client, string taskId)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var response = await client.GetAsync($"/api/v1/tasks/{taskId}");
            var payload = await response.Content.ReadFromJsonAsync<TaskStatusHttpResponse>();

            if (payload is not null
                && payload.Status is not GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Queued
                and not GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Processing)
            {
                return payload;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Task '{taskId}' did not reach a final state in time.");
    }

    private static string ReadZipEntry(byte[] archiveBytes, string entryName)
    {
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);

        entry.Should().NotBeNull($"the generated package should contain {entryName}");

        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    private static byte[] CreateDocxTemplate(string textContent)
    {
        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
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
                <w:body><w:p><w:r><w:t xml:space="preserve">{Escape(textContent)}</w:t></w:r></w:p></w:body>
                </w:document>
                """);
            WriteEntry(archive, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                </Relationships>
                """);
        }

        return memoryStream.ToArray();
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

            var rows = string.Join(
                string.Empty,
                cellValues.Select((value, index) =>
                    $"<row r=\"{index + 1}\"><c r=\"A{index + 1}\" t=\"inlineStr\"><is><t>{Escape(value)}</t></is></c></row>"));

            WriteEntry(archive, "xl/worksheets/sheet1.xml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                    <sheetData>{rows}</sheetData>
                </worksheet>
                """);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;
}
