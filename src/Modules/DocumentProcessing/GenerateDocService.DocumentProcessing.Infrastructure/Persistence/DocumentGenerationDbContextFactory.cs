using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence;

public sealed class DocumentGenerationDbContextFactory : IDesignTimeDbContextFactory<DocumentGenerationDbContext>
{
    public DocumentGenerationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DocumentGenerationDbContext>()
            .UseNpgsql(new PostgreSqlPersistenceOptions().ConnectionString)
            .Options;

        return new DocumentGenerationDbContext(options);
    }
}
