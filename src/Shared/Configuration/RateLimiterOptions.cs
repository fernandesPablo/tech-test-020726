namespace TechTest.Shared.Configuration;

public sealed class RateLimiterOptions
{
    public const string SectionName = "RateLimiter";

    /// <summary>Maximum number of requests allowed per window per IP address.</summary>
    public int PermitLimit { get; init; } = 30;

    /// <summary>Duration of the fixed window in seconds.</summary>
    public int WindowSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum number of requests that may queue when the limit is reached.
    /// Zero means requests are rejected immediately.
    /// </summary>
    public int QueueLimit { get; init; } = 0;
}
