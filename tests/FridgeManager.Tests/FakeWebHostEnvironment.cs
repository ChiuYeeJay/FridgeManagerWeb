using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace FridgeManager.Tests;

internal sealed class FakeWebHostEnvironment : IWebHostEnvironment, IDisposable
{
    public FakeWebHostEnvironment()
    {
        WebRootPath = Path.Combine(Path.GetTempPath(), "fm-webroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WebRootPath);
        ContentRootPath = WebRootPath;
        WebRootFileProvider = new NullFileProvider();
        ContentRootFileProvider = new NullFileProvider();
    }

    public string WebRootPath { get; set; }
    public IFileProvider WebRootFileProvider { get; set; }
    public string ApplicationName { get; set; } = "FridgeManager.Tests";
    public IFileProvider ContentRootFileProvider { get; set; }
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Development";

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(WebRootPath))
            {
                Directory.Delete(WebRootPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
