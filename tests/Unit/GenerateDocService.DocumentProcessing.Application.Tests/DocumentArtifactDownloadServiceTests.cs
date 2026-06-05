using System.Text;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class DocumentArtifactDownloadServiceTests
{
    [Fact]
    public async Task GetAsync_ShouldReturnStoredArtifact_ForCompletedTask()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var artifactStore = new InMemoryDocumentArtifactStore();
        var service = new DocumentArtifactDownloadService(repository, artifactStore);

        using var result = new GenerateDocService.DocumentProcessing.Application.Models.GeneratedDocumentResult(
            requestId: "req-1",
            fileName: "req-1.txt",
            contentType: "text/plain",
            outputFormat: "txt",
            content: Encoding.UTF8.GetBytes("hello world"),
            metadata: null);

        var artifact = await artifactStore.SaveAsync(result);

        var task = new DocumentGenerationTask("task-1", "req-1", "scriban", "txt");
        task.MarkCompleted(result.FileName, artifact.StoragePath);
        await repository.AddAsync(task);

        var storedArtifact = await service.GetAsync("task-1");

        storedArtifact.Should().NotBeNull();
        storedArtifact!.FileName.Should().Be("req-1.txt");
        Encoding.UTF8.GetString(storedArtifact.ToByteArray()).Should().Be("hello world");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenTaskIsNotCompleted()
    {
        var repository = new InMemoryDocumentGenerationTaskRepository();
        var artifactStore = new InMemoryDocumentArtifactStore();
        var service = new DocumentArtifactDownloadService(repository, artifactStore);

        var task = new DocumentGenerationTask("task-2", "req-2", "scriban", "txt");
        await repository.AddAsync(task);

        var storedArtifact = await service.GetAsync("task-2");

        storedArtifact.Should().BeNull();
    }
}
