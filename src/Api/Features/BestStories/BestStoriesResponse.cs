namespace TechTest.Api.Features.BestStories;

/// <summary>
/// A single story entry returned by the Best Stories endpoint.
/// ASP.NET Core's default camelCase JSON policy ensures field names match the specification:
/// Title → title, Uri → uri, PostedBy → postedBy, Time → time, Score → score, CommentCount → commentCount.
/// </summary>
public sealed record BestStoriesResponse(
    string Title,
    string? Uri,
    string PostedBy,
    DateTimeOffset Time,
    int Score,
    int CommentCount);
