using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Presentation.Contracts;

public sealed record TaskStatusHttpResponse(
    string TaskId,
    GenerationTaskStatus Status,
    string? ResultFileName,
    string? ResultStoragePath,
    string? DownloadUrl,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
