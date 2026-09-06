using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class UserClockTests
{
    [Fact]
    public void Resolve_PrefersSavedTimeZone()
    {
        var zone = UserClock.Resolve("America/Chicago", "Europe/London", "Pacific/Auckland");

        Assert.Equal("America/Chicago", zone.Id);
    }

    [Fact]
    public void Resolve_UsesBrowserWhenSavedIsMissing()
    {
        var zone = UserClock.Resolve(null, "Europe/London", "America/Chicago");

        Assert.Equal("Europe/London", zone.Id);
    }

    [Fact]
    public void Resolve_UsesBrowserWhenSavedIsInvalid()
    {
        var zone = UserClock.Resolve("Not/AZone", "Europe/London", "America/Chicago");

        Assert.Equal("Europe/London", zone.Id);
    }

    [Fact]
    public void Resolve_UsesFallbackWhenSavedAndBrowserAreMissing()
    {
        var zone = UserClock.Resolve("  ", null, "America/Chicago");

        Assert.Equal("America/Chicago", zone.Id);
    }

    [Fact]
    public void Resolve_InvalidFallback_UsesUtc()
    {
        var zone = UserClock.Resolve("Nope", "also-nope", "still-nope");

        Assert.Equal(TimeZoneInfo.Utc, zone);
    }
}
