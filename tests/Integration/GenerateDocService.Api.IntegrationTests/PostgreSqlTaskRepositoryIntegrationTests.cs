using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.DocumentProcessing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GenerateDocService.Api.IntegrationTests;

public sealed class PostgreSqlTaskRepositoryIntegrationTests : IClassFixture<PostgreSqlPersistenceFixture>
{
    private readonly PostgreSqlPersistenceFixture _fixture;

    public PostgreSqlTaskRepositoryIntegrationTests(PostgreSqlPersistenceFixture fixture) => _fixture = fixture;

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task Repository_ShouldRoundTripATask_ThroughTheMigratedSchema()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        using var scope = _fixture.Services!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentGenerationTaskRepository>();
        repository.Should().BeOfType<PostgreSqlDocumentGenerationTaskRepository>();

        var taskId = $"pg-{Guid.NewGuid():N}";
        await repository.AddAsync(new DocumentGenerationTask(taskId, "req-pg", "scriban", "txt"));

        var loaded = await repository.GetAsync(taskId);
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(GenerationTaskStatus.Queued);
        loaded.Engine.Should().Be("scriban");
        loaded.OutputFormat.Should().Be("txt");

        loaded.MarkCompleted($"{taskId}.txt", $"artifacts/req-pg/{taskId}.txt");
        await repository.UpdateAsync(loaded);

        var completed = await repository.GetAsync(taskId);
        completed!.Status.Should().Be(GenerationTaskStatus.Completed);
        completed.ResultFileName.Should().Be($"{taskId}.txt");
        completed.ResultStoragePath.Should().Be($"artifacts/req-pg/{taskId}.txt");

        await repository.DeleteAsync(taskId);
        (await repository.GetAsync(taskId)).Should().BeNull();
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task GetExpiredTasks_ShouldOnlyReturnTasksUpdatedBeforeTheCutoff()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        using var scope = _fixture.Services!.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentGenerationTaskRepository>();

        var oldTaskId = $"pg-old-{Guid.NewGuid():N}";
        var oldTask = new DocumentGenerationTask(oldTaskId, "req-old", "scriban", "txt");
        oldTask.MarkFailed("engine exploded");
        await repository.AddAsync(oldTask);

        var freshTaskId = $"pg-fresh-{Guid.NewGuid():N}";
        await repository.AddAsync(new DocumentGenerationTask(freshTaskId, "req-fresh", "scriban", "txt"));

        var expired = await repository.GetExpiredTasksAsync(DateTimeOffset.UtcNow.AddDays(-7));

        expired.Select(t => t.TaskId).Should().NotContain(freshTaskId, "it was created moments ago");
        expired.Should().OnlyContain(t => t.UpdatedAtUtc.HasValue);

        await repository.DeleteAsync(oldTaskId);
        await repository.DeleteAsync(freshTaskId);
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task FailedTaskState_ShouldSurviveAReloadFromTheDatabase()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var taskId = $"pg-failed-{Guid.NewGuid():N}";

        using (var scope = _fixture.Services!.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentGenerationTaskRepository>();
            var task = new DocumentGenerationTask(taskId, "req-failed", "scriban", "txt");
            await repository.AddAsync(task);

            task.MarkProcessing();
            await repository.UpdateAsync(task);
            task.MarkFailed("converter unreachable");
            await repository.UpdateAsync(task);
        }

        using (var scope = _fixture.Services!.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentGenerationTaskRepository>();
            var reloaded = await repository.GetAsync(taskId);

            reloaded!.Status.Should().Be(GenerationTaskStatus.Failed);
            reloaded.Error.Should().Be("converter unreachable");
            reloaded.ResultStoragePath.Should().BeNull();
        }

        using var cleanup = _fixture.Services!.CreateScope();
        await cleanup.ServiceProvider.GetRequiredService<IDocumentGenerationTaskRepository>().DeleteAsync(taskId);
    }
}
