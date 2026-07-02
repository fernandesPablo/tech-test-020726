using TechTest.Infrastructure.HackerNews;

namespace TechTest.Infrastructure.Cache;

public interface IStoryCacheService
{
    /// <summary>
    /// Returns the ordered list of best story IDs.
    /// Served from the two-level cache; fetches from Hacker News on a full cache miss.
    /// </summary>
    Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the story with the given ID.
    /// Served from the two-level cache; fetches from Hacker News on a full cache miss.
    /// Returns <c>null</c> if the story does not exist.
    /// </summary>
    Task<HackerNewsStory?> GetStoryAsync(int id, CancellationToken cancellationToken = default);
}
