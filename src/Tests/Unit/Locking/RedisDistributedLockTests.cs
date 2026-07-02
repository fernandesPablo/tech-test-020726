using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using TechTest.Infrastructure.Locking;

namespace TechTest.Tests.Unit.Locking;

public sealed class RedisDistributedLockTests
{
    // In StackExchange.Redis 3.x the production code calls:
    //   db.StringSetAsync(resource, token, expiry, When.NotExists)
    // which resolves to the 4-parameter overload on IDatabaseAsync:
    //   StringSetAsync(RedisKey, RedisValue, TimeSpan?, When)   — no CommandFlags, no keepTtl.
    private static (RedisDistributedLock sut, Mock<IDatabase> db) CreateSut()
    {
        var db = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                   .Returns(db.Object);

        var sut = new RedisDistributedLock(multiplexer.Object, NullLogger<RedisDistributedLock>.Instance);
        return (sut, db);
    }

    private static void SetupStringSet(Mock<IDatabase> db, bool result) =>
        db.Setup(d => d.StringSetAsync(
               It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
               It.IsAny<TimeSpan?>(), It.IsAny<When>()))
          .ReturnsAsync(result);

    [Fact]
    public async Task TryAcquireAsync_WhenLockIsAvailable_ReturnsHandle()
    {
        var (sut, db) = CreateSut();
        SetupStringSet(db, result: true);

        var handle = await sut.TryAcquireAsync("lock:test", TimeSpan.FromSeconds(30));

        Assert.NotNull(handle);
        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenLockIsHeld_ReturnsNull()
    {
        var (sut, db) = CreateSut();
        SetupStringSet(db, result: false);

        var handle = await sut.TryAcquireAsync("lock:test", TimeSpan.FromSeconds(30));

        Assert.Null(handle);
    }

    [Fact]
    public async Task DisposeAsync_ExecutesLuaReleaseScript()
    {
        var (sut, db) = CreateSut();
        SetupStringSet(db, result: true);
        db.Setup(d => d.ScriptEvaluateAsync(
               It.IsAny<string>(), It.IsAny<RedisKey[]?>(),
               It.IsAny<RedisValue[]?>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisResult.Create((RedisValue)1L));

        var handle = await sut.TryAcquireAsync("lock:test", TimeSpan.FromSeconds(30));
        Assert.NotNull(handle);

        await handle!.DisposeAsync();

        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.Is<RedisKey[]?>(k => k != null && k.Length == 1),
            It.Is<RedisValue[]?>(v => v != null && v.Length == 1),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WhenCalledTwice_OnlyReleasesOnce()
    {
        var (sut, db) = CreateSut();
        SetupStringSet(db, result: true);
        db.Setup(d => d.ScriptEvaluateAsync(
               It.IsAny<string>(), It.IsAny<RedisKey[]?>(),
               It.IsAny<RedisValue[]?>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisResult.Create((RedisValue)1L));

        var handle = await sut.TryAcquireAsync("lock:test", TimeSpan.FromSeconds(30));
        Assert.NotNull(handle);

        await handle!.DisposeAsync();
        await handle.DisposeAsync();  // second call must be a no-op

        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(), It.IsAny<RedisKey[]?>(),
            It.IsAny<RedisValue[]?>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_PassesExpiryAndNxFlagToRedis()
    {
        var (sut, db) = CreateSut();
        var expiry = TimeSpan.FromSeconds(45);
        SetupStringSet(db, result: true);

        await sut.TryAcquireAsync("lock:resource", expiry);

        // Verify expiry and NX semantics were used (4-param overload on IDatabaseAsync).
        db.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            expiry, When.NotExists), Times.Once);
    }
}
