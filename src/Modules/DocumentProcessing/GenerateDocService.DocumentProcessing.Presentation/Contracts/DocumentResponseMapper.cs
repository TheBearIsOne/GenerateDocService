using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Presentation.Contracts;

public static class DocumentResponseMapper
{
    public static DocumentEngineHttpResponse ToHttpResponse(DocumentGenerationEngineDescriptor descriptor)
        => new(
            descriptor.Name,
            descriptor.InputFormats,
            descriptor.OutputFormats,
            descriptor.TemplateFormats,
            descriptor.Priority,
            descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name);

    public static TaskStatusHttpResponse ToHttpResponse(TaskStatusResponse response, string? downloadUrl = null)
        => new(
            response.TaskId,
            response.Status,
            response.ResultFileName,
            response.ResultStoragePath,
            downloadUrl,
            response.Error,
            response.CreatedAtUtc,
            response.UpdatedAtUtc);
}
