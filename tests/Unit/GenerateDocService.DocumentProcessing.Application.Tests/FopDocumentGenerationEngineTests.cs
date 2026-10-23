using System.Net;
using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.Fop;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class FopDocumentGenerationEngineTests
{
    [Fact]
    public async Task GenerateAsync_ShouldSubmitFoAsMultipartAndReturnPdf()
    {
        using var handler = new RecordingHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://fop.test/")
        };
        var engine = new FopDocumentGenerationEngine(client);
        var request = new GenerateDocumentRequest(
            requestId: "fop-request-1",
            engine: "fop",
            inputFormat: "fo",
            outputFormat: "pdf",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("<fo:root xmlns:fo=\"http://www.w3.org/1999/XSL/Format\" />"),
            template: null,
            metadata: new Dictionary<string, string> { ["client"] = "test" });

        var result = await engine.GenerateAsync(request);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri.Should().Be("/api/v1/convert/sync?payloadType=XSL_FO&outputFormat=PDF&correlationId=fop-request-1");
        handler.ContentType.Should().StartWith("multipart/form-data");
        handler.Body.Should().Contain("name=primaryDocument");
        handler.Body.Should().Contain("<fo:root");
        result.FileName.Should().Be("fop-request-1.pdf");
        result.ContentType.Should().Be("application/pdf");
        result.OutputFormat.Should().Be("pdf");
        result.ContentLength.Should().BeGreaterThan(4);
    }

    [Theory]
    [InlineData("fo", "pdf", null, true)]
    [InlineData("xsl-fo", "pdf", null, true)]
    [InlineData("xml", "pdf", null, false)]
    [InlineData("fo", "pdf", "xsl", false)]
    public void CanHandle_ShouldMatchTheFopConversionContract(string inputFormat, string outputFormat, string? templateFormat, bool expected)
    {
        using var client = new HttpClient(new RecordingHttpMessageHandler());
        var engine = new FopDocumentGenerationEngine(client);

        engine.CanHandle(inputFormat, outputFormat, templateFormat).Should().Be(expected);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? RequestUri { get; private set; }
        public string? ContentType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri?.PathAndQuery;
            ContentType = request.Content?.Headers.ContentType?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-test"))
            };
        }
    }
}