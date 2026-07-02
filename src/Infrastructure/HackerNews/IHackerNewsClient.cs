namespace TechTest.Infrastructure.HackerNews;

public interface IHackerNewsClient
{
    /// <summary>Returns the ordered list of best story IDs from the Hacker News API.</summary>
    Task<IReadOnlyList<int>> GetBestStoryIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the story with the given ID, or <c>null</c> if the item does not exist.
    /// </summary>
    Task<HackerNewsStory?> GetStoryAsync(int id, CancellationToken cancellationToken = default);
}
