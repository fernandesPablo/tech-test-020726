namespace TechTest.Shared.Abstractions;

/// <summary>
/// Application-level distributed lock backed by Redis.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Attempts to acquire an exclusive lock on <paramref name="resource"/>.
    /// </summary>
    /// <returns>
    /// A handle that releases the lock when disposed, or <c>null</c> if the lock is currently held by another instance.
    /// </returns>
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);
}
