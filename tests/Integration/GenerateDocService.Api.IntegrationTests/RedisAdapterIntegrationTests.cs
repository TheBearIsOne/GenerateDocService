using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Infrastructure.Caching;
using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// Starts a real Redis and builds the infrastructure graph from configuration, so the adapters are
/// resolved exactly as the hosts resolve them rather than being hand-constructed.
/// </summary>
public sealed class RedisAdapterFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    public ServiceProvider? Services { get; private set; }

    public string? UnavailabilityReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder("redis:7").Build();
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            UnavailabilityReason = $"Redis container could not start: {exception.GetType().Name}: {exception.Message}";
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentProcessing:Caching:Provider"] = "Redis",
                ["DocumentProcessing:Caching:Redis:ConnectionString"] = _container.GetConnectionString(),
                ["DocumentProcessing:Caching:Redis:KeyPrefix"] = "gds-test",
                ["DocumentProcessing:Caching:Redis:GeneratedDocumentTtlMinutes"] = "60",
                ["DocumentProcessing:Caching:Redis:DeduplicationTtlMinutes"] = "15",
                ["DocumentProcessing:Caching:Redis:CompiledTemplateTtlMinutes"] = "30"
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

public sealed class RedisAdapterIntegrationTests : IClassFixture<RedisAdapterFixture>
{
    private readonly RedisAdapterFixture _fixture;

    public RedisAdapterIntegrationTests(RedisAdapterFixture fixture) => _fixture = fixture;

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public void RedisProvider_ShouldReplaceEveryInMemoryCacheAbstraction()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        _fixture.Services!.GetRequiredService<IGeneratedDocumentCache>()
            .Should().BeOfType<RedisGeneratedDocumentCache>();
        _fixture.Services.GetRequiredService<IGenerationRequestDeduplicationStore>()
            .Should().BeOfType<RedisGenerationRequestDeduplicationStore>();
        _fixture.Services.GetRequiredService<GenerateDocService.Engine.Abstractions.ICompiledTemplateCache>()
            .Should().BeOfType<RedisCompiledTemplateCache>();
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task DeduplicationStore_ShouldKeepTheFirstOwnerUntilReleased()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var store = _fixture.Services!.GetRequiredService<IGenerationRequestDeduplicationStore>();
        var fingerprint = $"fp-{Guid.NewGuid():N}";

        var first = await store.TryReserveAsync(fingerprint, "task-a");
        var second = await store.TryReserveAsync(fingerprint, "task-b");

        first.IsOwner.Should().BeTrue();
        second.IsOwner.Should().BeFalse();
        second.TaskId.Should().Be("task-a");

        // A release from a non-owner must not steal somebody else's reservation.
        await store.ReleaseAsync(fingerprint, "task-b");
        (await store.TryReserveAsync(fingerprint, "task-c")).IsOwner.Should().BeFalse();

        await store.ReleaseAsync(fingerprint, "task-a");
        var afterRelease = await store.TryReserveAsync(fingerprint, "task-d");
        afterRelease.IsOwner.Should().BeTrue();
        afterRelease.TaskId.Should().Be("task-d");
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task DeduplicationStore_ShouldApplyTheConfiguredTtl()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var store = _fixture.Services!.GetRequiredService<IGenerationRequestDeduplicationStore>();
        var fingerprint = $"fp-ttl-{Guid.NewGuid():N}";

        await store.TryReserveAsync(fingerprint, "task-a");

        var database = _fixture.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var ttl = await database.KeyTimeToLiveAsync($"gds-test:dedup:{fingerprint}");

        ttl.Should().NotBeNull("a reservation without expiry would pin a fingerprint forever");
        ttl!.Value.Should().BeCloseTo(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(30));
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task GeneratedDocumentCache_ShouldRoundTripADocument()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var cache = _fixture.Services!.GetRequiredService<IGeneratedDocumentCache>();
        var fingerprint = $"fp-doc-{Guid.NewGuid():N}";
        var document = new CachedGeneratedDocument(
            "req-1",
            "report.txt",
            "text/plain",
            "txt",
            11,
            "checksum-1",
            new Dictionary<string, string> { ["client"] = "sample" },
            new DocumentArtifactReference("minio", "documents/req-1/report.txt", "documents", "req-1/report.txt"));

        (await cache.GetAsync(fingerprint)).Should().BeNull();

        await cache.SetAsync(fingerprint, document);
        var cached = await cache.GetAsync(fingerprint);

        cached.Should().NotBeNull();
        cached!.FileName.Should().Be("report.txt");
        cached.ContentType.Should().Be("text/plain");
        cached.ContentLength.Should().Be(11);
        cached.Checksum.Should().Be("checksum-1");
        cached.Metadata.Should().ContainKey("client").WhoseValue.Should().Be("sample");
        cached.Artifact.StoragePath.Should().Be("documents/req-1/report.txt");
        cached.Artifact.ObjectKey.Should().Be("req-1/report.txt");
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task CompiledTemplateCache_ShouldRoundTripAScribanTemplateThatStillRenders()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var cache = _fixture.Services!.GetRequiredService<GenerateDocService.Engine.Abstractions.ICompiledTemplateCache>();
        var key = $"tpl-{Guid.NewGuid():N}";

        var original = Scriban.Template.Parse("Hello {{ name }}");
        await cache.SetAsync(
            key,
            new GenerateDocService.Engine.Abstractions.TemplateParseResult("scriban", original, null),
            TimeSpan.Zero);

        var cached = await cache.GetAsync(key);

        cached.Should().NotBeNull();
        cached!.TemplateFormat.Should().Be("scriban");
        cached.CompiledTemplate.Should().BeOfType<Scriban.Template>();

        // The Redis adapter stores template text and re-parses it, so rendering is the only real
        // proof that the round trip preserved meaning rather than just producing an object.
        var context = new Scriban.TemplateContext();
        context.PushGlobal(new Scriban.Runtime.ScriptObject { ["name"] = "Ada" });
        var rendered = await ((Scriban.Template)cached.CompiledTemplate).RenderAsync(context);

        rendered.Should().Be("Hello Ada");
    }

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task CompiledTemplateCache_ShouldSilentlySkipANonScribanTemplate()
    {
        Skip.If(_fixture.Services is null, _fixture.UnavailabilityReason);

        var cache = _fixture.Services!.GetRequiredService<GenerateDocService.Engine.Abstractions.ICompiledTemplateCache>();
        var key = $"tpl-liquid-{Guid.NewGuid():N}";

        // Documents current behaviour: the adapter only stores Scriban templates, so DotLiquid
        // compilations are never cached under the Redis provider and re-parse on every request.
        await cache.SetAsync(
            key,
            new GenerateDocService.Engine.Abstractions.TemplateParseResult(
                "liquid",
                DotLiquid.Template.Parse("Hello {{ name }}"),
                null),
            TimeSpan.Zero);

        (await cache.GetAsync(key)).Should().BeNull();
    }
}
