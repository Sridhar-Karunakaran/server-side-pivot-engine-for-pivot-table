using Microsoft.Extensions.Logging;

namespace PivotController.Core.Retry;

/// <summary>
/// Retries transient failures (IOException, TimeoutException) with exponential backoff
/// (250ms * 2^attempt + ±20% jitter), bounded by attempts. Validation errors (InvalidDataException,
/// InvalidOperationException) fail fast and are never retried.
/// </summary>
public static class RetryPolicy
{
    public static async Task<T> ExecuteAsync<T>(
        int attempts,
        Func<CancellationToken, Task<T>> operation,
        ILogger logger,
        string context,
        CancellationToken cancellationToken = default)
    {
        attempts = Math.Clamp(attempts, 0, 30);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or TimeoutException)
            {
                if (attempt >= attempts)
                {
                    throw;
                }
                double baseDelay = 250d * Math.Pow(2, attempt);
                double jitter = (Random.Shared.NextDouble() - 0.5) * 0.4; // ±20%
                int delay = Math.Max(1, (int)(baseDelay * (1 + jitter)));
                logger.LogWarning(exception, "Transient failure in {Context} (attempt {Attempt}/{Max}); retrying in {Delay}ms.",
                    context, attempt + 1, attempts + 1, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
