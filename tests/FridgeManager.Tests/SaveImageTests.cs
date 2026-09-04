using FridgeManager.Services;
using FridgeManager.Services.Models;

namespace FridgeManager.Tests;

public sealed class SaveImageTests
{
    [Fact]
    public async Task SaveImageAsync_Jpeg_WritesGuidFilenameAndReturnsRelativePath()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0x00], name: "from-client.JPEG");

        var result = await host.Inventory.SaveImageAsync(file);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.StartsWith("/uploads/", result.Value);
        Assert.EndsWith(".jpg", result.Value);
        Assert.DoesNotContain("from-client", result.Value, StringComparison.OrdinalIgnoreCase);

        var physical = Path.Combine(host.Env.WebRootPath, result.Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physical));
        Assert.Equal(4, new FileInfo(physical).Length);
    }

    [Fact]
    public async Task SaveImageAsync_UnsupportedType_Fails()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/gif", [0x47, 0x49, 0x46]);

        var result = await host.Inventory.SaveImageAsync(file);

        Assert.False(result.Success);
        Assert.Contains("JPG", result.Error);
        Assert.Null(result.Value);
        Assert.False(Directory.Exists(Path.Combine(host.Env.WebRootPath, "uploads"))
            && Directory.EnumerateFiles(Path.Combine(host.Env.WebRootPath, "uploads")).Any());
    }

    [Fact]
    public async Task SaveImageAsync_OverFiveMegabytes_Fails()
    {
        using var host = new ServiceHost();
        var oversized = new byte[InventoryService.MaxImageBytes + 1];
        var file = new FakeBrowserFile("image/png", oversized);

        var result = await host.Inventory.SaveImageAsync(file);

        Assert.False(result.Success);
        Assert.Contains("5 MB", result.Error);
        Assert.Null(result.Value);
    }

    private sealed class ServiceHost : IDisposable
    {
        public SqliteDbFactory Factory { get; } = new();
        public FakeWebHostEnvironment Env { get; } = new();
        public InventoryService Inventory { get; }

        public ServiceHost()
        {
            TestData.Seed(Factory);
            Inventory = new InventoryService(Factory, Env);
        }

        public void Dispose()
        {
            Factory.Dispose();
            Env.Dispose();
        }
    }
}
