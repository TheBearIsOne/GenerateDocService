using GenerateDocService.DocumentProcessing.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence;

public sealed class DocumentGenerationDbContext : DbContext
{
    public DocumentGenerationDbContext(DbContextOptions<DocumentGenerationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DocumentGenerationTask> DocumentGenerationTasks => Set<DocumentGenerationTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentGenerationTask>(entity =>
        {
            entity.HasKey(e => e.TaskId);
            entity.ToTable("document_generation_tasks");

            entity.Property(e => e.TaskId)
                .HasColumnName("task_id")
                .HasMaxLength(64);

            entity.Property(e => e.RequestId)
                .HasColumnName("request_id")
                .HasMaxLength(64);

            entity.Property(e => e.Engine)
                .HasColumnName("engine")
                .HasMaxLength(64);

            entity.Property(e => e.OutputFormat)
                .HasColumnName("output_format")
                .HasMaxLength(32);

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<int>();

            entity.Property(e => e.ResultFileName)
                .HasColumnName("result_file_name")
                .HasMaxLength(256);

            entity.Property(e => e.ResultStoragePath)
                .HasColumnName("result_storage_path")
                .HasMaxLength(1024);

            entity.Property(e => e.Error)
                .HasColumnName("error");

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc");

            entity.Property(e => e.UpdatedAtUtc)
                .HasColumnName("updated_at_utc");
        });
    }
}
