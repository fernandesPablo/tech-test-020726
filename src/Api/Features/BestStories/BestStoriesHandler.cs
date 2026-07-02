using TechTest.Infrastructure.Cache;
using TechTest.Shared.Configuration;
using TechTest.Infrastructure.HackerNews;

namespace TechTest.Api.Features.BestStories;

public sealed class BestStoriesHandler(IStoryCacheService cacheService, HackerNewsOptions hnOptions)
{
    private readonly IStoryCacheService _storyCacheService = cacheService;
    private readonly HackerNewsOptions _hnOptions = hnOptions;

    public async Task<List<BestStoriesResponse>> HandleAsync(int n, CancellationToken cancellationToken)
    {
        var ids = await _storyCacheService.GetBestStoryIdsAsync(cancellationToken);

        // Fan out story-detail fetches concurrently.
        // SemaphoreSlim caps simultaneous in-flight requests to MaxConcurrentRequests,
        // preventing the handler from overwhelming the Hacker News API or the cache layer.
        using var semaphore = new SemaphoreSlim(_hnOptions.MaxConcurrentRequests, _hnOptions.MaxConcurrentRequests);

        var tasks = ids.Select(async id =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await _storyCacheService.GetStoryAsync(id, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var stories = await Task.WhenAll(tasks);

        return [.. stories
            .Where(s => s is not null)
            .OrderByDescending(s => s!.Score)
            .Take(n)
            .Select(MapToResponse)];
    }

    private static BestStoriesResponse MapToResponse(HackerNewsStory? story) =>
        new(
            story!.Title,
            story.Url,
            story.By,
            DateTimeOffset.FromUnixTimeSeconds(story.Time),
            story.Score,
            story.Descendants);
}
