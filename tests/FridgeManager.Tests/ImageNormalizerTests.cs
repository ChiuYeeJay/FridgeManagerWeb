using FridgeManager.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace FridgeManager.Tests;

public sealed class ImageNormalizerTests
{
    [Fact]
    public void Normalize_StripsExif()
    {
        using var image = new Image<Rgba32>(16, 16, Color.Red);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, "FridgeManagerTest");
        image.Metadata.ExifProfile = exif;
        using var encoded = new MemoryStream();
        image.Save(encoded, new JpegEncoder());

        var result = ImageNormalizer.Normalize(encoded.ToArray());

        Assert.NotNull(result);
        Assert.Equal("image/webp", result.ContentType);
        using var decoded = Image.Load(result.Bytes);
        Assert.Null(decoded.Metadata.ExifProfile);
    }

    [Fact]
    public void Normalize_AppliesOrientationAndDoesNotKeepExif()
    {
        using var image = new Image<Rgba32>(40, 80, Color.Green);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        image.Metadata.ExifProfile = exif;
        using var encoded = new MemoryStream();
        image.Save(encoded, new JpegEncoder());

        var result = ImageNormalizer.Normalize(encoded.ToArray());

        Assert.NotNull(result);
        using var decoded = Image.Load(result.Bytes);
        Assert.Equal(80, decoded.Width);
        Assert.Equal(40, decoded.Height);
        Assert.Null(decoded.Metadata.ExifProfile);
    }

    [Fact]
    public void Normalize_CapsLongEdgeAt2000()
    {
        using var image = new Image<Rgba32>(3000, 1500, Color.Blue);
        using var encoded = new MemoryStream();
        image.SaveAsPng(encoded);

        var result = ImageNormalizer.Normalize(encoded.ToArray());

        Assert.NotNull(result);
        using var decoded = Image.Load(result.Bytes);
        Assert.Equal(2000, decoded.Width);
        Assert.Equal(1000, decoded.Height);
    }

    [Fact]
    public void Normalize_DoesNotUpscale()
    {
        using var image = new Image<Rgba32>(100, 50, Color.Yellow);
        using var encoded = new MemoryStream();
        image.SaveAsPng(encoded);

        var result = ImageNormalizer.Normalize(encoded.ToArray());

        Assert.NotNull(result);
        using var decoded = Image.Load(result.Bytes);
        Assert.Equal(100, decoded.Width);
        Assert.Equal(50, decoded.Height);
    }

    [Fact]
    public void Normalize_UndecodableBytes_ReturnsNull()
        => Assert.Null(ImageNormalizer.Normalize("<html>not an image</html>"u8.ToArray()));
}
