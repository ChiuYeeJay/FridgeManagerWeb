using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace FridgeManager.Tests;

internal static class TestImages
{
    public static byte[] Jpeg(int width = 8, int height = 8)
        => Encode(width, height, new JpegEncoder());

    public static byte[] Png(int width = 8, int height = 8)
        => Encode(width, height, new PngEncoder());

    public static byte[] Webp(int width = 8, int height = 8)
        => Encode(width, height, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossy,
            Quality = 80
        });

    private static byte[] Encode(int width, int height, IImageEncoder encoder)
    {
        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        using var output = new MemoryStream();
        image.Save(output, encoder);
        return output.ToArray();
    }
}
