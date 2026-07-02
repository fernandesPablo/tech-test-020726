namespace TechTest.Shared.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; init; } = "localhost:6379";

    /// <summary>Maximum time a distributed lock can be held before automatic expiry.</summary>
    public TimeSpan LockTtl { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a waiting instance pauses before re-checking Redis after failing to acquire the lock.</summary>
    public TimeSpan LockRetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Maximum number of attempts to acquire the lock before giving up.</summary>
    public int LockRetryCount { get; init; } = 10;
}
