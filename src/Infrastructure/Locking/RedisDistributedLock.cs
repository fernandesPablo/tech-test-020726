using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TechTest.Shared.Abstractions;

namespace TechTest.Infrastructure.Locking;

internal sealed class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger<RedisDistributedLock> _logger;

    public RedisDistributedLock(IConnectionMultiplexer connection, ILogger<RedisDistributedLock> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var db = _connection.GetDatabase();
        var token = Guid.NewGuid().ToString("N");

        // SET resource token NX PX <ms> — atomic: sets the key only if it does not already exist.
        var acquired = await db.StringSetAsync(resource, token, expiry, When.NotExists);

        if (acquired)
        {
            _logger.LogDebug("Distributed lock acquired for {LockResource}.", resource);
            return new LockHandle(db, resource, token, _logger);
        }

        _logger.LogDebug("Distributed lock not acquired for {LockResource}; held by another instance.", resource);
        return null;
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private readonly ILogger _logger;
        private bool _disposed;

        public LockHandle(IDatabase db, string key, string token, ILogger logger)
        {
            _db = db;
            _key = key;
            _token = token;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            // Lua script: delete the key only if the stored token matches ours.
            // Prevents releasing a lock that was legitimately re-acquired by another instance
            // after this lock's TTL expired mid-operation.
            const string releaseScript = """
                if redis.call("get", KEYS[1]) == ARGV[1] then
                    return redis.call("del", KEYS[1])
                else
                    return 0
                end
                """;

            await _db.ScriptEvaluateAsync(
                releaseScript,
                keys: [new RedisKey(_key)],
                values: [new RedisValue(_token)]);

            _logger.LogDebug("Distributed lock released for {LockResource}.", _key);
        }
    }
}
