using Microsoft.AspNetCore.Components.Forms;

namespace FridgeManager.Tests;

internal sealed class FakeBrowserFile : IBrowserFile
{
    private readonly byte[] _bytes;

    public FakeBrowserFile(string contentType, byte[] bytes, string name = "photo.bin")
    {
        ContentType = contentType;
        _bytes = bytes;
        Name = name;
        LastModified = DateTimeOffset.UtcNow;
    }

    public string Name { get; }
    public DateTimeOffset LastModified { get; }
    public long Size => _bytes.Length;
    public string ContentType { get; }

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        if (Size > maxAllowedSize)
        {
            throw new IOException(
                $"Supplied file with size {Size} bytes exceeds the maximum of {maxAllowedSize} bytes.");
        }

        return new MemoryStream(_bytes, writable: false);
    }
}
