using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace FridgeManager.Services;

public sealed record NormalizedImage(byte[] Bytes, string ContentType);

public static class ImageNormalizer
{
    public const int MaxLongEdge = 2000;
    public const int WebpQuality = 80;
    public const string WebpContentType = "image/webp";

    public static NormalizedImage? Normalize(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var image = Image.Load(bytes);
            image.Mutate(x => x.AutoOrient());

            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IccProfile = null;

            var longEdge = Math.Max(image.Width, image.Height);
            if (longEdge > MaxLongEdge)
            {
                var scale = MaxLongEdge / (double)longEdge;
                var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                var height = Math.Max(1, (int)Math.Round(image.Height * scale));
                image.Mutate(x => x.Resize(width, height));
            }

            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = WebpQuality
            });
            return new NormalizedImage(output.ToArray(), WebpContentType);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
