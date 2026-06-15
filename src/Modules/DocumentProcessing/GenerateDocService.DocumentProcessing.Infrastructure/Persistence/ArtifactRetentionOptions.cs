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
}
