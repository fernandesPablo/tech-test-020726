using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using AppRateLimiterOptions = TechTest.Shared.Configuration.RateLimiterOptions;

namespace TechTest.Api.RateLimiting;

/// <summary>
/// Fixed-window rate limiting policy partitioned by client IP address.
/// Each unique IP gets its own independent permit bucket.
/// </summary>
internal sealed class IpRateLimiterPolicy(AppRateLimiterOptions options) : IRateLimiterPolicy<string>
{
    public const string PolicyName = "ip-fixed-window";

    private readonly AppRateLimiterOptions _options = options;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = _options.PermitLimit,
            Window = TimeSpan.FromSeconds(_options.WindowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = _options.QueueLimit
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;
}
