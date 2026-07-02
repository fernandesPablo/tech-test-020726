using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;
using TechTest.Api.Features.BestStories;
using TechTest.Api.HealthChecks;
using TechTest.Api.RateLimiting;
using TechTest.Infrastructure.Cache;
using TechTest.Infrastructure.HackerNews;
using TechTest.Infrastructure.Locking;
using TechTest.Infrastructure.Resilience;
using TechTest.Shared.Abstractions;
using TechTest.Shared.Configuration;
using AppRateLimiterOptions = TechTest.Shared.Configuration.RateLimiterOptions;

namespace TechTest.Api.DependencyInjection;

public static class NativeInjectorBootstrapper
{
    public static IServiceCollection RegisterServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── Configuration objects bound as concrete singletons ─────────────────
        // Consuming classes inject the options type directly; no IOptions<T> wrapper needed.
        services.AddSingleton(configuration.GetRequiredSection(CacheOptions.SectionName).Get<CacheOptions>()!);
        services.AddSingleton(configuration.GetRequiredSection(RedisOptions.SectionName).Get<RedisOptions>()!);
        services.AddSingleton(configuration.GetRequiredSection(HackerNewsOptions.SectionName).Get<HackerNewsOptions>()!);
        services.AddSingleton(configuration.GetRequiredSection(RetryOptions.SectionName).Get<RetryOptions>()!);
        services.AddSingleton(configuration.GetRequiredSection(CircuitBreakerOptions.SectionName).Get<CircuitBreakerOptions>()!);
        services.AddSingleton(configuration.GetRequiredSection(AppRateLimiterOptions.SectionName).Get<AppRateLimiterOptions>()!);

        // ── Framework ─────────────────────────────────────────────────────────
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddProblemDetails();

        // ── Caching ───────────────────────────────────────────────────────────
        services.AddMemoryCache();

        // Single Redis connection shared by IDistributedCache and IDistributedLock.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
            ConnectionMultiplexer.Connect(sp.GetRequiredService<RedisOptions>().ConnectionString));

        // Redis-backed IDistributedCache, reusing the shared multiplexer.
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>()
            .Configure<IConnectionMultiplexer>((opts, multiplexer) =>
                opts.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer));

        // ── Hacker News HTTP client ────────────────────────────────────────────
        services.AddHttpClient<HackerNewsClient>((sp, client) =>
        {
            var hnOptions = sp.GetRequiredService<HackerNewsOptions>();
            client.BaseAddress = new Uri(hnOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(hnOptions.TimeoutSeconds);
        });

        // ── Resilience ────────────────────────────────────────────────────────
        // Singleton so circuit-breaker state is shared across all requests in the process.
        services.AddSingleton<CircuitBreaker>(sp =>
        {
            var opts = sp.GetRequiredService<CircuitBreakerOptions>();
            return new CircuitBreaker(
                opts.FailureThreshold,
                TimeSpan.FromSeconds(opts.CoolDownSeconds),
                sp.GetRequiredService<ILogger<CircuitBreaker>>());
        });

        // IHackerNewsClient resolves to the full decorator chain:
        //   RetryingHackerNewsClient → CircuitBreakerHackerNewsClient → HackerNewsClient
        //
        // Retry is outermost: it handles transient blips by retrying the inner call.
        // CircuitBreaker is next: it sees every individual attempt, so it opens much faster
        // during a real outage and generates far less traffic to a failing downstream.
        // When the circuit is open, CircuitBreakerOpenException is thrown and
        // RetryingHackerNewsClient classifies it as non-retryable, stopping immediately.
        services.AddTransient<IHackerNewsClient>(sp =>
            new RetryingHackerNewsClient(
                new CircuitBreakerHackerNewsClient(
                    sp.GetRequiredService<HackerNewsClient>(),
                    sp.GetRequiredService<CircuitBreaker>(),
                    sp.GetRequiredService<ILogger<CircuitBreakerHackerNewsClient>>()),
                sp.GetRequiredService<RetryOptions>(),
                sp.GetRequiredService<ILogger<RetryingHackerNewsClient>>()));

        // ── Infrastructure services ───────────────────────────────────────────
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddScoped<IStoryCacheService, StoryCacheService>();

        // ── Feature handlers ──────────────────────────────────────────────────
        services.AddScoped<BestStoriesHandler>();

        // ── Rate limiting ─────────────────────────────────────────────────────
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy<string, IpRateLimiterPolicy>(IpRateLimiterPolicy.PolicyName);
        });

        // ── Health checks ─────────────────────────────────────────────────────
        services.AddApplicationHealthChecks();

        return services;
    }
}
