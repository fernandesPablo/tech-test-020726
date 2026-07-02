namespace TechTest.Shared.Configuration;

public sealed class RetryOptions
{
    public const string SectionName = "Retry";

    /// <summary>Maximum number of retry attempts for transient failures.</summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>Base delay in milliseconds for the first retry. Subsequent retries use exponential backoff.</summary>
    public int BaseDelayMs { get; init; } = 500;
}
