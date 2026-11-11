using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// Boots a real host against a real RabbitMQ so the scheduler's send address and the consumer's
/// receive endpoint are proven to agree. A queue-name mismatch leaves messages unconsumed and tasks
/// stuck at Queued, which the in-memory transport cannot reproduce because it publishes by type.
/// </summary>
public sealed class RabbitMqGenerationPipelineFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;
    private PostgreSqlContainer? _postgresContainer;

    public IHost? Host { get; private set; }

    public string? UnavailabilityReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            // Pinned to the same image the compose stack runs.
            _container = new RabbitMqBuilder("rabbitmq:3-management").Build();
            await _container.StartAsync();
            _postgresContainer = new PostgreSqlBuilder("postgres:17").Build();
            await _postgresContainer.StartAsync();
        }
        catch (Exception exception)
        {
            UnavailabilityReason =
                $"RabbitMQ/PostgreSQL containers could not start: {exception.GetType().Name}: {exception.Message}";
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }

            if (_postgresContainer is not null)
            {
                await _postgresContainer.DisposeAsync();
            }

            return;
        }

        var endpoint = new Uri(_container.GetConnectionString());
        var credentials = endpoint.UserInfo.Split(':', 2);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DocumentProcessing:Messaging:Transport"] = "RabbitMQ",
            ["DocumentProcessing:Messaging:QueueName"] = "document-generation",
            ["DocumentProcessing:Messaging:RabbitMq:Host"] = endpoint.Host,
            ["DocumentProcessing:Messaging:RabbitMq:Port"] = endpoint.Port.ToString(),
            ["DocumentProcessing:Messaging:RabbitMq:VirtualHost"] = "/",
            ["DocumentProcessing:Messaging:RabbitMq:Username"] = Uri.UnescapeDataString(credentials[0]),
            ["DocumentProcessing:Messaging:RabbitMq:Password"] = Uri.UnescapeDataString(credentials.Length > 1 ? credentials[1] : string.Empty),
            ["DocumentProcessing:Persistence:Provider"] = "PostgreSql",
            ["DocumentProcessing:Persistence:PostgreSql:ConnectionString"] = _postgresContainer.GetConnectionString()
        });

        builder.Services.AddDocumentProcessingInfrastructure(builder.Configuration);

        Host = builder.Build();
    await Host.Services.ApplyDocumentProcessingMigrationsAsync(builder.Configuration);
        await Host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }
}

public sealed class RabbitMqGenerationPipelineIntegrationTests : IClassFixture<RabbitMqGenerationPipelineFixture>
{
    private readonly RabbitMqGenerationPipelineFixture _fixture;

    public RabbitMqGenerationPipelineIntegrationTests(RabbitMqGenerationPipelineFixture fixture) => _fixture = fixture;

    [SkippableFact]
    [Trait("Category", "RequiresDocker")]
    public async Task AsyncRequest_ShouldBeConsumedFromTheConfiguredQueue_AndComplete()
    {
        Skip.If(_fixture.Host is null, _fixture.UnavailabilityReason);

        using var scope = _fixture.Host!.Services.CreateScope();
        var generation = scope.ServiceProvider.GetRequiredService<AsyncDocumentGenerationService>();
        var status = scope.ServiceProvider.GetRequiredService<TaskStatusQueryService>();

        var accepted = await generation.EnqueueAsync(new GenerateDocumentRequest(
            requestId: Guid.NewGuid().ToString("N"),
            engine: "scriban",
            inputFormat: "json",
            outputFormat: "txt",
            templateFormat: "scriban",
            payload: Encoding.UTF8.GetBytes("""{"document":"hello"}"""),
            template: Encoding.UTF8.GetBytes("Document: {{ document }}"),
            metadata: new Dictionary<string, string>()));

        accepted.Status.Should().Be(GenerationTaskStatus.Queued);

        var final = await WaitForFinalStatusAsync(status, accepted.TaskId, TimeSpan.FromSeconds(90));

        final.Status.Should().Be(
            GenerationTaskStatus.Completed,
            "the receive endpoint must be bound to DocumentProcessing:Messaging:QueueName, not the convention-derived message name");
        final.ResultFileName.Should().EndWith(".txt");
        final.Error.Should().BeNull();

        var persistedTask = await scope.ServiceProvider
            .GetRequiredService<GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence.IDocumentGenerationTaskRepository>()
            .GetAsync(accepted.TaskId);
        persistedTask!.PendingMessageJson.Should().BeNull("the RabbitMQ send has been acknowledged and the outbox row should be cleared");
    }

    private static async Task<TaskStatusResponse> WaitForFinalStatusAsync(
        TaskStatusQueryService status,
        string taskId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await status.GetAsync(taskId);
            if (current is { Status: GenerationTaskStatus.Completed or GenerationTaskStatus.Failed })
            {
                return current;
            }

            await Task.Delay(250);
        }

        return await status.GetAsync(taskId)
            ?? throw new InvalidOperationException($"Task '{taskId}' disappeared while polling.");
    }
}
