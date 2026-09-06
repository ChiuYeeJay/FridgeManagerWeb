using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class NpgsqlConnectionStringsTests
{
    [Fact]
    public void FromDatabaseUrl_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(NpgsqlConnectionStrings.FromDatabaseUrl(null));
        Assert.Null(NpgsqlConnectionStrings.FromDatabaseUrl(""));
        Assert.Null(NpgsqlConnectionStrings.FromDatabaseUrl("   "));
    }

    [Fact]
    public void FromDatabaseUrl_PostgresUrl_AddsSslRequire()
    {
        var cs = NpgsqlConnectionStrings.FromDatabaseUrl(
            "postgresql://alice:p%40ss@db.example:5432/fridge");

        Assert.Equal(
            "Host=db.example;Port=5432;Database=fridge;Username=alice;Password=p@ss;SSL Mode=Require;Trust Server Certificate=true",
            cs);
    }

    [Fact]
    public void FromDatabaseUrl_DefaultPort_Uses5432()
    {
        var cs = NpgsqlConnectionStrings.FromDatabaseUrl("postgres://u:p@localhost/fridge");

        Assert.Contains("Port=5432", cs);
        Assert.Contains("Host=localhost", cs);
    }

    [Fact]
    public void FromDatabaseUrl_NonPostgres_Throws()
        => Assert.Throws<InvalidOperationException>(
            () => NpgsqlConnectionStrings.FromDatabaseUrl("https://example.com/fridge"));
}
