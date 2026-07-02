using Microsoft.Extensions.Diagnostics.HealthChecks;
using TechTest.Infrastructure.HackerNews;

namespace TechTest.Api.HealthChecks;

internal sealed class HackerNewsHealthCheck : IHealthCheck
{
    private readonly IHackerNewsClient _client;
    private readonly ILogger<HackerNewsHealthCheck> _logger;

    public HackerNewsHealthCheck(IHackerNewsClient client, ILogger<HackerNewsHealthCheck> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetBestStoryIdsAsync(cancellationToken);
            return HealthCheckResult.Healthy("Hacker News API is reachable.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hacker News API health check failed.");
            return HealthCheckResult.Unhealthy("Hacker News API is unreachable.", ex);
        }
    }
}
