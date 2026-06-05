using GenerateDocService.DocumentProcessing.Domain.Tasks;

namespace GenerateDocService.DocumentProcessing.Application.Models;

public sealed record TaskStatusResponse(
    string TaskId,
    GenerationTaskStatus Status,
    string? ResultFileName,
    string? ResultStoragePath,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
