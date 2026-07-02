using System.Text.Json.Serialization;

namespace TechTest.Infrastructure.HackerNews;

/// <summary>
/// Represents the raw item payload returned by the Hacker News API.
/// Field names match the HN JSON property names exactly.
/// </summary>
public sealed class HackerNewsStory
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>The story URL. May be absent for Ask HN posts.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("by")]
    public string By { get; init; } = string.Empty;

    /// <summary>Unix timestamp (seconds since epoch).</summary>
    [JsonPropertyName("time")]
    public long Time { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    /// <summary>Total comment count (called <c>descendants</c> in the HN API).</summary>
    [JsonPropertyName("descendants")]
    public int Descendants { get; init; }
}
