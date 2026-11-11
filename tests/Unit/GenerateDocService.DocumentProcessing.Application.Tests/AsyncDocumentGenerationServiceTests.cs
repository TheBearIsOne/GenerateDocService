using System.Text;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.Engine.Scriban;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class AsyncDocumentGenerationServiceTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldPreserveCorrelationIdFromMetadata()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var submission = new CapturingSubmission(repository);
        using var metrics = new DocumentGenerationMetrics();
        var service = new AsyncDocumentGenerationService(
            new InMemoryGenerationRequestDeduplicationStore(),
            new DocumentGenerationEngineRegistry([new FakeDocumentGenerationEngine()]),
            repository,
            submission,
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

        submission.Message.Should().NotBeNull();
        submission.Message!.CorrelationId.Should().Be("corr-123");
        submission.Message.Metadata.Should().ContainKey("correlationId").WhoseValue.Should().Be("corr-123");
    }

    [Fact]
    public async Task EnqueueAsync_ShouldFallbackToRequestId_WhenCorrelationIdIsMissing()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var submission = new CapturingSubmission(repository);
        var requestId = Guid.NewGuid().ToString("N");
        using var metrics = new DocumentGenerationMetrics();
        var service = new AsyncDocumentGenerationService(
            new InMemoryGenerationRequestDeduplicationStore(),
            new DocumentGenerationEngineRegistry([new FakeDocumentGenerationEngine()]),
            repository,
            submission,
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

        submission.Message.Should().NotBeNull();
        submission.Message!.CorrelationId.Should().Be(requestId);
    }

    [Fact]
    public async Task EnqueueAsync_ShouldAutoSelectHighestPriorityEngine_WhenEngineIsNotSpecified()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var submission = new CapturingSubmission(repository);
        using var metrics = new DocumentGenerationMetrics();
        var service = new AsyncDocumentGenerationService(
            new InMemoryGenerationRequestDeduplicationStore(),
            new DocumentGenerationEngineRegistry([
                new FakeDocumentGenerationEngine(),
                new ScribanDocumentGenerationEngine(new TemplateParserRegistry([new ScribanTemplateParser()]))
            ]),
            repository,
            submission,
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

        submission.Message.Should().NotBeNull();
        submission.Message!.Engine.Should().Be("scriban");
    }

    [Fact]
    public async Task EnqueueAsync_ShouldMarkTaskFailed_WhenSchedulerFails()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var scheduler = new CapturingBackgroundGenerationScheduler
        {
            Failure = new IOException("broker unavailable")
        };
        var deduplicationStore = new InMemoryGenerationRequestDeduplicationStore();
        using var metrics = new DocumentGenerationMetrics();
        var service = new AsyncDocumentGenerationService(
            deduplicationStore,
            new DocumentGenerationEngineRegistry([new FakeDocumentGenerationEngine()]),
            repository,
            new InMemoryAsyncDocumentGenerationSubmission(repository, scheduler),
            metrics);
        var request = new GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: "fake",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("{\"document\":\"hello\"}"),
            template: null,
            metadata: new Dictionary<string, string>());

        var act = () => service.EnqueueAsync(request);

        await act.Should().ThrowAsync<IOException>().WithMessage("broker unavailable");
        var tasks = await repository.GetExpiredTasksAsync(DateTimeOffset.MaxValue);
        tasks.Should().ContainSingle(task => task.Status == GenerationTaskStatus.Failed);
    }

    private sealed class CapturingSubmission(InMemoryDocumentGenerationTaskRepository repository)
        : GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging.IAsyncDocumentGenerationSubmission
    {
        public GenerateDocumentRequested? Message { get; private set; }

        public async Task SubmitAsync(
            GenerateDocService.DocumentProcessing.Domain.Tasks.DocumentGenerationTask task,
            GenerateDocumentRequested message,
            CancellationToken cancellationToken = default)
        {
            await repository.AddAsync(task, cancellationToken);
            Message = message;
        }
    }

    private sealed class CapturingBackgroundGenerationScheduler : GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging.IBackgroundGenerationScheduler
    {
        public GenerateDocumentRequested? Message { get; private set; }
        public Exception? Failure { get; init; }

        public Task<string> EnqueueAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Message = message;
            return Task.FromResult(message.TaskId);
        }
    }
}
