using System.IO.Compression;
using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.MiniExcel;
using MiniExcelFormat = global::MiniExcelLibs.ExcelType;
using MiniExcelReader = global::MiniExcelLibs.MiniExcel;

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

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("xlsx", true)]
    [InlineData("XLSX", true)]
    [InlineData("Xlsx", true)]
    [InlineData("docx", false)]
    [InlineData("scriban", false)]
    [InlineData("json", false)]
    public void CanHandle_ShouldAcceptOnlyXlsxTemplateFormat(string? templateFormat, bool expected)
    {
        var engine = new MiniExcelDocumentGenerationEngine();

        engine.CanHandle("json", "xlsx", templateFormat).Should().Be(expected);
    }

    [Fact]
    public async Task GenerateAsync_ShouldPopulateEveryTemplatePlaceholderFromData()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var template = CreateXlsxTemplate("{{invoiceNo}}", "Customer: {{customer}}", "Units: {{units}}", "Paid: {{paid}}");
        var request = BuildRequest(
            "tpl-all",
            """{"invoiceNo":"INV-2026-042","customer":"Ada Lovelace","units":1250,"paid":true}""",
            template);

        var result = await engine.GenerateAsync(request);
        var workbook = result.Content.ReadFully();
        var worksheetXml = ReadWorkbookXml(workbook);

        worksheetXml.Should().Contain("INV-2026-042");
        worksheetXml.Should().Contain("Customer: Ada Lovelace");
        worksheetXml.Should().Contain("Units: 1250");
        worksheetXml.Should().Contain("Paid: True");
        worksheetXml.Should().NotContain("{{");
        worksheetXml.Should().NotContain("}}");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldProduceTemplateOutputThatExcelCanReadBack()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var template = CreateXlsxTemplate("{{invoiceNo}}", "Customer: {{customer}}");
        var request = BuildRequest(
            "tpl-roundtrip",
            """{"invoiceNo":"INV-7","customer":"Grace Hopper"}""",
            template);

        var result = await engine.GenerateAsync(request);
        var workbook = result.Content.ReadFully();
        var cells = ReadRows(workbook, useHeaderRow: false).SelectMany(row => row.Values).ToList();

        cells.Should().HaveCount(2);
        cells[0].Should().Be("INV-7");
        cells[1].Should().Be("Customer: Grace Hopper");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRenderTemplateKeyMissingFromPayloadAsEmpty()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var template = CreateXlsxTemplate("No: {{invoiceNo}}", "Missing: {{absent}}");
        var request = BuildRequest("tpl-missing", """{"invoiceNo":"INV-9"}""", template);

        var result = await engine.GenerateAsync(request);
        var worksheetXml = ReadWorkbookXml(result.Content.ReadFully());

        worksheetXml.Should().Contain("No: INV-9");
        worksheetXml.Should().Contain("Missing: ");
        worksheetXml.Should().NotContain("{{absent}}");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldIgnorePayloadKeysNotReferencedByTemplate()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var template = CreateXlsxTemplate("No: {{invoiceNo}}");
        var request = BuildRequest(
            "tpl-extra",
            """{"invoiceNo":"INV-9","unusedNumber":1,"unusedText":"ignore me"}""",
            template);

        var result = await engine.GenerateAsync(request);
        var worksheetXml = ReadWorkbookXml(result.Content.ReadFully());

        worksheetXml.Should().Contain("No: INV-9");
        worksheetXml.Should().NotContain("ignore me");
        worksheetXml.Should().NotContain("unusedNumber");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRenderJsonNullTemplateValueAsEmpty()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var template = CreateXlsxTemplate("{{name}}", "nick={{nickname}}");
        var request = BuildRequest("tpl-null", """{"name":"Ada","nickname":null}""", template);

        var result = await engine.GenerateAsync(request);
        var worksheetXml = ReadWorkbookXml(result.Content.ReadFully());

        worksheetXml.Should().Contain("Ada");
        worksheetXml.Should().Contain("nick=");
        worksheetXml.Should().NotContain("null");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldEscapeMarkupAndPreserveUnicodeInTemplate()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var template = CreateXlsxTemplate("{{markup}}", "{{cyrillic}}", "{{symbol}}");
        var request = BuildRequest(
            "tpl-unsafe",
            """{"markup":"<b>&\"x\"</b>","cyrillic":"Привет","symbol":"★"}""",
            template);

        var result = await engine.GenerateAsync(request);
        var workbook = result.Content.ReadFully();

        ReadRows(workbook, useHeaderRow: false)
            .SelectMany(row => row.Values)
            .Should()
            .Equal("<b>&\"x\"</b>", "Привет", "★");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldThrowWhenTemplateGivenButPayloadIsJsonArray()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("tpl-array-payload", """[{"a":1}]""", CreateXlsxTemplate("{{a}}"));

        var act = () => engine.GenerateAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("MiniExcel templates require a JSON object as payload.");
    }

    [Fact]
    public async Task GenerateAsync_ShouldFallBackToTableOutputWhenTemplateBytesAreEmpty()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("tpl-empty-bytes", """[{"Name":"Alice"},{"Name":"Bob"}]""", []);

        var result = await engine.GenerateAsync(request);
        var rows = ReadRows(result.Content.ReadFully());

        rows.Should().HaveCount(2);
        rows[0]["Name"].Should().Be("Alice");
        rows[1]["Name"].Should().Be("Bob");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldThrowWhenTemplatePlaceholderResolvesToNestedObject()
    {
        // Accepted behaviour, not an oversight. MiniExcel's template renderer supports nested and
        // collection values for its {{$list.field}} syntax, so the engine cannot reject nesting
        // wholesale without breaking legitimate templates, and it cannot tell which placeholder a
        // value belongs to without parsing the workbook XML. Telling the two cases apart belongs in
        // MiniExcel; this test pins what callers get today so a change is noticed.
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest(
            "tpl-nested",
            """{"name":"Ada","address":{"city":"London"}}""",
            CreateXlsxTemplate("{{name}}", "{{address}}"));

        var act = () => engine.GenerateAsync(request);

        await act.Should().ThrowAsync<IndexOutOfRangeException>();
    }

    [Fact]
    public async Task GenerateAsync_ShouldThrowWhenTemplatePlaceholderResolvesToArray()
    {
        // Accepted behaviour for the same reason as the nested-object case above.
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest(
            "tpl-array-value",
            """{"name":"Ada","tags":["a","b"]}""",
            CreateXlsxTemplate("{{name}}", "{{tags}}"));

        var act = () => engine.GenerateAsync(request);

        await act.Should().ThrowAsync<IndexOutOfRangeException>();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRoundTripHeadersAndValuesFromJsonArray()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest(
            "table-roundtrip",
            """
            [
                {"Name":"Alice","Age":30,"City":"Moscow"},
                {"Name":"Bob","Age":25,"City":"Saint Petersburg"}
            ]
            """);

        var result = await engine.GenerateAsync(request);
        var rows = ReadRows(result.Content.ReadFully());

        rows.Should().HaveCount(2);
        rows[0].Keys.Should().Equal("Name", "Age", "City");
        rows[0]["Name"].Should().Be("Alice");
        Convert.ToInt32(rows[0]["Age"]).Should().Be(30);
        rows[1]["City"].Should().Be("Saint Petersburg");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldWriteHeaderlessEmptyWorkbookForEmptyJsonArray()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("table-empty", "[]");

        var result = await engine.GenerateAsync(request);
        var workbook = result.Content.ReadFully();
        var worksheetXml = ReadWorkbookXml(workbook);

        workbook.Should().NotBeEmpty();
        ReadRows(workbook).Should().BeEmpty();
        ReadRows(workbook, useHeaderRow: false).Should().BeEmpty();
        worksheetXml.Should().Contain("sheetData");
        worksheetXml.Should().Contain("dimension ref=\"A1\"");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldPlaceScalarArrayItemsUnderValueHeader()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("table-scalars", """["a","b","c"]""");

        var result = await engine.GenerateAsync(request);
        var rows = ReadRows(result.Content.ReadFully());

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.Keys.Single() == "value");
        rows.Select(row => row["value"]).Should().Equal("a", "b", "c");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldKeepLastValueForKeysDifferingOnlyByCase()
    {
        // The engine builds a case-insensitive dictionary, so "Name" and "name" collide.
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("table-dup-case", """[{"Name":"first","name":"second"}]""");

        var result = await engine.GenerateAsync(request);
        var rows = ReadRows(result.Content.ReadFully());

        rows.Should().ContainSingle();
        rows[0].Should().ContainSingle();
        rows[0]["Name"].Should().Be("second");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldPreserveNumericAndBooleanCellTypes()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("table-types", """[{"text":"abc","whole":42,"fraction":95.5,"flag":true,"off":false}]""");

        var result = await engine.GenerateAsync(request);
        var workbook = result.Content.ReadFully();
        var worksheetXml = ReadWorkbookXml(workbook);
        var row = ReadRows(workbook).Single();

        worksheetXml.Should().Contain("t=\"n\"");
        worksheetXml.Should().Contain("t=\"b\"");
        row["text"].Should().Be("abc");
        Convert.ToDouble(row["whole"]).Should().Be(42d);
        Convert.ToDouble(row["fraction"]).Should().Be(95.5d);
        Convert.ToBoolean(row["flag"]).Should().BeTrue();
        Convert.ToBoolean(row["off"]).Should().BeFalse();

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldWriteLargeIntegersAtFullPrecision()
    {
        // Values that fit in Int64 are written as exact numeric cells rather than
        // degrading to double. Reading such a cell back is lossy in Excel itself,
        // so fidelity is asserted on the written sheet.
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("table-bigint", """[{"big":9007199254740993}]""");

        var result = await engine.GenerateAsync(request);
        var worksheetXml = ReadWorkbookXml(result.Content.ReadFully());

        worksheetXml.Should().Contain("9007199254740993");
        worksheetXml.Should().Contain("t=\"n\"");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldEscapeMarkupAndPreserveUnicodeInTableOutput()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest(
            "table-unsafe",
            """[{"markup":"<b>&\"x\"</b>","cyrillic":"Привет","symbol":"★","newline":"a\nb"}]""");

        var result = await engine.GenerateAsync(request);
        var row = ReadRows(result.Content.ReadFully()).Single();

        row["markup"].Should().Be("<b>&\"x\"</b>");
        row["cyrillic"].Should().Be("Привет");
        row["symbol"].Should().Be("★");
        row["newline"].Should().Be("a\nb");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRoundTripWideAndTallPayload()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        const int rowCount = 300;
        const int columnCount = 12;
        var rowsJson = string.Join(
            ",",
            Enumerable.Range(1, rowCount).Select(r =>
                "{" + string.Join(",", Enumerable.Range(1, columnCount).Select(c => $"\"c{c}\":{r * c}")) + "}"));
        var request = BuildRequest("table-large", $"[{rowsJson}]");

        var result = await engine.GenerateAsync(request);
        var workbook = result.Content.ReadFully();
        var rows = ReadRows(workbook);

        rows.Should().HaveCount(rowCount);
        rows[0].Keys.Should().HaveCount(columnCount);
        Convert.ToInt32(rows[rowCount - 1]["c12"]).Should().Be(rowCount * columnCount);

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldThrowOnMalformedJsonPayload()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("bad-malformed", """[{"Name":}]""");

        await ((Func<Task>)(async () => await engine.GenerateAsync(request)))
            .Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task GenerateAsync_ShouldThrowOnEmptyPayload()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("bad-empty", string.Empty);

        await ((Func<Task>)(async () => await engine.GenerateAsync(request)))
            .Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task GenerateAsync_ShouldThrowOnWhitespaceOnlyPayload()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("bad-whitespace", "   ");

        await ((Func<Task>)(async () => await engine.GenerateAsync(request)))
            .Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"just text\"")]
    [InlineData("true")]
    [InlineData("null")]
    public async Task GenerateAsync_ShouldThrowOnNonContainerJsonPayload(string payload)
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("bad-scalar", payload);

        await ((Func<Task>)(async () => await engine.GenerateAsync(request)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("JSON payload must be an array of objects or a single object.");
    }

    [Fact]
    public async Task GenerateAsync_ShouldAlignArrayItemsWithInconsistentShapes()
    {
        // Headers used to come from the first item only, so later items that lacked those keys
        // surfaced as an unhandled KeyNotFoundException. Rows are now aligned to the union of keys.
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("mixed-shapes", """[{"Name":"A"},"plain",42,null]""");

        var result = await engine.GenerateAsync(request);
        var rows = ReadRows(result.Content.ReadFully());

        rows.Should().HaveCount(4);
        rows[0]["Name"].Should().Be("A");
        rows[1]["value"].Should().Be("plain");
        rows[2]["value"]?.ToString().Should().Be("42");

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldRejectANestedObjectInTableOutput()
    {
        // MiniExcel used to write the CLR type name into the cell and report success, so a corrupt
        // workbook shipped silently. A cell cannot hold a structure, so this now fails loudly.
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("nested", """[{"a":{"b":{"c":"deep"}}}]""");

        await ((Func<Task>)(async () => await engine.GenerateAsync(request)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*$.a*worksheet cell*");
    }

    [Fact]
    public async Task GenerateAsync_ShouldRejectANestedArrayInTableOutput()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("nested-array", """[{"tags":["x","y"]}]""");

        await ((Func<Task>)(async () => await engine.GenerateAsync(request)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*$.tags*worksheet cell*");
    }

    [Fact]
    public async Task GenerateAsync_ShouldFormatTemplateNumbersInvariantly_UnderAnyAmbientCulture()
    {
        // The template path used to format with the ambient culture, so the same request produced
        // 1234.56 on an invariant host and 1234,56 on a ru-RU one.
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");

            var engine = new MiniExcelDocumentGenerationEngine();
            var request = BuildRequest("culture-invariant", """{"amount":1234.56}""", CreateXlsxTemplate("{{amount}}"));

            var result = await engine.GenerateAsync(request);
            var worksheetXml = ReadWorkbookXml(result.Content.ReadFully());

            worksheetXml.Should().Contain("1234.56");
            worksheetXml.Should().NotContain("1234,56");

            if (result is IDisposable disposable) disposable.Dispose();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("json", "pdf", null)]
    [InlineData("json", "xlsx", "docx")]
    [InlineData("xml", "xlsx", null)]
    [InlineData("csv", "xlsx", "xlsx")]
    public async Task GenerateAsync_ShouldThrowWhenRequestIsOutsideTheEngineContract(
        string inputFormat, string outputFormat, string? templateFormat)
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = new GenerateDocumentRequest(
            requestId: "bad-contract",
            engine: "miniexcel",
            inputFormat: inputFormat,
            outputFormat: outputFormat,
            templateFormat: templateFormat,
            payload: Encoding.UTF8.GetBytes("""[{"a":1}]"""),
            template: templateFormat is null ? null : CreateXlsxTemplate("{{a}}"),
            metadata: new Dictionary<string, string>());

        await ((Func<Task>)(async () => await engine.GenerateAsync(request)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Engine 'miniexcel' cannot handle {inputFormat} -> {outputFormat}.");
    }

    [Fact]
    public async Task GenerateAsync_ShouldThrowWhenCancelledBeforeGeneration()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("cancelled", """[{"a":1}]""");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => engine.GenerateAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateAsync_ShouldExposeRequestIdFileNameContentTypeAndMetadata()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var metadata = new Dictionary<string, string> { ["correlation"] = "abc-123", ["tenant"] = "t1" };
        var request = new GenerateDocumentRequest(
            requestId: "req-77",
            engine: "miniexcel",
            inputFormat: "json",
            outputFormat: "xlsx",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("""[{"a":1}]"""),
            template: null,
            metadata: metadata);

        var result = await engine.GenerateAsync(request);

        result.RequestId.Should().Be("req-77");
        result.FileName.Should().Be("req-77.xlsx");
        result.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        result.OutputFormat.Should().Be("xlsx");
        result.Checksum.Should().BeNull();
        result.Metadata.Should().BeSameAs(metadata);

        if (result is IDisposable disposable) disposable.Dispose();
    }

    [Fact]
    public async Task GenerateAsync_ShouldReturnReReadableContentStreamAtPositionZero()
    {
        var engine = new MiniExcelDocumentGenerationEngine();
        var request = BuildRequest("stream-position", """[{"Name":"Alice"}]""");

        var result = await engine.GenerateAsync(request);

        result.Content.Position.Should().Be(0);
        result.Content.CanSeek.Should().BeTrue();
        result.ContentLength.Should().BeGreaterThan(0);

        var first = result.Content.ReadFully();
        var second = result.Content.ReadFully();

        first.Should().HaveCount((int)result.ContentLength);
        second.Should().Equal(first);

        if (result is IDisposable disposable) disposable.Dispose();
    }

    private static GenerateDocumentRequest BuildRequest(string requestId, string payload, byte[]? template = null)
        => new(
            requestId: requestId,
            engine: "miniexcel",
            inputFormat: "json",
            outputFormat: "xlsx",
            templateFormat: template is null ? null : "xlsx",
            payload: Encoding.UTF8.GetBytes(payload),
            template: template,
            metadata: new Dictionary<string, string>());

    private static List<IDictionary<string, object>> ReadRows(byte[] workbookBytes, bool useHeaderRow = true)
    {
        using var stream = new MemoryStream(workbookBytes, writable: false);
        return MiniExcelReader
            .Query(stream, useHeaderRow: useHeaderRow, excelType: MiniExcelFormat.XLSX)
            .Cast<IDictionary<string, object>>()
            .ToList();
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
