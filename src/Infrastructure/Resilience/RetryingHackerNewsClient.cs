using Microsoft.Extensions.Logging;
using TechTest.Infrastructure.HackerNews;
using TechTest.Shared.Configuration;

namespace TechTest.Infrastructure.Resilience;

/// <summary>
/// Decorator that wraps <see cref="IHackerNewsClient"/> with exponential-backoff retry logic
/// for transient failures. Permanent failures (HTTP 4xx, caller cancellation) are never retried.
/// </summary>
internal sealed class RetryingHackerNewsClient : IHackerNewsClient
{
    private readonly IHackerNewsClient _inner;
    private readonly RetryOptions _retryOptions;
    private readonly ILogger<RetryingHackerNewsClient> _logger;

    public RetryingHackerNewsClient(
        IHackerNewsClient inner,
        RetryOptions retryOptions,
        ILogger<RetryingHackerNewsClient> logger)
    {
        _inner = inner;
        _retryOptions = retryOptions;
        _logger = logger;
    }

    public Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default)
        => ExecuteWithRetryAsync(() => _inner.GetBestStoryIdsAsync(cancellationToken), cancellationToken);

    public Task<HackerNewsStory?> GetStoryAsync(int id, CancellationToken cancellationToken = default)
        => ExecuteWithRetryAsync(() => _inner.GetStoryAsync(id, cancellationToken), cancellationToken);

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                if (attempt >= _retryOptions.MaxRetryCount)
                {
                    _logger.LogWarning(ex,
                        "Hacker News request failed after {MaxRetryCount} attempt(s); giving up.",
                        _retryOptions.MaxRetryCount);
                    throw;
                }

                var delayMs = (int)(_retryOptions.BaseDelayMs * Math.Pow(2, attempt));
                _logger.LogWarning(ex,
                    "Transient Hacker News failure on attempt {Attempt}/{MaxRetryCount}; retrying in {DelayMs}ms.",
                    attempt + 1, _retryOptions.MaxRetryCount, delayMs);

                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Returns true when the failure is worth retrying.
    /// Network errors and HTTP 5xx are transient.
    /// HTTP 4xx and caller-initiated cancellation are permanent.
    /// </summary>
    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        // Timeout (HttpClient.Timeout exceeded) — TaskCanceledException with caller token not triggered.
        // Caller-initiated cancellation — not transient; propagate immediately.
        if (exception is OperationCanceledException)
            return !cancellationToken.IsCancellationRequested;

        if (exception is HttpRequestException httpEx)
        {
            // No status code = network-level failure (DNS, refused connection, etc.) — transient.
            // 5xx = server error — transient.
            // 4xx = client/permanent error — not retried.
            return httpEx.StatusCode is null
                || (int)httpEx.StatusCode >= 500;
        }

        return false;
    }
}
