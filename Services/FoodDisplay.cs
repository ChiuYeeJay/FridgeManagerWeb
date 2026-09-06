using System.Globalization;
using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;

namespace FridgeManager.Services;

public static class FoodDisplay
{
    private static readonly CultureInfo English = CreateEnglish();

    private static CultureInfo CreateEnglish()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        string[] abbreviated =
        [
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec", ""
        ];
        culture.DateTimeFormat.AbbreviatedMonthNames = abbreviated;
        culture.DateTimeFormat.AbbreviatedMonthGenitiveNames = abbreviated;
        return culture;
    }

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

    public const string SizeHint =
        "How you’d pick it up: both palms wrap it (1), one hand lifts it (2), both hands (3).";

    public static string RelativeDays(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "today",
            1 => "tomorrow",
            -1 => "yesterday",
            > 1 => $"in {days} days",
            _ => $"{Math.Abs(days)} days ago"
        };
    }

    public static string ExpiryCardLabel(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;
        return days switch
        {
            > 1 => $"Expires in {days} days",
            1 => "Expires tomorrow",
            0 => "Expires today",
            -1 => "Expired yesterday",
            _ => $"Expired {Math.Abs(days)} days ago"
        };
    }

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
        return $"{label} ({relative})";
    }

    public static string Stamp(DateTime utc, TimeZoneInfo zone)
    {
        var utcValue = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcValue, zone);
        var offset = zone.GetUtcOffset(utcValue);
        return $"{local.ToString("d MMM yyyy, HH:mm", English)} ({FormatOffset(offset)})";
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "−" : "+";
        var abs = offset.Duration();
        return abs.Minutes == 0
            ? $"UTC{sign}{abs.Hours}"
            : $"UTC{sign}{abs.Hours}:{abs.Minutes:D2}";
    }

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
