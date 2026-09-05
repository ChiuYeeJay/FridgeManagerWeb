using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class LocalUrlsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("food")]
    [InlineData("food/1")]
    [InlineData("/food")]
    [InlineData("Account/Login")]
    public void IsSafe_LocalPaths_AreAccepted(string? uri)
        => Assert.True(LocalUrls.IsSafe(uri));

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com/phish")]
    [InlineData("javascript:alert(1)")]
    public void IsSafe_OffSiteOrSchemedUrls_AreRejected(string uri)
        => Assert.False(LocalUrls.IsSafe(uri));

    [Fact]
    public void Sanitize_ReplacesUnsafeUrlsWithEmpty()
        => Assert.Equal("", LocalUrls.Sanitize("//evil.com"));
}
