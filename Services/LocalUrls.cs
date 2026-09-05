namespace FridgeManager.Services;

public static class LocalUrls
{
    public static bool IsSafe(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return true;
        }

        if (uri.Contains("://", StringComparison.Ordinal)
            || uri.StartsWith("//", StringComparison.Ordinal)
            || uri.StartsWith("/\\", StringComparison.Ordinal)
            || uri.StartsWith('\\')
            || uri.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.IsWellFormedUriString(uri, UriKind.Relative);
    }

    public static string Sanitize(string? uri)
        => IsSafe(uri) ? uri ?? "" : "";
}
