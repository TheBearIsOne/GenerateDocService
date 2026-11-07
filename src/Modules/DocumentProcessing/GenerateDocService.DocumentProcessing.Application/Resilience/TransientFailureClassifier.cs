using System.Net.Sockets;

namespace GenerateDocService.DocumentProcessing.Application.Resilience;

/// <summary>
/// Decides whether a generation failure is worth retrying. Infrastructure adds provider-specific
/// exception types so the application layer keeps no reference to Redis, MinIO, or MassTransit.
/// </summary>
public sealed class TransientFailureClassifier
{
    private readonly HashSet<Type> _transientTypes = new()
    {
        typeof(HttpRequestException),
        typeof(TimeoutException),
        typeof(IOException),
        typeof(SocketException)
    };

    public TransientFailureClassifier(params Type[] additionalTransientTypes)
    {
        foreach (var type in additionalTransientTypes ?? [])
        {
            ArgumentNullException.ThrowIfNull(type);

            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new ArgumentException($"Type '{type}' is not an exception.", nameof(additionalTransientTypes));
            }

            _transientTypes.Add(type);
        }
    }

    public bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            // Cancellation outranks any transient inner fault: a caller abort or an expired
            // processing deadline must not be retried into another full attempt.
            if (current is OperationCanceledException)
            {
                return false;
            }

            foreach (var transientType in _transientTypes)
            {
                if (transientType.IsInstanceOfType(current))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
