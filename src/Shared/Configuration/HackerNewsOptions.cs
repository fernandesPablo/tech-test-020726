namespace TechTest.Shared.Configuration;

public sealed class HackerNewsOptions
{
    public const string SectionName = "HackerNews";

    public string BaseUrl { get; init; } = "https://hacker-news.firebaseio.com";

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>Maximum number of simultaneous story-detail requests to the Hacker News API.</summary>
    public int MaxConcurrentRequests { get; init; } = 10;

    /// <summary>Maximum value of <c>n</c> accepted by the endpoint.</summary>
    public int MaxN { get; init; } = 500;
}
