namespace GenerateDocService.DocumentProcessing.Infrastructure.Resilience;

public sealed class DocumentProcessingFopOptions
{
    public const string SectionName = "DocumentProcessing:Fop";

    public string BaseUrl { get; set; } = "http://localhost:8080/";
    public int AttemptTimeoutSeconds { get; set; } = 30;
    public int TotalTimeoutSeconds { get; set; } = 120;
    public int MaxRetryAttempts { get; set; } = 2;
    public int RetryBaseDelayMilliseconds { get; set; } = 1_000;
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;
    public int CircuitBreakerSamplingDurationSeconds { get; set; } = 60;
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;
}
