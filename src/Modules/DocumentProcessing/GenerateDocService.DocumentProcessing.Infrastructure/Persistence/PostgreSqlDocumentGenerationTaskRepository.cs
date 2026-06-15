using GenerateDocService.DocumentProcessing.Application.Abstractions.Persistence;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence;

public sealed class PostgreSqlDocumentGenerationTaskRepository : IDocumentGenerationTaskRepository
{
    private readonly DocumentGenerationDbContext _dbContext;

    public PostgreSqlDocumentGenerationTaskRepository(DocumentGenerationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DocumentGenerationTask> AddAsync(
        DocumentGenerationTask task,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.DocumentGenerationTasks.AddAsync(task, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<DocumentGenerationTask?> GetAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentGenerationTasks
            .FirstOrDefaultAsync(e => e.TaskId == taskId, cancellationToken);
    }

    public async Task UpdateAsync(
        DocumentGenerationTask task,
        CancellationToken cancellationToken = default)
    {
        _dbContext.DocumentGenerationTasks.Update(task);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DocumentGenerationTasks
            .FirstOrDefaultAsync(e => e.TaskId == taskId, cancellationToken);

        if (task is not null)
        {
            _dbContext.DocumentGenerationTasks.Remove(task);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<DocumentGenerationTask>> GetExpiredTasksAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentGenerationTasks
            .Where(e => e.UpdatedAtUtc.HasValue && e.UpdatedAtUtc.Value < olderThan)
            .ToListAsync(cancellationToken);
    }
}
