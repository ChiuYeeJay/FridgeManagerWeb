using FridgeManager.Data.Enums;
using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class ExpiryRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 3);

    [Fact]
    public void Of_Yesterday_IsExpired()
        => Assert.Equal(ExpiryState.Expired, ExpiryRules.Of(Today.AddDays(-1), Today));

    [Fact]
    public void Of_TodayPlusTwo_IsExpiringSoon()
        => Assert.Equal(ExpiryState.ExpiringSoon, ExpiryRules.Of(Today.AddDays(2), Today));

    [Fact]
    public void Of_TodayPlusTen_IsNormal()
        => Assert.Equal(ExpiryState.Normal, ExpiryRules.Of(Today.AddDays(10), Today));
}
