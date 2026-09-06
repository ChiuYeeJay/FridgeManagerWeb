using FridgeManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;

namespace FridgeManager.Tests;

public sealed class SaveImageTests
{
    [Fact]
    public async Task SaveImageAsync_Jpeg_WritesWebpKeyAndIgnoresClientName()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", TestImages.Jpeg(), name: "from-client.JPEG");

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(UploadPaths.IsSafeStorageKey(result.Value));
        Assert.EndsWith(".webp", result.Value);
        Assert.DoesNotContain("from-client", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(host.PhysicalPath(result.Value)));

        using var decoded = Image.Load(host.PhysicalPath(result.Value));
        Assert.Equal("Webp", decoded.Metadata.DecodedImageFormat?.Name);
    }

    [Fact]
    public async Task SaveImageAsync_Png_WritesWebpKey()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/png", TestImages.Png());

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(UploadPaths.IsSafeStorageKey(result.Value));
        Assert.True(File.Exists(host.PhysicalPath(result.Value)));
    }

    [Fact]
    public async Task SaveImageAsync_Webp_WritesWebpKey()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/webp", TestImages.Webp());

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.True(UploadPaths.IsSafeStorageKey(result.Value));
        Assert.True(File.Exists(host.PhysicalPath(result.Value)));
    }

    [Fact]
    public async Task SaveImageAsync_UnsignedIn_Fails()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", TestImages.Jpeg());

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
        Assert.False(HasUploads(host));
    }

    [Fact]
    public async Task SaveImageAsync_PngBytesClaimedAsJpeg_Fails()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", TestImages.Png());

        var result = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));

        Assert.False(result.Success);
        Assert.Contains("JPG", result.Error);
        Assert.Null(result.Value);
        Assert.False(HasUploads(host));
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
        Assert.False(HasUploads(host));
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
    public async Task DeleteImageAsync_SafeKey_RemovesFile()
    {
        using var host = new ServiceHost();
        var file = new FakeBrowserFile("image/jpeg", TestImages.Jpeg());
        var uploaded = await host.Inventory.SaveImageAsync(file, Principals.For("user-1"));
        var physical = host.PhysicalPath(uploaded.Value!);
        Assert.True(File.Exists(physical));

        await host.Inventory.DeleteImageAsync(uploaded.Value);

        Assert.False(File.Exists(physical));
    }

    [Fact]
    public async Task DeleteImageAsync_LegacyOrTraversal_DoesNotDelete()
    {
        using var host = new ServiceHost();
        var decoy = Path.Combine(host.Env.WebRootPath, "keep.txt");
        await File.WriteAllTextAsync(decoy, "keep");

        await host.Inventory.DeleteImageAsync("/uploads/../keep.txt");
        await host.Inventory.DeleteImageAsync("/uploads/c56f25dfe4d544e0bd1c8fd72ba4d249.jpg");

        Assert.True(File.Exists(decoy));
    }

    [Fact]
    public async Task LocalImageStorage_RoundTrip_WritesUnderUploadsThenDeletes()
    {
        using var host = new ServiceHost();
        await using var stream = new MemoryStream(TestImages.Webp());

        var key = await host.Storage.SaveAsync(stream, "image/webp");

        Assert.True(UploadPaths.IsSafeStorageKey(key));
        Assert.Equal($"/uploads/{key}", host.Storage.GetPublicUrl(key));
        Assert.True(File.Exists(host.PhysicalPath(key)));

        await host.Storage.DeleteAsync(key);

        Assert.False(File.Exists(host.PhysicalPath(key)));
    }

    [Fact]
    public void LocalImageStorage_LegacyPath_ResolvesToNull()
    {
        using var env = new FakeWebHostEnvironment();
        Assert.Null(new LocalImageStorage(env).GetPublicUrl("/uploads/c56f25dfe4d544e0bd1c8fd72ba4d249.jpg"));
    }

    private static bool HasUploads(ServiceHost host)
        => Directory.Exists(Path.Combine(host.Env.WebRootPath, "uploads"))
           && Directory.EnumerateFiles(Path.Combine(host.Env.WebRootPath, "uploads"), "*", SearchOption.AllDirectories).Any();

    private sealed class ServiceHost : IDisposable
    {
        public SqliteDbFactory Factory { get; } = new();
        public FakeWebHostEnvironment Env { get; } = new();
        public LocalImageStorage Storage { get; }
        public InventoryService Inventory { get; }

        public ServiceHost()
        {
            TestData.Seed(Factory);
            Storage = new LocalImageStorage(Env);
            Inventory = new InventoryService(Factory, Storage, NullLogger<InventoryService>.Instance);
        }

        public string PhysicalPath(string key)
            => Path.Combine(Env.WebRootPath, "uploads", key.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            Factory.Dispose();
            Env.Dispose();
        }
    }
}
