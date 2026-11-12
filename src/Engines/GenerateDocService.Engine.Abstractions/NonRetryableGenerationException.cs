namespace GenerateDocService.Engine.Abstractions;

/// <summary>
/// Thrown by an engine whose failure must not be retried, because a retry could repeat a side effect
/// on a system this service does not control. The background processor classifies this as terminal
/// and fails the task on the first attempt.
/// </summary>
/// <remarks>
/// Engines that call a known-idempotent upstream, such as the FOP converter, should let ordinary
/// transient exceptions propagate so they do get retried.
/// </remarks>
public class NonRetryableGenerationException : Exception
{
    public NonRetryableGenerationException(string message)
        : base(message)
    {
    }

    public NonRetryableGenerationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
