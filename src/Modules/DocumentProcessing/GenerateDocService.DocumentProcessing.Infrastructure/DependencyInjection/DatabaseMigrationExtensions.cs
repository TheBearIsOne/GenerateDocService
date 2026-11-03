using System.Data.Common;
using GenerateDocService.DocumentProcessing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;

public static class DatabaseMigrationExtensions
{
    // Arbitrary stable key identifying this schema's migration lock across hosts.
    private const long MigrationLockKey = 0x474453_4D4947;

    public static async Task ApplyDocumentProcessingMigrationsAsync(
        this IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var persistenceOptions = configuration
            .GetSection(DocumentProcessingPersistenceOptions.SectionName)
            .Get<DocumentProcessingPersistenceOptions>()
            ?? new DocumentProcessingPersistenceOptions();

        if (!persistenceOptions.IsPostgreSqlProvider())
        {
            return;
        }

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentGenerationDbContext>();

        // Every host applies migrations on startup, so serialise them behind an
        // advisory lock held on the same session EF migrates through.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await ExecuteAdvisoryLockAsync(dbContext.Database.GetDbConnection(), "pg_advisory_lock", cancellationToken);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            try
            {
                await ExecuteAdvisoryLockAsync(dbContext.Database.GetDbConnection(), "pg_advisory_unlock", cancellationToken);
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task ExecuteAdvisoryLockAsync(
        DbConnection connection,
        string functionName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {functionName}(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@key";
        parameter.Value = MigrationLockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
