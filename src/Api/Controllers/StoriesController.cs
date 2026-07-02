using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TechTest.Api.Features.BestStories;
using TechTest.Api.RateLimiting;
using TechTest.Shared.Configuration;

namespace TechTest.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[EnableRateLimiting(IpRateLimiterPolicy.PolicyName)]
public sealed class StoriesController : ControllerBase
{
    private readonly BestStoriesHandler _bestStoriesHandler;
    private readonly HackerNewsOptions _hackerNewOptions;

    public StoriesController(BestStoriesHandler handler, HackerNewsOptions hnOptions)
    {
        _bestStoriesHandler = handler;
        _hackerNewOptions = hnOptions;
    }

    /// <summary>Returns the top N Hacker News stories ranked by score descending.</summary>
    /// <param name="n">Number of stories to return. Must be between 1 and the configured maximum.</param>
    [HttpGet("best")]
    [ProducesResponseType(typeof(IReadOnlyList<BestStoriesResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBest([FromQuery] int n, CancellationToken cancellationToken)
    {
        if (n <= 0 || n > _hackerNewOptions.MaxN)
        {
            return ValidationProblem(new ValidationProblemDetails
            {
                Errors = { ["n"] = [$"n must be between 1 and {_hackerNewOptions.MaxN}."] }
            });
        }

        var stories = await _bestStoriesHandler.HandleAsync(n, cancellationToken);
        return Ok(stories);
    }
}
