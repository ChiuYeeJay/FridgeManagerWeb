namespace FridgeManager.Services;

public static class UploadPaths
{
    public const string FolderName = "uploads";
    public const string UrlPrefix = "/uploads/";

    public static bool IsSafeStoredPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(UrlPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = path[UrlPrefix.Length..];
        return IsSafeFileName(fileName);
    }

    public static bool IsSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        if (extension is not (".jpg" or ".png" or ".webp"))
        {
            return false;
        }

        return Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _);
    }

    public static bool HasMatchingMagic(ReadOnlySpan<byte> bytes, string extension)
        => extension switch
        {
            ".jpg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".png" => bytes.Length >= 8
                      && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                      && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
            ".webp" => bytes.Length >= 12
                       && bytes[0] == (byte)'R' && bytes[1] == (byte)'I'
                       && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
                       && bytes[8] == (byte)'W' && bytes[9] == (byte)'E'
                       && bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
            _ => false
        };
}
