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

    private static GenerateDocumentRequest CreateRequest(byte[] template)
        => new(
            requestId: "miniword-test",
            engine: "miniword",
            inputFormat: "json",
            outputFormat: "docx",
            templateFormat: "docx",
            payload: Encoding.UTF8.GetBytes("{\"name\":\"Ada\"}"),
            template: template);
}