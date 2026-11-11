using System.Text;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Resilience;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.Engine.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class BackgroundDocumentGenerationProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldRetryTransientFailure_AndComplete()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher();
        var engine = new FlakyEngine(new HttpRequestException("connection refused"), failuresBeforeSuccess: 1);
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(repository, new InMemoryGeneratedDocumentCache(), publisher, metrics, engine);

        var task = await SeedTaskAsync(repository);

        await processor.ProcessAsync(CreateMessage(task.TaskId));

        var stored = await repository.GetAsync(task.TaskId);
        stored!.Status.Should().Be(GenerationTaskStatus.Completed);
        engine.Calls.Should().Be(2);
        publisher.Generated.Should().HaveCount(1);
        publisher.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ShouldFailTask_AndPublishOnce_WhenTransientFailureExhaustsAttempts()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher();
        var engine = new FlakyEngine(new HttpRequestException("connection refused"), failuresBeforeSuccess: int.MaxValue);
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(repository, new InMemoryGeneratedDocumentCache(), publisher, metrics, engine);

        var task = await SeedTaskAsync(repository);

        await processor.ProcessAsync(CreateMessage(task.TaskId));

        var stored = await repository.GetAsync(task.TaskId);
        stored!.Status.Should().Be(GenerationTaskStatus.Failed);
        stored.Error.Should().Contain("connection refused");
        engine.Calls.Should().Be(3, "the default MaxAttempts is 3");
        publisher.Failed.Should().HaveCount(1);
        publisher.Generated.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotRetry_NonTransientFailure()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher();
        var engine = new FlakyEngine(
            new InvalidOperationException("Engine 'flaky' cannot handle xml -> xls with template 'none'."),
            failuresBeforeSuccess: int.MaxValue);
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(repository, new InMemoryGeneratedDocumentCache(), publisher, metrics, engine);

        var task = await SeedTaskAsync(repository);

        await processor.ProcessAsync(CreateMessage(task.TaskId));

        var stored = await repository.GetAsync(task.TaskId);
        stored!.Status.Should().Be(GenerationTaskStatus.Failed);
        engine.Calls.Should().Be(1);
        publisher.Failed.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldFailWithTimeoutError_AndNotRetry_WhenProcessingDeadlineExpires()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher();
        var engine = new BlockingEngine(TimeSpan.FromSeconds(30));
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(
            repository,
            new InMemoryGeneratedDocumentCache(),
            publisher,
            metrics,
            engine,
            options => options.ProcessingTimeoutSeconds = 1);

        var task = await SeedTaskAsync(repository);

        await processor.ProcessAsync(CreateMessage(task.TaskId));

        var stored = await repository.GetAsync(task.TaskId);
        stored!.Status.Should().Be(GenerationTaskStatus.Failed);
        stored.Error.Should().Contain("processing timeout");
        engine.Calls.Should().Be(1, "an expired deadline is terminal, not retryable");
        publisher.Failed.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldServeFromDocumentCache_WithoutInvokingEngine()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher();
        var engine = new FlakyEngine(null, failuresBeforeSuccess: 0);
        using var metrics = new DocumentGenerationMetrics();

        var fingerprint = "fingerprint-1";
        var documentCache = new InMemoryGeneratedDocumentCache();
        await documentCache.SetAsync(
            fingerprint,
            new CachedGeneratedDocument(
                "req-1",
                "cached.txt",
                "text/plain",
                "txt",
                5,
                "checksum",
                new Dictionary<string, string>(),
                new DocumentArtifactReference("in-memory", "artifacts/req-1/cached.txt", "artifacts", "req-1/cached.txt")));

        var processor = CreateProcessor(repository, documentCache, publisher, metrics, engine);
        var task = await SeedTaskAsync(repository);

        await processor.ProcessAsync(CreateMessage(
            task.TaskId,
            new Dictionary<string, string> { [DocumentRequestFingerprintProvider.MetadataKey] = fingerprint }));

        var stored = await repository.GetAsync(task.TaskId);
        stored!.Status.Should().Be(GenerationTaskStatus.Completed);
        stored.ResultFileName.Should().Be("cached.txt");
        engine.Calls.Should().Be(0);
        publisher.Generated.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldIgnoreDuplicateDelivery_AfterTaskCompletes()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher();
        var engine = new FlakyEngine(null, failuresBeforeSuccess: 0);
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(repository, new InMemoryGeneratedDocumentCache(), publisher, metrics, engine);
        var task = await SeedTaskAsync(repository);
        var message = CreateMessage(task.TaskId);

        await processor.ProcessAsync(message);
        await processor.ProcessAsync(message);

        (await repository.GetAsync(task.TaskId))!.Status.Should().Be(GenerationTaskStatus.Completed);
        engine.Calls.Should().Be(1);
        publisher.Generated.Should().HaveCount(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldKeepTaskCompleted_WhenGeneratedEventPublishingFails()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher { GeneratedFailure = new IOException("broker unavailable") };
        var engine = new FlakyEngine(null, failuresBeforeSuccess: 0);
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(repository, new InMemoryGeneratedDocumentCache(), publisher, metrics, engine);
        var task = await SeedTaskAsync(repository);

        await processor.ProcessAsync(CreateMessage(task.TaskId));

        var stored = await repository.GetAsync(task.TaskId);
        stored!.Status.Should().Be(GenerationTaskStatus.Completed);
        stored.ResultStoragePath.Should().NotBeNullOrWhiteSpace();
        publisher.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ShouldComplete_WhenGeneratedDocumentCacheIsUnavailable()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var publisher = new CapturingEventPublisher();
        var engine = new FlakyEngine(null, failuresBeforeSuccess: 0);
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(
            repository,
            new UnavailableGeneratedDocumentCache(),
            publisher,
            metrics,
            engine);
        var task = await SeedTaskAsync(repository);

        await processor.ProcessAsync(CreateMessage(
            task.TaskId,
            new Dictionary<string, string>
            {
                [DocumentRequestFingerprintProvider.MetadataKey] = "fingerprint-redis-down"
            }));

        var stored = await repository.GetAsync(task.TaskId);
        stored!.Status.Should().Be(GenerationTaskStatus.Completed);
        stored.ResultStoragePath.Should().NotBeNullOrWhiteSpace();
        engine.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_ShouldThrow_WhenTaskIsUnknown()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        using var metrics = new DocumentGenerationMetrics();
        var processor = CreateProcessor(
            repository,
            new InMemoryGeneratedDocumentCache(),
            new CapturingEventPublisher(),
            metrics,
            new FlakyEngine(null, failuresBeforeSuccess: 0));

        var act = () => processor.ProcessAsync(CreateMessage(Guid.NewGuid().ToString("N")));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*was not found*");
    }

    private static BackgroundDocumentGenerationProcessor CreateProcessor(
        InMemoryDocumentGenerationTaskRepository repository,
        IGeneratedDocumentCache documentCache,
        CapturingEventPublisher publisher,
        DocumentGenerationMetrics metrics,
        IDocumentGenerationEngine engine,
        Action<GenerationResilienceOptions>? configure = null)
    {
        var options = new GenerationResilienceOptions
        {
            InitialDelayMilliseconds = 1,
            MaxDelayMilliseconds = 2
        };
        configure?.Invoke(options);

        return new BackgroundDocumentGenerationProcessor(
            documentCache,
            new DocumentGenerationEngineRegistry([engine]),
            repository,
            new InMemoryDocumentArtifactStore(),
            publisher,
            metrics,
            new TransientFailureClassifier(),
            Options.Create(options),
            NullLogger<BackgroundDocumentGenerationProcessor>.Instance);
    }

    private static async Task<DocumentGenerationTask> SeedTaskAsync(InMemoryDocumentGenerationTaskRepository repository)
    {
        var task = new DocumentGenerationTask(
            taskId: Guid.NewGuid().ToString("N"),
            requestId: "req-1",
            engine: "flaky",
            outputFormat: "txt");

        await repository.AddAsync(task);

        return task;
    }

    private static GenerateDocumentRequested CreateMessage(
        string taskId,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            taskId,
            "corr-1",
            "req-1",
            "flaky",
            "json",
            "txt",
            null,
            Encoding.UTF8.GetBytes("{\"document\":\"hello\"}"),
            null,
            metadata ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow);

    private sealed class CapturingEventPublisher : IDocumentGenerationEventPublisher
    {
        public List<DocumentGenerated> Generated { get; } = [];
        public List<DocumentGenerationFailed> Failed { get; } = [];
        public Exception? GeneratedFailure { get; init; }

        public Task PublishGeneratedAsync(DocumentGenerated message, CancellationToken cancellationToken = default)
        {
            if (GeneratedFailure is not null)
            {
                throw GeneratedFailure;
            }

            Generated.Add(message);
            return Task.CompletedTask;
        }

        public Task PublishFailedAsync(DocumentGenerationFailed message, CancellationToken cancellationToken = default)
        {
            Failed.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableGeneratedDocumentCache : IGeneratedDocumentCache
    {
        public Task<CachedGeneratedDocument?> GetAsync(string fingerprint, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Redis unavailable");

        public Task SetAsync(
            string fingerprint,
            CachedGeneratedDocument document,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("Redis unavailable");
    }

    private sealed class FlakyEngine(Exception? failure, int failuresBeforeSuccess) : IDocumentGenerationEngine
    {
        public int Calls { get; private set; }

        public string Name => "flaky";

        public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null) => true;

        public Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;

            if (failure is not null && Calls <= failuresBeforeSuccess)
            {
                throw failure;
            }

            return Task.FromResult<IReportResult>(new GeneratedDocumentResult(
                request.RequestId,
                $"{request.RequestId}.txt",
                "text/plain",
                "txt",
                Encoding.UTF8.GetBytes("generated"),
                request.Metadata));
        }
    }

    private sealed class BlockingEngine(TimeSpan blockFor) : IDocumentGenerationEngine
    {
        public int Calls { get; private set; }

        public string Name => "flaky";

        public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null) => true;

        public async Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;

            await Task.Delay(blockFor, cancellationToken);

            return new GeneratedDocumentResult(
                request.RequestId,
                $"{request.RequestId}.txt",
                "text/plain",
                "txt",
                Encoding.UTF8.GetBytes("generated"),
                request.Metadata);
        }
    }
}
