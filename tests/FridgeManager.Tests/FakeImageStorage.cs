using FridgeManager.Services;

namespace FridgeManager.Tests;

internal sealed class FakeImageStorage : IImageStorage
{
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public List<string> Deleted { get; } = [];
    public bool ThrowOnSave { get; set; }
    public bool ThrowOnDelete { get; set; }

    public Task<string> SaveAsync(
        Stream normalizedImage,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnSave)
        {
            throw new InvalidOperationException("storage save failed");
        }

        using var buffer = new MemoryStream();
        normalizedImage.CopyTo(buffer);
        var key = UploadPaths.NewStorageKey(DateTime.UtcNow);
        _objects[key] = buffer.ToArray();
        return Task.FromResult(key);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (ThrowOnDelete)
        {
            throw new InvalidOperationException("storage delete failed");
        }

        Deleted.Add(storageKey);
        _objects.Remove(storageKey);
        return Task.CompletedTask;
    }

    public string? GetPublicUrl(string? storageKey)
        => UploadPaths.IsSafeStorageKey(storageKey) ? $"/uploads/{storageKey}" : null;

    public void Seed(string key, byte[] bytes) => _objects[key] = bytes;

    public bool Contains(string key) => _objects.ContainsKey(key);
}
