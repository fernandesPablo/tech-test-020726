using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TechTest.Api.HealthChecks;

public static class HealthCheckExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IServiceCollection AddApplicationHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
            .AddCheck<HackerNewsHealthCheck>("hackernews", tags: ["ready"]);

        return services;
    }

    public static IEndpointRouteBuilder MapApplicationHealthCheckEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: verifies the process is alive and able to respond to HTTP requests.
        // No dependency checks are included — the fact that this endpoint responds is sufficient.
        app.MapHealthChecks("/hc/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteJsonResponseAsync
        });

        // Readiness: verifies all required external dependencies are operational.
        // The application is only considered ready when both Redis and the Hacker News API are reachable.
        app.MapHealthChecks("/hc/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteJsonResponseAsync
        });

        return app;
    }

    private static Task WriteJsonResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = e.Value.Duration.TotalMilliseconds,
                exception = e.Value.Exception?.Message
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
