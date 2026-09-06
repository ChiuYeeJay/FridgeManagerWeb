namespace FridgeManager.Services;

public sealed class AppOptions
{
    public const string SectionName = "App";
    public const string FallbackTimeZoneId = "America/Chicago";

    public string DefaultTimeZone { get; set; } = FallbackTimeZoneId;
}
