using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Presentation.Contracts;

public static class DocumentResponseMapper
{
    public static TaskStatusHttpResponse ToHttpResponse(TaskStatusResponse response)
        => new(
            response.TaskId,
            response.Status,
            response.ResultFileName,
            response.Error,
            response.CreatedAtUtc,
            response.UpdatedAtUtc);
}
