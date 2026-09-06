namespace FridgeManager.Services;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public const string HttpClientName = "Gemini";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxRequestsPerUserPerHour { get; set; } = 20;
}
