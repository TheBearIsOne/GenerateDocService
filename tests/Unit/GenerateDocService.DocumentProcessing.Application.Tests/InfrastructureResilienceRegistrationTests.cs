using GenerateDocService.DocumentProcessing.Application.Resilience;
using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using GenerateDocService.Engine.Fop;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class InfrastructureResilienceRegistrationTests
{
    [Fact]
    public void FopHttpClient_ShouldBuildItsResiliencePipeline_FromDefaultConfiguration()
    {
        using var provider = BuildProvider([]);

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(FopDocumentGenerationEngine.HttpClientName);

        client.BaseAddress.Should().Be(new Uri("http://localhost:8080/"));

        // Building the client materialises the Polly pipeline, so reaching this point proves the
        // resilience options satisfy the library's validation. The standard handler also takes over
        // timeout handling and disables the client-level timeout.
        client.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void FopRegistration_ShouldRejectRelativeBaseUrl()
    {
        var act = () => BuildProvider(new Dictionary<string, string?>
        {
            ["DocumentProcessing:Fop:BaseUrl"] = "converter.internal"
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*absolute URI*");
    }

    [Fact]
    public void ResilienceOptions_ShouldBindFromConfiguration()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DocumentProcessing:Resilience:MaxAttempts"] = "5",
            ["DocumentProcessing:Resilience:ProcessingTimeoutSeconds"] = "42",
            ["DocumentProcessing:Fop:TotalTimeoutSeconds"] = "30"
        });

        var options = provider.GetRequiredService<IOptions<GenerationResilienceOptions>>().Value;
        options.MaxAttempts.Should().Be(5);
        options.ProcessingTimeoutSeconds.Should().Be(42);

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(FopDocumentGenerationEngine.HttpClientName);
        client.BaseAddress.Should().Be(new Uri("http://localhost:8080/"));
    }

    [Fact]
    public void RedisProvider_ShouldTeachTheClassifierAboutRedisConnectionFaults()
    {
        using var inMemory = BuildProvider([]);
        using var withRedis = BuildProvider(new Dictionary<string, string?>
        {
            ["DocumentProcessing:Caching:Provider"] = "Redis",
            ["DocumentProcessing:Caching:Redis:ConnectionString"] = "localhost:6379"
        });

        // RedisConnectionException derives from RedisException, not from any BCL network fault, so it
        // is only classified as transient once the Redis provider registers it.
        var redisFault = new StackExchange.Redis.RedisConnectionException(
            StackExchange.Redis.ConnectionFailureType.UnableToConnect,
            "connection refused");

        inMemory.GetRequiredService<TransientFailureClassifier>().IsTransient(redisFault).Should().BeFalse();
        withRedis.GetRequiredService<TransientFailureClassifier>().IsTransient(redisFault).Should().BeTrue();
    }

    [Fact]
    public void AwkwardFopTimeoutCombination_ShouldStillBuildAValidPipeline()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["DocumentProcessing:Fop:AttemptTimeoutSeconds"] = "60",
            ["DocumentProcessing:Fop:TotalTimeoutSeconds"] = "10",
            ["DocumentProcessing:Fop:MaxRetryAttempts"] = "2",
            ["DocumentProcessing:Fop:CircuitBreakerSamplingDurationSeconds"] = "5"
        });

        // A 10s total cannot cover 3 attempts of 60s, and a 5s breaker window cannot cover a 60s
        // attempt. Both get widened rather than failing validation on the first request.
        var act = () => provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(FopDocumentGenerationEngine.HttpClientName);

        act.Should().NotThrow();
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDocumentProcessingInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}
