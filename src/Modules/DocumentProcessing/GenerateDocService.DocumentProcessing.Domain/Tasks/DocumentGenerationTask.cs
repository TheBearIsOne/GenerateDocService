namespace GenerateDocService.DocumentProcessing.Domain.Tasks;

public sealed class DocumentGenerationTask
{
    public DocumentGenerationTask(string taskId, string requestId, string engine, string outputFormat)
    {
        TaskId = taskId;
        RequestId = requestId;
        Engine = engine;
        OutputFormat = outputFormat;
        Status = GenerationTaskStatus.Queued;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string TaskId { get; }
    public string RequestId { get; }
    public string Engine { get; }
    public string OutputFormat { get; }
    public GenerationTaskStatus Status { get; private set; }
    public string? ResultFileName { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    public void MarkProcessing()
    {
        Status = GenerationTaskStatus.Processing;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkCompleted(string resultFileName)
    {
        Status = GenerationTaskStatus.Completed;
        ResultFileName = resultFileName;
        Error = null;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = GenerationTaskStatus.Failed;
        Error = error;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
