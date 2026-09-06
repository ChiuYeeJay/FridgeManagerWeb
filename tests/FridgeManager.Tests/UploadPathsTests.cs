using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class UploadPathsTests
{
    [Fact]
    public void IsSafeStorageKey_GeneratedKey_IsAccepted()
        => Assert.True(UploadPaths.IsSafeStorageKey("food-images/2026/09/c56f25dfe4d544e0bd1c8fd72ba4d249.webp"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://evil.com/x.webp")]
    [InlineData("/images/categories/drink.webp")]
    [InlineData("/uploads/c56f25dfe4d544e0bd1c8fd72ba4d249.jpg")]
    [InlineData("/uploads/../appsettings.json")]
    [InlineData("food-images/2026/09/not-a-guid.webp")]
    [InlineData("food-images/2026/09/C56F25DFE4D544E0BD1C8FD72BA4D249.webp")]
    [InlineData("food-images/2026/09/c56f25dfe4d544e0bd1c8fd72ba4d249.png")]
    [InlineData("food-images/../09/c56f25dfe4d544e0bd1c8fd72ba4d249.webp")]
    [InlineData("other-images/2026/09/c56f25dfe4d544e0bd1c8fd72ba4d249.webp")]
    public void IsSafeStorageKey_UnsafeValues_AreRejected(string? key)
        => Assert.False(UploadPaths.IsSafeStorageKey(key));

    [Fact]
    public void NewStorageKey_IsServerGeneratedAndSafe()
    {
        var key = UploadPaths.NewStorageKey(new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));

        Assert.StartsWith("food-images/2026/09/", key);
        Assert.True(UploadPaths.IsSafeStorageKey(key));
        Assert.DoesNotContain("photo", key, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasMatchingMagic_JpegHeader_MatchesJpg()
        => Assert.True(UploadPaths.HasMatchingMagic([0xFF, 0xD8, 0xFF, 0x00], ".jpg"));

    [Fact]
    public void HasMatchingMagic_HtmlBytesClaimedAsJpeg_Fails()
        => Assert.False(UploadPaths.HasMatchingMagic("<html>"u8, ".jpg"));

    [Fact]
    public void ImageUrl_NullPath_FallsBackToCategoryPlate()
    {
        var item = new FridgeManager.Data.Entities.FoodItem
        {
            Category = FridgeManager.Data.Enums.FoodCategory.Snack,
            ImagePath = null
        };

        Assert.Equal("/images/categories/snack.webp", FoodDisplay.ImageUrl(item, new FakeImageStorage()));
    }

    [Fact]
    public void ImageUrl_UnsafeAndLegacyPath_FallsBackToCategoryPlate()
    {
        var storage = new FakeImageStorage();
        var unsafeItem = new FridgeManager.Data.Entities.FoodItem
        {
            Category = FridgeManager.Data.Enums.FoodCategory.Drink,
            ImagePath = "javascript:alert(1)"
        };
        var legacy = new FridgeManager.Data.Entities.FoodItem
        {
            Category = FridgeManager.Data.Enums.FoodCategory.Drink,
            ImagePath = "/uploads/c56f25dfe4d544e0bd1c8fd72ba4d249.jpg"
        };

        Assert.Equal("/images/categories/drink.webp", FoodDisplay.ImageUrl(unsafeItem, storage));
        Assert.Equal("/images/categories/drink.webp", FoodDisplay.ImageUrl(legacy, storage));
    }

    [Fact]
    public void ImageUrl_SafeKey_UsesStorageUrl()
    {
        var key = "food-images/2026/09/c56f25dfe4d544e0bd1c8fd72ba4d249.webp";
        var item = new FridgeManager.Data.Entities.FoodItem
        {
            Category = FridgeManager.Data.Enums.FoodCategory.Meal,
            ImagePath = key
        };

        Assert.Equal($"/uploads/{key}", FoodDisplay.ImageUrl(item, new FakeImageStorage()));
    }
}
