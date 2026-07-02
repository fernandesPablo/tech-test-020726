namespace TechTest.Shared.Configuration;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public TimeSpan StoryIdsTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan StoryDetailsTtl { get; init; } = TimeSpan.FromMinutes(10);
}
