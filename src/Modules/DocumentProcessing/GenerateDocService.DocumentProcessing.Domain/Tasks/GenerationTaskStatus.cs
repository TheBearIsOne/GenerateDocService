namespace GenerateDocService.DocumentProcessing.Domain.Tasks;

public enum GenerationTaskStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}
