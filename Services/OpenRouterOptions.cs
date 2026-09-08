namespace FridgeManager.Services;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";
    public const string DefaultBaseUrl = "https://openrouter.ai/api/v1";
    public const string DefaultModel = "openai/gpt-4o-mini";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = DefaultModel;
    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public int TimeoutSeconds { get; set; } = 45;
    public int MaxRequestsPerUserPerHour { get; set; } = 20;
    public string HttpReferer { get; set; } = "";
    public string AppTitle { get; set; } = "Fridge Manager";
}
