using System.Globalization;
using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;

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

    public static string SizeDetail(int sizeUnits) => sizeUnits switch
    {
        1 => "Small · 1 unit",
        2 => "Medium · 2 units",
        3 => "Large · 3 units",
        _ => $"{sizeUnits} units"
    };

    public static string ExpirationDetail(DateOnly date, DateOnly today)
    {
        var label = date.ToString("d MMMM yyyy", English);
        var days = date.DayNumber - today.DayNumber;
        var relative = days switch
        {
            < 0 => $"expired {Math.Abs(days)} day{(Math.Abs(days) == 1 ? "" : "s")} ago",
            0 => "today",
            1 => "tomorrow",
            _ => $"in {days} days"
        };
        return $"{label} · {relative}";
    }

    public static string UtcStamp(DateTime utc)
        => utc.ToString("d MMM yyyy, HH:mm UTC", English);

    public static string CategoryImage(FoodCategory category)
        => $"/images/categories/{category.ToString().ToLowerInvariant()}.webp";

    public static string ImageUrl(FoodItem item, IImageStorage storage)
        => storage.GetPublicUrl(item.ImagePath) ?? CategoryImage(item.Category);

    public static string OwnerLabel(ApplicationUser owner)
    {
        var name = owner.UserName ?? owner.Email ?? "";
        var at = name.IndexOf('@');
        return at > 0 ? name[..at] : name;
    }

    public static string OwnerInitial(ApplicationUser owner)
    {
        var label = OwnerLabel(owner);
        return string.IsNullOrEmpty(label) ? "?" : char.ToUpperInvariant(label[0]).ToString();
    }

    public static bool IsOwn(FoodItem item, string? currentUserId)
        => !string.IsNullOrEmpty(currentUserId) && item.OwnerId == currentUserId;

    public static string DateLabel(DateOnly date) => date.ToString("d MMM yyyy", English);

    public static string BadgeLabel(Data.Enums.ExpiryState state) => state switch
    {
        Data.Enums.ExpiryState.ExpiringSoon => "Expiring soon",
        Data.Enums.ExpiryState.Expired => "Expired",
        _ => "Normal"
    };
}
