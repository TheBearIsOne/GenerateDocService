using System.Text;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.Engine.Scriban;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class AsyncDocumentGenerationServiceTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldPreserveCorrelationIdFromMetadata()
    {
        var scheduler = new CapturingBackgroundGenerationScheduler();
        using var metrics = new DocumentGenerationMetrics();
        var service = new AsyncDocumentGenerationService(
            new InMemoryGenerationRequestDeduplicationStore(),
            new DocumentGenerationEngineRegistry([new FakeDocumentGenerationEngine()]),
            new InMemoryDocumentGenerationTaskRepository(),
            scheduler,
            metrics);

        var request = new GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: "fake",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("{\"document\":\"hello\"}"),
            template: null,
            metadata: new Dictionary<string, string>
            {
                ["correlationId"] = "corr-123"
            });

        _ = await service.EnqueueAsync(request);

        scheduler.Message.Should().NotBeNull();
        scheduler.Message!.CorrelationId.Should().Be("corr-123");
        scheduler.Message.Metadata.Should().ContainKey("correlationId").WhoseValue.Should().Be("corr-123");
    }

    [Fact]
    public async Task EnqueueAsync_ShouldFallbackToRequestId_WhenCorrelationIdIsMissing()
    {
        var scheduler = new CapturingBackgroundGenerationScheduler();
        var requestId = Guid.NewGuid().ToString("N");
        using var metrics = new DocumentGenerationMetrics();
        var service = new AsyncDocumentGenerationService(
            new InMemoryGenerationRequestDeduplicationStore(),
            new DocumentGenerationEngineRegistry([new FakeDocumentGenerationEngine()]),
            new InMemoryDocumentGenerationTaskRepository(),
            scheduler,
            metrics);

        var request = new GenerateDocumentRequest(
            requestId: requestId,
            engine: "fake",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("{\"document\":\"hello\"}"),
            template: null,
            metadata: new Dictionary<string, string>());

        _ = await service.EnqueueAsync(request);

        scheduler.Message.Should().NotBeNull();
        scheduler.Message!.CorrelationId.Should().Be(requestId);
    }

    [Fact]
    public async Task EnqueueAsync_ShouldAutoSelectHighestPriorityEngine_WhenEngineIsNotSpecified()
    {
        var scheduler = new CapturingBackgroundGenerationScheduler();
        using var metrics = new DocumentGenerationMetrics();
        var service = new AsyncDocumentGenerationService(
            new InMemoryGenerationRequestDeduplicationStore(),
            new DocumentGenerationEngineRegistry([
                new FakeDocumentGenerationEngine(),
                new ScribanDocumentGenerationEngine(new TemplateParserRegistry([new ScribanTemplateParser()]))
            ]),
            new InMemoryDocumentGenerationTaskRepository(),
            scheduler,
            metrics);

        var request = new GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: string.Empty,
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes("{\"document\":\"hello\"}"),
            template: Encoding.UTF8.GetBytes("Document: {{ document }}"),
            metadata: new Dictionary<string, string>());

        _ = await service.EnqueueAsync(request);

        scheduler.Message.Should().NotBeNull();
        scheduler.Message!.Engine.Should().Be("scriban");
    }

    private sealed class CapturingBackgroundGenerationScheduler : GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging.IBackgroundGenerationScheduler
    {
        public GenerateDocumentRequested? Message { get; private set; }

        public Task<string> EnqueueAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.FromResult(message.TaskId);
        }
    }
}
