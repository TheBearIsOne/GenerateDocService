namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence;

public sealed class ArtifactRetentionOptions
{
    public const string SectionName = "DocumentProcessing:Retention";

    /// <summary>
    /// Number of days to retain completed task artifacts. Default: 30.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Number of days to retain failed task records. Default: 7.
    /// </summary>
    public int FailedTaskRetentionDays { get; set; } = 7;

    /// <summary>
    /// How often the cleanup job runs. Default: 1 hour.
    /// </summary>
    public int CleanupIntervalHours { get; set; } = 1;

    /// <summary>
    /// Seconds after which a task still marked Processing is treated as abandoned by a crashed
    /// worker and moved to Failed. Keep this well above
    /// <c>DocumentProcessing:Resilience:ProcessingTimeoutSeconds</c> so a slow-but-alive generation
    /// is never reaped. Default: 1800. Zero disables the reaper.
    /// </summary>
    public int StuckProcessingTaskSeconds { get; set; } = 1_800;
}
