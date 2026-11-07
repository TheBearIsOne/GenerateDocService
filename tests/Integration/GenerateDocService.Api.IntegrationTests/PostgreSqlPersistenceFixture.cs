using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// Starts a real PostgreSQL and applies the production startup migration path once per class.
/// Docker unavailability is recorded instead of thrown so the tests can skip cleanly.
/// </summary>
public sealed class PostgreSqlPersistenceFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public ServiceProvider? Services { get; private set; }

    public IConfiguration? Configuration { get; private set; }

    public string? UnavailabilityReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            // Pinned to the same major version the compose stack runs.
            _container = new PostgreSqlBuilder("postgres:17").Build();
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            UnavailabilityReason =
                $"PostgreSQL container could not start: {exception.GetType().Name}: {exception.Message}";
            return;
        }

        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DocumentProcessing:Persistence:Provider"] = "PostgreSql",
                ["DocumentProcessing:Persistence:PostgreSql:ConnectionString"] = _container.GetConnectionString()
            })
            .Build();

        Services = new ServiceCollection()
            .AddLogging()
            .AddDocumentProcessingInfrastructure(Configuration)
            .BuildServiceProvider();

        // The same call both hosts make on startup. When it was missing, every async request failed
        // with 42P01 "relation document_generation_tasks does not exist" behind a fully green suite.
        await Services.ApplyDocumentProcessingMigrationsAsync(Configuration);
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
