using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class FoodDisplayTests
{
    private static readonly DateOnly Today = new(2026, 9, 6);

    [Theory]
    [InlineData(2026, 9, 6, "today")]
    [InlineData(2026, 9, 7, "tomorrow")]
    [InlineData(2026, 9, 5, "yesterday")]
    [InlineData(2026, 9, 9, "in 3 days")]
    [InlineData(2026, 9, 1, "5 days ago")]
    public void RelativeDays_UsesCalendarOffset(int year, int month, int day, string expected)
        => Assert.Equal(expected, FoodDisplay.RelativeDays(new DateOnly(year, month, day), Today));

    [Theory]
    [InlineData(2026, 9, 9, "Expires in 3 days")]
    [InlineData(2026, 9, 7, "Expires tomorrow")]
    [InlineData(2026, 9, 6, "Expires today")]
    [InlineData(2026, 9, 5, "Expired yesterday")]
    [InlineData(2026, 9, 1, "Expired 5 days ago")]
    public void ExpiryCardLabel_PrefixesExpiresOrExpired(int year, int month, int day, string expected)
        => Assert.Equal(expected, FoodDisplay.ExpiryCardLabel(new DateOnly(year, month, day), Today));

    [Theory]
    [InlineData(2026, 9, 9, "9 September 2026 (in 3 days)")]
    [InlineData(2026, 9, 7, "7 September 2026 (tomorrow)")]
    [InlineData(2026, 9, 6, "6 September 2026 (today)")]
    [InlineData(2026, 9, 5, "5 September 2026 (expired 1 day ago)")]
    [InlineData(2026, 9, 1, "1 September 2026 (expired 5 days ago)")]
    public void ExpirationDetail_DateThenParentheticalDays(int year, int month, int day, string expected)
        => Assert.Equal(expected, FoodDisplay.ExpirationDetail(new DateOnly(year, month, day), Today));

    [Fact]
    public void SizeHint_DescribesGrabTestWithoutExamples()
        => Assert.Equal(
            "How you’d pick it up: both palms wrap it (1), one hand lifts it (2), both hands (3).",
            FoodDisplay.SizeHint);

    [Fact]
    public void Stamp_ConvertsUtcToAmericaChicagoInDaylightTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var utc = new DateTime(2026, 9, 6, 19, 32, 0, DateTimeKind.Utc);

        Assert.Equal("6 Sep 2026, 14:32 (UTC−5)", FoodDisplay.Stamp(utc, zone));
    }

    [Fact]
    public void Stamp_ConvertsUtcToAmericaChicagoInStandardTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var utc = new DateTime(2026, 1, 15, 20, 32, 0, DateTimeKind.Utc);

        Assert.Equal("15 Jan 2026, 14:32 (UTC−6)", FoodDisplay.Stamp(utc, zone));
    }
}
