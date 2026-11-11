using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using GenerateDocService.DocumentProcessing.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// Starts a real MinIO and builds the infrastructure graph from configuration, so the artifact store
/// is resolved and constructed exactly as the hosts do it.
/// </summary>
public sealed class MinioArtifactStoreFixture : IAsyncLifetime
{
    private MinioContainer? _container;

    public ServiceProvider? Services { get; private set; }

    public string? UnavailabilityReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MinioBuilder("minio/minio:latest").Build();
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            UnavailabilityReason = $"MinIO container could not start: {exception.GetType().Name}: {exception.Message}";
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentProcessing:Storage:Provider"] = "ObjectStorage",
                // Passed through verbatim so the test also exercises BuildMinioEndpoint's
                // normalisation of a connection string that is not already an absolute URI.
                ["DocumentProcessing:Storage:ObjectStorage:Endpoint"] = _container.GetConnectionString(),
                ["DocumentProcessing:Storage:ObjectStorage:BucketName"] = "documents",
                ["DocumentProcessing:Storage:ObjectStorage:AccessKey"] = _container.GetAccessKey(),
                ["DocumentProcessing:Storage:ObjectStorage:SecretKey"] = _container.GetSecretKey(),
                ["DocumentProcessing:Storage:ObjectStorage:UseSsl"] = "false",
                ["DocumentProcessing:Storage:ObjectStorage:CreateBucketIfMissing"] = "true"
            })
            .Build();

        Services = new ServiceCollection()
            .AddLogging()
            .AddDocumentProcessingInfrastructure(configuration)
            .BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        Services?.Dispose();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

public sealed class MinioArtifactStoreIntegrationTests : IClassFixture<MinioArtifactStoreFixture>
{
    private readonly MinioArtifactStoreFixture _fixture;

    public MinioArtifactStoreIntegrationTests(MinioArtifactStoreFixture fixture) => _fixture = fixture;

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public void ObjectStorageProvider_ShouldReplaceTheInMemoryArtifactStore()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        _fixture.Services!.GetRequiredService<IDocumentArtifactStore>()
            .Should().BeOfType<MinioDocumentArtifactStore>();
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task ArtifactStore_ShouldCreateTheBucketAndRoundTripAnArtifact()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var store = _fixture.Services!.GetRequiredService<IDocumentArtifactStore>();
        var content = Encoding.UTF8.GetBytes("generated document body");
        var requestId = Guid.NewGuid().ToString("N");

        var reference = await store.SaveAsync(new GeneratedDocumentResult(
            requestId, $"{requestId}.txt", "text/plain", "txt", content));

        reference.Provider.Should().Be("minio");
        reference.Container.Should().Be("documents");
        reference.ObjectKey.Should().Be($"{requestId}/{requestId}.txt");
        reference.StoragePath.Should().Be($"documents/{requestId}/{requestId}.txt");

        var stored = await store.GetAsync(reference.StoragePath);

        stored.Should().NotBeNull();
        stored!.FileName.Should().Be($"{requestId}.txt");
        stored.ContentType.Should().Be("text/plain");
        stored.OutputFormat.Should().Be("txt");
        stored.ToByteArray().Should().Equal(content);
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task ArtifactStore_ShouldMakeAnArtifactUnavailableAfterDelete()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var store = _fixture.Services!.GetRequiredService<IDocumentArtifactStore>();
        var requestId = Guid.NewGuid().ToString("N");

        var reference = await store.SaveAsync(new GeneratedDocumentResult(
            requestId, $"{requestId}.txt", "text/plain", "txt", [1, 2, 3]));

        (await store.GetAsync(reference.StoragePath)).Should().NotBeNull();

        await store.DeleteAsync(reference.StoragePath);

        // GetAsync stats the object first, so a removed object surfaces as a failure rather than null.
        var act = () => store.GetAsync(reference.StoragePath);
        await act.Should().ThrowAsync<Exception>();
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task ArtifactStore_ShouldIgnoreAMalformedStoragePath()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var store = _fixture.Services!.GetRequiredService<IDocumentArtifactStore>();

        (await store.GetAsync("no-separator")).Should().BeNull();
        (await store.GetAsync("/leading-separator")).Should().BeNull();
        (await store.GetAsync("trailing/")).Should().BeNull();

        var act = () => store.DeleteAsync("no-separator");
        await act.Should().NotThrowAsync();
    }
}
