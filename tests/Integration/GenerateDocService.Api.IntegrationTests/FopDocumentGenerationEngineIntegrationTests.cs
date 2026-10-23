using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.Fop;
using GenerateDocService.Engine.Scriban;
using Xunit.Sdk;

namespace GenerateDocService.Api.IntegrationTests;

public sealed class FopDocumentGenerationEngineIntegrationTests
{
    [Fact]
    [Trait("Category", "ExternalDependency")]
    public async Task GenerateAsync_ShouldCreatePdfArtifactUsingConfiguredConverter()
    {
        var baseUrl = Environment.GetEnvironmentVariable("FOP_CONVERTER_BASE_URL");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var converterUri))
        {
          throw SkipException.ForSkip("Set FOP_CONVERTER_BASE_URL to run the FOP converter integration test.");
        }

        using var client = new HttpClient { BaseAddress = converterUri };
        var scriban = new ScribanDocumentGenerationEngine();
        var templateResult = await scriban.GenerateAsync(new GenerateDocumentRequest(
            requestId: "fop-template",
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes("""{"title":"Invoice INV-2026-001","customer":"Ada Lovelace"}"""),
            template: Encoding.UTF8.GetBytes("""
                <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format">
                  <fo:layout-master-set>
                    <fo:simple-page-master master-name="document" page-width="8.5in" page-height="11in" margin="0.75in">
                      <fo:region-body />
                    </fo:simple-page-master>
                  </fo:layout-master-set>
                  <fo:page-sequence master-reference="document">
                    <fo:flow flow-name="xsl-region-body">
                      <fo:block font-size="18pt">{{ title }}</fo:block>
                      <fo:block font-size="12pt">Customer: {{ customer }}</fo:block>
                    </fo:flow>
                  </fo:page-sequence>
                </fo:root>
                """),
              metadata: null));
        var renderedFo = await ReadAllBytesAsync(templateResult.Content, CancellationToken.None);
        (templateResult as IDisposable)?.Dispose();

        var renderedFoText = Encoding.UTF8.GetString(renderedFo);
        renderedFoText.Should().Contain("Invoice INV-2026-001");
        renderedFoText.Should().Contain("Customer: Ada Lovelace");
        renderedFoText.Should().NotContain("{{ title }}");
        renderedFoText.Should().NotContain("{{ customer }}");

        var engine = new FopDocumentGenerationEngine(client);
        var request = new GenerateDocumentRequest(
            requestId: "fop-generated",
            engine: "fop",
            inputFormat: "xsl-fo",
            outputFormat: "pdf",
            templateFormat: null,
            payload: renderedFo,
            template: null);
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await engine.GenerateAsync(request, cancellationTokenSource.Token);
        var pdfBytes = await ReadAllBytesAsync(result.Content, cancellationTokenSource.Token);
        var outputPath = await WriteArtifactAsync("fop/fop-template-generated.pdf", pdfBytes, cancellationTokenSource.Token);

        Encoding.ASCII.GetString(pdfBytes, 0, 4).Should().Be("%PDF");
        pdfBytes.Should().HaveCountGreaterThan(1_000);
        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().Be(pdfBytes.LongLength);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream source, CancellationToken cancellationToken)
    {
        source.Position = 0;
        using var destination = new MemoryStream();
        await source.CopyToAsync(destination, cancellationToken);
        return destination.ToArray();
    }

    private static async Task<string> WriteArtifactAsync(string relativePath, byte[] content, CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(FindSolutionRoot(), "artifacts", "test-results", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllBytesAsync(outputPath, content, cancellationToken);
        return outputPath;
    }

    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GenerateDocService.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate GenerateDocService.slnx from the test output directory.");
    }
}