using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class UploadPathsTests
{
    [Fact]
    public void IsSafeStoredPath_GuidJpeg_IsAccepted()
        => Assert.True(UploadPaths.IsSafeStoredPath("/uploads/c56f25dfe4d544e0bd1c8fd72ba4d249.jpg"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://evil.com/x.jpg")]
    [InlineData("/images/categories/drink.webp")]
    [InlineData("/uploads/../appsettings.json")]
    [InlineData("/uploads/not-a-guid.jpg")]
    [InlineData("/uploads/c56f25dfe4d544e0bd1c8fd72ba4d249.gif")]
    public void IsSafeStoredPath_UnsafeValues_AreRejected(string? path)
        => Assert.False(UploadPaths.IsSafeStoredPath(path));

    [Fact]
    public void HasMatchingMagic_JpegHeader_MatchesJpg()
        => Assert.True(UploadPaths.HasMatchingMagic([0xFF, 0xD8, 0xFF, 0x00], ".jpg"));

    [Fact]
    public void HasMatchingMagic_HtmlBytesClaimedAsJpeg_Fails()
        => Assert.False(UploadPaths.HasMatchingMagic("<html>"u8, ".jpg"));

    [Fact]
    public void ImageUrl_UnsafePath_FallsBackToCategoryPlate()
    {
        var item = new FridgeManager.Data.Entities.FoodItem
        {
            Category = FridgeManager.Data.Enums.FoodCategory.Drink,
            ImagePath = "javascript:alert(1)"
        };

        Assert.Equal("/images/categories/drink.webp", FoodDisplay.ImageUrl(item));
    }
}
