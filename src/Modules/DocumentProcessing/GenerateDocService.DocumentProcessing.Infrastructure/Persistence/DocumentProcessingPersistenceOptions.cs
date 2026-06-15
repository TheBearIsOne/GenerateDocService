namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence;

public sealed class DocumentProcessingPersistenceOptions
{
    public const string SectionName = "DocumentProcessing:Persistence";

    public string Provider { get; set; } = "InMemory";

    public PostgreSqlPersistenceOptions PostgreSql { get; set; } = new();

    public bool IsPostgreSqlProvider()
        => string.Equals(Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase);
}

public sealed class PostgreSqlPersistenceOptions
{
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=generatedocservice;Username=postgres;Password=postgres";
}
