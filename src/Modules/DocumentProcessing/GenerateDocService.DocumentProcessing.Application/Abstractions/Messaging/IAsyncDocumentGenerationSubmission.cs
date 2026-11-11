using GenerateDocService.DocumentProcessing.Application.Messaging;
using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;

public interface IAsyncDocumentGenerationSubmission
{
    Task SubmitAsync(
        DocumentGenerationTask task,
        GenerateDocumentRequested message,
        CancellationToken cancellationToken = default);
}