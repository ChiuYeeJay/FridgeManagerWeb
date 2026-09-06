namespace FridgeManager.Services;

public sealed class R2Options
{
    public const string SectionName = "R2";

    public string ServiceUrl { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string BucketName { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";

    public bool IsComplete
        => !string.IsNullOrWhiteSpace(ServiceUrl)
           && !string.IsNullOrWhiteSpace(AccessKeyId)
           && !string.IsNullOrWhiteSpace(SecretAccessKey)
           && !string.IsNullOrWhiteSpace(BucketName)
           && !string.IsNullOrWhiteSpace(PublicBaseUrl);
}
