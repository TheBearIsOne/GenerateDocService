using System.Text.Json;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Domain.Tasks;
using GenerateDocService.DocumentProcessing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Messaging;

public sealed class PostgreSqlAsyncDocumentGenerationSubmission(
    DocumentGenerationDbContext dbContext) : IAsyncDocumentGenerationSubmission
{
    public async Task SubmitAsync(
        DocumentGenerationTask task,
        GenerateDocumentRequested message,
        CancellationToken cancellationToken = default)
    {
        task.QueueMessageForDispatch(JsonSerializer.Serialize(message));
        await dbContext.DocumentGenerationTasks.AddAsync(task, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
