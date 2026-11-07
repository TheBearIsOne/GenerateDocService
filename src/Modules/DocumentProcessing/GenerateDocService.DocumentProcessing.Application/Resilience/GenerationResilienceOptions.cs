namespace GenerateDocService.DocumentProcessing.Application.Resilience;

public sealed class GenerationResilienceOptions
{
    public const string SectionName = "DocumentProcessing:Resilience";

    public int ProcessingTimeoutSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 3;
    public int InitialDelayMilliseconds { get; set; } = 250;
    public int MaxDelayMilliseconds { get; set; } = 5_000;
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <param name="completedAttempts">Zero-based, so 0 is the delay before the second attempt.</param>
    public TimeSpan GetRetryDelay(int completedAttempts)
    {
        if (completedAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAttempts));
        }

        var multiplier = BackoffMultiplier < 1.0 ? 1.0 : BackoffMultiplier;
        var delay = InitialDelayMilliseconds * Math.Pow(multiplier, completedAttempts);
        var capped = Math.Min(delay, MaxDelayMilliseconds);

        return TimeSpan.FromMilliseconds(Math.Max(0, capped));
    }
}
