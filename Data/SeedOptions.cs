namespace FridgeManager.Data;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? AdminUserName { get; set; }
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public bool DemoData { get; set; }
}
