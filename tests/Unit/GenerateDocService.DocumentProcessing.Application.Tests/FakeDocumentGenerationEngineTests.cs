using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class FakeDocumentGenerationEngineTests
{
    [Fact]
    public async Task GenerateAsync_ShouldWriteTextArtifact()
    {
        var engine = new FakeDocumentGenerationEngine();
        var request = new GenerateDocumentRequest(
            requestId: "fake-artifact",
            engine: "fake",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("{\"title\":\"Sample\"}"),
            template: null);

        var result = await engine.GenerateAsync(request);
        var content = result.Content.ReadFully();
        var outputPath = await TestArtifactWriter.WriteAsync("fake/fake-generated.txt", content);

        Encoding.UTF8.GetString(content).Should().Contain("engine=fake");
        File.Exists(outputPath).Should().BeTrue();
        new FileInfo(outputPath).Length.Should().Be(content.LongLength);
    }
}