namespace FridgeManager.Services;

public static class NpgsqlConnectionStrings
{
    public static string? FromDatabaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            throw new InvalidOperationException("DATABASE_URL must be a postgresql:// connection URL.");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        var port = uri.IsDefaultPort ? 5432 : uri.Port;

        return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }
}
