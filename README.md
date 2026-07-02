# TechTest — Hacker News Best Stories API

ASP.NET Core 9 Web API that returns the top **N** Hacker News stories ranked by score.

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Redis (local or Docker)

```bash
docker run -d -p 6379:6379 redis:latest
```

---

## Running the Application

```bash
cd TechTest
dotnet run --project src/Api
```

Swagger UI is available at the root URL (e.g. `http://localhost:5055`) in the `Development` environment. The exact port is set in `src/Api/Properties/launchSettings.json`.

---

## Running the Tests

```bash
cd TechTest
dotnet test
```

57 tests — unit and integration. No external services required; all dependencies are mocked.

---

## API

### `GET /api/v1/stories/best?n={count}`

Handled by `StoriesController` (`[ApiController]`, versioned via route prefix `api/v1`).  
Returns the top `n` stories sorted by score descending.

**Response**

```json
[
  {
    "title": "Story title",
    "uri": "https://...",
    "postedBy": "username",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

`uri` is nullable — stories without a URL (e.g. Ask HN posts) will have `"uri": null`.

**Validation** — `n` must be between `1` and `HackerNews:MaxN` (default 500). Invalid or missing `n` returns `400 Bad Request`.

### Health Checks

| Endpoint | Status codes | Purpose |
|---|---|---|
| `GET /hc/live` | `200` | Liveness — process is responding |
| `GET /hc/ready` | `200` healthy · `503` unhealthy | Readiness — Redis and Hacker News API reachable |

Both endpoints return `application/json` with per-check detail:

```json
{
  "status": "Unhealthy",
  "totalDurationMs": 312.5,
  "checks": [
    {
      "name": "redis",
      "status": "Unhealthy",
      "description": "Redis is unreachable.",
      "durationMs": 310.1,
      "exception": "No connection is available to service this operation"
    },
    {
      "name": "hackernews",
      "status": "Healthy",
      "description": "Hacker News API is reachable.",
      "durationMs": 1.2,
      "exception": null
    }
  ]
}
```

---

## Configuration

All settings are in `appsettings.json`.

| Key | Default | Description |
|---|---|---|
| `HackerNews:BaseUrl` | `https://hacker-news.firebaseio.com` | Hacker News API base URL |
| `HackerNews:TimeoutSeconds` | `30` | HTTP client timeout |
| `HackerNews:MaxConcurrentRequests` | `10` | Concurrent story-detail fetches |
| `HackerNews:MaxN` | `500` | Maximum allowed `n` |
| `Cache:StoryIdsTtl` | `00:05:00` | L1/L2 TTL for story ID list |
| `Cache:StoryDetailsTtl` | `00:10:00` | L1/L2 TTL for individual stories |
| `Redis:ConnectionString` | `localhost:6379` | Redis connection string |
| `Redis:LockTtl` | `00:00:30` | Distributed lock expiry |
| `Redis:LockRetryDelay` | `00:00:00.200` | Pause between lock acquisition retries |
| `Redis:LockRetryCount` | `10` | Lock acquisition retries |
| `Retry:MaxRetryCount` | `3` | HTTP retry attempts |
| `Retry:BaseDelayMs` | `500` | Exponential backoff base delay |
| `CircuitBreaker:FailureThreshold` | `5` | Failures before circuit opens |
| `CircuitBreaker:CoolDownSeconds` | `60` | Open-state duration |
| `RateLimiter:PermitLimit` | `30` | Requests allowed per window per IP |
| `RateLimiter:WindowSeconds` | `60` | Fixed window duration in seconds |
| `RateLimiter:QueueLimit` | `0` | Queued requests when limit is reached (0 = reject immediately) |

---

## Architecture

Vertical Slice layout inside a four-project solution:

```
src/
├── Api/             # Minimal API, middleware, features, health checks
├── Infrastructure/  # HTTP client, caching, locking, resilience
├── Shared/          # Configuration options, shared abstractions
└── Tests/           # Unit and integration tests
```

Features live under `Api/Features/{Feature}/` with their own handler and response DTO. HTTP concerns (routing, validation, status codes) are handled by controllers in `Api/Controllers/`.

---

## Key Engineering Decisions

**Two-level cache (L1 → L2 → origin)**  
In-process `IMemoryCache` is checked first; on a miss, Redis (`IDistributedCache`) is checked and the result is back-populated into L1. Only on a full cache miss is the Hacker News API called. Both caches are written together on a successful fetch. Failed fetches are never cached.

**Distributed lock on cache refresh**  
When the story-ID list is absent from both caches, a Redis `SET NX PX` lock ensures only one instance calls the Hacker News API. Other instances retry the cache lookup while the lock is held, preventing stampedes.

**Manual retry with exponential backoff**  
`RetryingHackerNewsClient` wraps the HTTP client and retries on transient failures (`HttpRequestException`, timeouts) up to `MaxRetryCount` times with configurable base delay. `OperationCanceledException` and `CircuitBreakerOpenException` are not retried.

**Manual circuit breaker**  
`CircuitBreaker` implements the standard Closed → Open → Half-Open state machine. The decorator chain is ordered `Retry → CircuitBreaker → HttpClient` so the circuit breaker sees every individual HTTP attempt, including retries. This means the circuit opens after `FailureThreshold` consecutive individual failures rather than after `FailureThreshold` fully-exhausted retry sequences — faster failure detection and less amplified load on a struggling downstream service. When the circuit is open, `CircuitBreakerHackerNewsClient` throws `CircuitBreakerOpenException`, which `RetryingHackerNewsClient` treats as non-retryable and propagates immediately.

**Bounded concurrency**  
`BestStoriesHandler` fans out story-detail fetches with `Task.WhenAll` and a `SemaphoreSlim` gate, keeping active concurrent HTTP calls at or below `MaxConcurrentRequests`.

**No third-party resilience libraries**  
Retry and circuit breaker are implemented manually per the task requirements, keeping the dependency footprint minimal.

**Centralized dependency injection**  
All service registrations live in a single `NativeInjectorBootstrapper` class; `Program.cs` makes one call (`RegisterServices`). Configuration sections are bound eagerly at startup as plain singletons (`configuration.GetSection("X").Get<T>()`) rather than via `IOptions<T>`. This trades hot-reload support for simpler constructors and fail-fast startup — misconfigured settings crash the app immediately rather than at first use.

**IP-based rate limiting**  
The `/api/v1/best-stories` endpoint is protected by ASP.NET Core's built-in `AddRateLimiter` middleware using a fixed-window policy partitioned by client IP (`RemoteIpAddress`). Limit, window, and queue depth are fully configurable via `RateLimiter` options. Exceeding the limit returns `429 Too Many Requests`. Health check and Swagger endpoints are not subject to rate limiting. Note: when the application runs behind a reverse proxy, `RemoteIpAddress` reflects the proxy's IP; forwarding headers (`X-Forwarded-For`) are not currently read.

---

## Future Improvements

- **Polly** — replace the manual retry and circuit breaker implementations.
- **Background refresh** — proactively refresh caches before expiry to eliminate cold-miss latency.
- **Forwarded-headers support** — read `X-Forwarded-For` so rate limiting works correctly behind a reverse proxy.
- **Observability** — expose metrics (cache hit rate, circuit breaker state, retry counts) via OpenTelemetry.
- **Configurable health check timeouts** — surface per-check timeout configuration.
