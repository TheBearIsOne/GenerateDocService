using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class InMemoryProviderBoundsTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DocumentCache_ShouldServeDocument_BeforeItsTtlExpires()
    {
        var clock = new MutableClock(Epoch);
        var cache = CreateCache(clock, options => options.GeneratedDocumentTtlMinutes = 60);

        await cache.SetAsync("fp-1", CreateDocument("fp-1"));
        clock.Now = Epoch.AddMinutes(59);

        var cached = await cache.GetAsync("fp-1");

        cached.Should().NotBeNull();
        cached!.FileName.Should().Be("fp-1.txt");
    }

    [Fact]
    public async Task DocumentCache_ShouldDropDocument_AfterItsTtlExpires()
    {
        var clock = new MutableClock(Epoch);
        var cache = CreateCache(clock, options => options.GeneratedDocumentTtlMinutes = 60);

        await cache.SetAsync("fp-1", CreateDocument("fp-1"));
        clock.Now = Epoch.AddMinutes(61);

        (await cache.GetAsync("fp-1")).Should().BeNull();
    }

    [Fact]
    public async Task DocumentCache_ShouldNeverExpire_WhenTtlIsZero()
    {
        var clock = new MutableClock(Epoch);
        var cache = CreateCache(clock, options =>
        {
            options.GeneratedDocumentTtlMinutes = 0;
            options.MaxCachedDocuments = 0;
        });

        await cache.SetAsync("fp-1", CreateDocument("fp-1"));
        clock.Now = Epoch.AddYears(10);

        (await cache.GetAsync("fp-1")).Should().NotBeNull();
    }

    [Fact]
    public async Task DocumentCache_ShouldEvictOldestEntries_WhenCapacityIsExceeded()
    {
        var clock = new MutableClock(Epoch);
        var cache = CreateCache(clock, options =>
        {
            options.GeneratedDocumentTtlMinutes = 0;
            options.MaxCachedDocuments = 2;
        });

        await cache.SetAsync("fp-1", CreateDocument("fp-1"));
        clock.Now = Epoch.AddSeconds(1);
        await cache.SetAsync("fp-2", CreateDocument("fp-2"));
        clock.Now = Epoch.AddSeconds(2);
        await cache.SetAsync("fp-3", CreateDocument("fp-3"));

        (await cache.GetAsync("fp-1")).Should().BeNull("it was the oldest entry over capacity");
        (await cache.GetAsync("fp-2")).Should().NotBeNull();
        (await cache.GetAsync("fp-3")).Should().NotBeNull();
    }

    [Fact]
    public async Task Deduplication_ShouldKeepTheFirstOwner_WhileItsReservationIsLive()
    {
        var clock = new MutableClock(Epoch);
        var store = CreateDeduplicationStore(clock, options => options.DeduplicationTtlMinutes = 15);

        var first = await store.TryReserveAsync("fp-1", "task-1");
        clock.Now = Epoch.AddMinutes(14);
        var second = await store.TryReserveAsync("fp-1", "task-2");

        first.IsOwner.Should().BeTrue();
        first.TaskId.Should().Be("task-1");
        second.IsOwner.Should().BeFalse();
        second.TaskId.Should().Be("task-1");
    }

    [Fact]
    public async Task Deduplication_ShouldHandOwnershipToANewTask_AfterTheReservationExpires()
    {
        var clock = new MutableClock(Epoch);
        var store = CreateDeduplicationStore(clock, options => options.DeduplicationTtlMinutes = 15);

        _ = await store.TryReserveAsync("fp-1", "task-1");
        clock.Now = Epoch.AddMinutes(16);

        var retry = await store.TryReserveAsync("fp-1", "task-2");

        retry.IsOwner.Should().BeTrue("an abandoned task must not pin its fingerprint forever");
        retry.TaskId.Should().Be("task-2");
    }

    [Fact]
    public async Task Deduplication_ShouldReleaseImmediately_WhenTheOwnerReleases()
    {
        var clock = new MutableClock(Epoch);
        var store = CreateDeduplicationStore(clock, options => options.DeduplicationTtlMinutes = 15);

        _ = await store.TryReserveAsync("fp-1", "task-1");
        await store.ReleaseAsync("fp-1", "task-1");

        var retry = await store.TryReserveAsync("fp-1", "task-2");

        retry.IsOwner.Should().BeTrue();
        retry.TaskId.Should().Be("task-2");
    }

    [Fact]
    public async Task Deduplication_ShouldIgnoreReleaseFromANonOwner()
    {
        var clock = new MutableClock(Epoch);
        var store = CreateDeduplicationStore(clock, options => options.DeduplicationTtlMinutes = 15);

        _ = await store.TryReserveAsync("fp-1", "task-1");
        await store.ReleaseAsync("fp-1", "task-2");

        (await store.TryReserveAsync("fp-1", "task-3")).IsOwner.Should().BeFalse();
    }

    private static InMemoryGeneratedDocumentCache CreateCache(
        MutableClock clock,
        Action<InMemoryProviderOptions> configure)
    {
        var options = new InMemoryProviderOptions();
        configure(options);

        return new InMemoryGeneratedDocumentCache(Options.Create(options), clock);
    }

    private static InMemoryGenerationRequestDeduplicationStore CreateDeduplicationStore(
        MutableClock clock,
        Action<InMemoryProviderOptions> configure)
    {
        var options = new InMemoryProviderOptions();
        configure(options);

        return new InMemoryGenerationRequestDeduplicationStore(Options.Create(options), clock);
    }

    private static CachedGeneratedDocument CreateDocument(string requestId)
        => new(
            requestId,
            $"{requestId}.txt",
            "text/plain",
            "txt",
            5,
            "checksum",
            new Dictionary<string, string>(),
            new DocumentArtifactReference("in-memory", $"artifacts/{requestId}/{requestId}.txt", "artifacts", requestId));

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
