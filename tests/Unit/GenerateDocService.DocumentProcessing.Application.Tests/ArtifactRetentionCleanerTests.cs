using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.DocumentProcessing.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class ArtifactRetentionCleanerTests
{
    [Fact]
    public async Task RunAsync_ShouldFailTask_AbandonedInProgress()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var task = await AddTaskAsync(repository, "stuck-1");
        task.MarkProcessing();
        await repository.UpdateAsync(task);

        await Task.Delay(1_200);

        var result = await CreateCleaner(repository, new InMemoryDocumentArtifactStore(), new ArtifactRetentionOptions
        {
            RetentionDays = 365,
            FailedTaskRetentionDays = 365,
            StuckProcessingTaskSeconds = 1
        }).RunAsync();

        result.StuckFailed.Should().Be(1);

        var stored = await repository.GetAsync("stuck-1");
        stored!.Status.Should().Be(GenerationTaskStatus.Failed);
        stored.Error.Should().Contain("abandoned");
    }

    [Fact]
    public async Task RunAsync_ShouldLeaveARecentProcessingTaskAlone()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var task = await AddTaskAsync(repository, "live-1");
        task.MarkProcessing();
        await repository.UpdateAsync(task);

        var result = await CreateCleaner(repository, new InMemoryDocumentArtifactStore(), new ArtifactRetentionOptions
        {
            RetentionDays = 365,
            FailedTaskRetentionDays = 365,
            StuckProcessingTaskSeconds = 3_600
        }).RunAsync();

        result.StuckFailed.Should().Be(0);
        (await repository.GetAsync("live-1"))!.Status.Should().Be(GenerationTaskStatus.Processing);
    }

    [Fact]
    public async Task RunAsync_ShouldNotReap_WhenTheThresholdIsZero()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var task = await AddTaskAsync(repository, "stuck-2");
        task.MarkProcessing();
        await repository.UpdateAsync(task);

        await Task.Delay(1_200);

        var result = await CreateCleaner(repository, new InMemoryDocumentArtifactStore(), new ArtifactRetentionOptions
        {
            RetentionDays = 365,
            FailedTaskRetentionDays = 365,
            StuckProcessingTaskSeconds = 0
        }).RunAsync();

        result.StuckFailed.Should().Be(0);
        (await repository.GetAsync("stuck-2"))!.Status.Should().Be(GenerationTaskStatus.Processing);
    }

    [Fact]
    public async Task RunAsync_ShouldDeleteCompletedTaskTogetherWithItsArtifact()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var store = new InMemoryDocumentArtifactStore();
        var task = await AddTaskAsync(repository, "done-1");

        var document = new GeneratedDocumentResult("req-1", "done-1.txt", "text/plain", "txt", [1, 2, 3]);
        var artifact = await store.SaveAsync(document);
        task.MarkCompleted(document.FileName, artifact.StoragePath);
        await repository.UpdateAsync(task);

        await Task.Delay(20);

        var result = await CreateCleaner(repository, store, new ArtifactRetentionOptions
        {
            RetentionDays = 0,
            FailedTaskRetentionDays = 365,
            StuckProcessingTaskSeconds = 3_600
        }).RunAsync();

        result.CompletedRemoved.Should().Be(1);
        (await repository.GetAsync("done-1")).Should().BeNull();
        (await store.GetAsync(artifact.StoragePath)).Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_ShouldDeleteExpiredFailedTask()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var task = await AddTaskAsync(repository, "bad-1");
        task.MarkFailed("engine exploded");
        await repository.UpdateAsync(task);

        await Task.Delay(20);

        var result = await CreateCleaner(repository, new InMemoryDocumentArtifactStore(), new ArtifactRetentionOptions
        {
            RetentionDays = 365,
            FailedTaskRetentionDays = 0,
            StuckProcessingTaskSeconds = 3_600
        }).RunAsync();

        result.FailedRemoved.Should().Be(1);
        (await repository.GetAsync("bad-1")).Should().BeNull();
    }

    private static ArtifactRetentionCleaner CreateCleaner(
        IDocumentGenerationTaskRepository repository,
        IDocumentArtifactStore artifactStore,
        ArtifactRetentionOptions options)
        => new(
            repository,
            artifactStore,
            Options.Create(options),
            NullLogger<ArtifactRetentionCleaner>.Instance);

    private static async Task<DocumentGenerationTask> AddTaskAsync(
        InMemoryDocumentGenerationTaskRepository repository,
        string taskId)
    {
        var task = new DocumentGenerationTask(taskId, "req-1", "scriban", "txt");
        await repository.AddAsync(task);

        return task;
    }
}
