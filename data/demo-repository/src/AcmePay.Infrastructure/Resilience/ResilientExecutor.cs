using Microsoft.Extensions.Logging;

namespace AcmePay.Infrastructure.Resilience;

/// <summary>
/// Minimal retry helper with exponential backoff and jitter.
/// Synthetic demo of production resilience patterns.
/// </summary>
public sealed class ResilientExecutor(ILogger<ResilientExecutor> logger)
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (attempt <= maxRetries && IsTransient(ex))
            {
                var delayMs = 100 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 50);
                logger.LogWarning(ex,
                    "Transient failure (attempt {Attempt}/{Max}); retrying in {Delay}ms",
                    attempt, maxRetries, delayMs);
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is TimeoutException
        || ex is System.Net.Http.HttpRequestException
        || (ex.InnerException is not null && IsTransient(ex.InnerException));
}
