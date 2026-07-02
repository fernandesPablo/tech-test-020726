namespace TechTest.Infrastructure.Cache;

internal static class CacheKeys
{
    internal const string BestStoryIds = "beststories:ids";
    internal const string BestStoryIdsLock = "lock:beststories:ids";

    internal static string Story(int id) => $"story:{id}";
}
