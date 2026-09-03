using System.Globalization;
using FridgeManager.Data.Entities;

namespace FridgeManager.Services;

public static class FoodDisplay
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    public static string SizeLabel(int sizeUnits) => sizeUnits switch
    {
        1 => "Small (1)",
        2 => "Medium (2)",
        3 => "Large (3)",
        _ => $"{sizeUnits} units"
    };

    public static string ImageUrl(FoodItem item)
        => string.IsNullOrEmpty(item.ImagePath)
            ? $"/images/categories/{item.Category.ToString().ToLowerInvariant()}.webp"
            : item.ImagePath;

    public static string OwnerLabel(ApplicationUser owner)
    {
        var name = owner.UserName ?? owner.Email ?? "";
        var at = name.IndexOf('@');
        return at > 0 ? name[..at] : name;
    }

    public static string DateLabel(DateOnly date) => date.ToString("d MMM yyyy", English);

    public static string BadgeLabel(Data.Enums.ExpiryState state) => state switch
    {
        Data.Enums.ExpiryState.ExpiringSoon => "Expiring soon",
        Data.Enums.ExpiryState.Expired => "Expired",
        _ => "Normal"
    };
}
