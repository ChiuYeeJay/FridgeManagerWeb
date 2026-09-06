using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class AiRateLimiterTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset Start = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_AllowsTwentyThenRejectsTheTwentyFirst()
    {
        var limiter = new AiRateLimiter(new TestTimeProvider(Start));

        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.TryAcquire("alice", 20, Hour));
        }

        Assert.False(limiter.TryAcquire("alice", 20, Hour));
    }

    [Fact]
    public void TryAcquire_IsolatesUsers()
    {
        var limiter = new AiRateLimiter(new TestTimeProvider(Start));

        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.TryAcquire("alice", 20, Hour));
        }

        Assert.False(limiter.TryAcquire("alice", 20, Hour));
        Assert.True(limiter.TryAcquire("bob", 20, Hour));
    }

    [Fact]
    public void TryAcquire_WindowSlidesWithClock()
    {
        var clock = new TestTimeProvider(Start);
        var limiter = new AiRateLimiter(clock);

        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.TryAcquire("alice", 20, Hour));
        }

        Assert.False(limiter.TryAcquire("alice", 20, Hour));

        clock.Advance(Hour);
        Assert.True(limiter.TryAcquire("alice", 20, Hour));
    }
}
