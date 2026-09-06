namespace FridgeManager.Services;

public interface IImageStorage
{
    /// Stores an already-normalized image and returns its storage key.
    Task<string> SaveAsync(
        Stream normalizedImage,
        string contentType,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    /// Returns a browser-usable URL, or null if the key is not a valid storage key.
    string? GetPublicUrl(string? storageKey);
}
