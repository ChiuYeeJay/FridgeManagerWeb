namespace FridgeManager.Services;

public sealed class LocalImageStorage(IWebHostEnvironment env) : IImageStorage
{
    public async Task<string> SaveAsync(
        Stream normalizedImage,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedImage);

        if (string.IsNullOrWhiteSpace(env.WebRootPath))
        {
            throw new InvalidOperationException("Web root is not configured.");
        }

        var key = UploadPaths.NewStorageKey(DateTime.UtcNow);
        var physicalPath = PhysicalPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using var output = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await normalizedImage.CopyToAsync(output, cancellationToken);
        return key;
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!UploadPaths.IsSafeStorageKey(storageKey) || string.IsNullOrWhiteSpace(env.WebRootPath))
        {
            return Task.CompletedTask;
        }

        var physicalPath = PhysicalPath(storageKey);
        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }

        return Task.CompletedTask;
    }

    public string? GetPublicUrl(string? storageKey)
        => UploadPaths.IsSafeStorageKey(storageKey)
            ? $"{UploadPaths.UrlPrefix}{storageKey}"
            : null;

    private string PhysicalPath(string storageKey)
        => Path.Combine(
            env.WebRootPath,
            UploadPaths.FolderName,
            storageKey.Replace('/', Path.DirectorySeparatorChar));
}
