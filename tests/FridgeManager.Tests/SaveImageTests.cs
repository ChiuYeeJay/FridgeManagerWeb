using FridgeManager.Services;

namespace FridgeManager.Tests;

public sealed class SaveImageTests
{
    [Fact]
    public async Task SaveImageAsync_Jpeg_WritesGuidFilenameAndReturnsRelativePath()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0x00], name: "from-client.JPEG");

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(UploadPaths.IsSafeStoredPath(result.Value));
        Assert.EndsWith(".jpg", result.Value);
        Assert.DoesNotContain("from-client", result.Value, StringComparison.OrdinalIgnoreCase);

        var physical = Path.Combine(host.Env.WebRootPath, result.Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physical));
        Assert.Equal(4, new FileInfo(physical).Length);
    }

    [Fact]
    public async Task SaveImageAsync_Png_WritesGuidFilename()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(UploadPaths.IsSafeStoredPath(result.Value));
        Assert.EndsWith(".png", result.Value);
        Assert.True(File.Exists(Path.Combine(
            host.Env.WebRootPath,
            result.Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task SaveImageAsync_Webp_WritesGuidFilename()
    {
        using var host = new ServiceHost();
        byte[] bytes =
        [
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0, 0, 0, 0,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P'
        ];
        var file = new FakeBrowserFile("image/webp", bytes);

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(UploadPaths.IsSafeStoredPath(result.Value));
        Assert.EndsWith(".webp", result.Value);
        Assert.True(File.Exists(Path.Combine(
            host.Env.WebRootPath,
            result.Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task SaveImageAsync_UnsignedIn_Fails()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0x00]);

        var result = await host.Inventory.SaveImageAsync(file, new System.Security.Claims.ClaimsPrincipal());

        Assert.False(result.Success);
        Assert.Contains("signed in", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SaveImageAsync_ContentTypeJpegButHtmlBytes_Fails()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", "<html><script></script></html>"u8.ToArray());

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.False(result.Success);
        Assert.Contains("JPG", result.Error);
        Assert.Null(result.Value);
        Assert.False(Directory.Exists(Path.Combine(host.Env.WebRootPath, "uploads"))
            && Directory.EnumerateFiles(Path.Combine(host.Env.WebRootPath, "uploads")).Any());
    }

    [Fact]
    public async Task SaveImageAsync_UnsupportedType_Fails()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/gif", [0x47, 0x49, 0x46]);

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

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

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.False(result.Success);
        Assert.Contains("5 MB", result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task DeleteImageAsync_SafePath_RemovesFile()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", [0xFF, 0xD8, 0xFF, 0x00]);
        var uploaded = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));
        var physical = Path.Combine(host.Env.WebRootPath, uploaded.Value!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(physical));

        await host.Inventory.DeleteImageAsync(uploaded.Value);

        Assert.False(File.Exists(physical));
    }

    [Fact]
    public async Task DeleteImageAsync_TraversalPath_DoesNotDelete()
    {
        using var host = new ServiceHost();
        var decoy = Path.Combine(host.Env.WebRootPath, "keep.txt");
        await File.WriteAllTextAsync(decoy, "keep");

        await host.Inventory.DeleteImageAsync("/uploads/../keep.txt");

        Assert.True(File.Exists(decoy));
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
