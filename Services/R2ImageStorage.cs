using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace FridgeManager.Services;

public sealed class R2ImageStorage(IAmazonS3 s3, IOptions<R2Options> options) : IImageStorage
{
    public async Task<string> SaveAsync(
        Stream normalizedImage,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedImage);

        var r2 = options.Value;
        var key = UploadPaths.NewStorageKey(DateTime.UtcNow);
        var request = new PutObjectRequest
        {
            BucketName = r2.BucketName,
            Key = key,
            InputStream = normalizedImage,
            ContentType = contentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };
        await s3.PutObjectAsync(request, cancellationToken);
        return key;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (!UploadPaths.IsSafeStorageKey(storageKey))
        {
            return;
        }

        await s3.DeleteObjectAsync(options.Value.BucketName, storageKey, cancellationToken);
    }

    public string? GetPublicUrl(string? storageKey)
    {
        if (!UploadPaths.IsSafeStorageKey(storageKey))
        {
            return null;
        }

        return $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{storageKey}";
    }
}
