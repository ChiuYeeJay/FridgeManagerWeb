namespace FridgeManager.Services;

public sealed class ImageStorageOptions
{
    public const string SectionName = "ImageStorage";

    public string Provider { get; set; } = "Local";
}
