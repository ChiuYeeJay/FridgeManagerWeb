using FridgeManager.Data.Enums;
using FridgeManager.Services;
using FridgeManager.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace FridgeManager.Tests;

public sealed class FoodImageAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_Anonymous_FailsWithSignInMessage()
    {
        var host = Host();
        var result = await host.Service.AnalyzeAsync(TestImages.Jpeg(), "image/jpeg", new());

        Assert.False(result.Success);
        Assert.Equal(FoodImageAnalysisService.SignInMessage, result.Error);
        Assert.Equal(0, host.Analyzer.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenDisabled_FailsWithoutCallingAnalyzer()
    {
        var host = Host(enabled: false);
        var result = await host.Service.AnalyzeAsync(
            TestImages.Jpeg(),
            "image/jpeg",
            Principals.For("alice"));

        Assert.False(result.Success);
        Assert.Equal(FoodImageAnalysisService.UnavailableMessage, result.Error);
        Assert.Equal(0, host.Analyzer.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_RateLimitConsumedWhenAnalyzerFails()
    {
        var host = Host(analyzerResult: OperationResult<FoodImageAnalysisResult>.Fail("boom"));

        for (var i = 0; i < 20; i++)
        {
            var failed = await host.Service.AnalyzeAsync(
                TestImages.Jpeg(),
                "image/jpeg",
                Principals.For("alice"));
            Assert.False(failed.Success);
            Assert.Equal(FoodImageAnalysisService.FailureMessage, failed.Error);
        }

        var limited = await host.Service.AnalyzeAsync(
            TestImages.Jpeg(),
            "image/jpeg",
            Principals.For("alice"));

        Assert.False(limited.Success);
        Assert.Equal(FoodImageAnalysisService.RateLimitMessage, limited.Error);
        Assert.Equal(20, host.Analyzer.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_AnalyzerFailure_DoesNotCreateFoodItem()
    {
        using var db = new SqliteDbFactory();
        var seed = TestData.Seed(db);
        await using (var context = await db.CreateDbContextAsync())
        {
            Assert.Equal(4, context.FoodItems.Count());
        }

        var host = Host(analyzerResult: OperationResult<FoodImageAnalysisResult>.Fail("unavailable"));
        var result = await host.Service.AnalyzeAsync(
            TestImages.Jpeg(),
            "image/jpeg",
            Principals.For(seed.AliceId));

        Assert.False(result.Success);
        await using (var context = await db.CreateDbContextAsync())
        {
            Assert.Equal(4, context.FoodItems.Count());
        }
    }

    [Fact]
    public async Task AnalyzeAsync_SendsNormalizedWebpWithoutExif()
    {
        var host = Host();
        using var image = new Image<Rgba32>(2000, 1000, Color.Red);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Software, "FridgeManagerTest");
        image.Metadata.ExifProfile = exif;
        using var encoded = new MemoryStream();
        image.Save(encoded, new JpegEncoder());

        var result = await host.Service.AnalyzeAsync(
            encoded.ToArray(),
            "image/jpeg",
            Principals.For("alice"));

        Assert.True(result.Success);
        Assert.Equal("image/webp", host.Analyzer.LastContentType);
        Assert.NotNull(host.Analyzer.LastImage);
        using var decoded = Image.Load(host.Analyzer.LastImage);
        Assert.True(Math.Max(decoded.Width, decoded.Height) <= 1600);
        Assert.Equal(1600, decoded.Width);
        Assert.Equal(800, decoded.Height);
        Assert.Null(decoded.Metadata.ExifProfile);
    }

    [Fact]
    public async Task AnalyzeAsync_DropsInvalidFields()
    {
        var host = Host(new FoodImageAnalysisResult(
            new string('a', 250),
            (FoodCategory)99,
            new DateOnly(1999, 12, 31),
            9,
            new string('n', 1200),
            ["from model"]));

        var result = await host.Service.AnalyzeAsync(
            TestImages.Jpeg(),
            "image/jpeg",
            Principals.For("alice"));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(200, result.Value.Name!.Length);
        Assert.Null(result.Value.Category);
        Assert.Null(result.Value.ExpirationDate);
        Assert.Null(result.Value.SizeUnits);
        Assert.Equal(1000, result.Value.Note!.Length);
        Assert.Contains("from model", result.Value.Warnings);
        Assert.Contains(result.Value.Warnings, w => w.Contains("category", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Value.Warnings, w => w.Contains("expiration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Value.Warnings, w => w.Contains("size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_FutureDateBeyondFiveYears_IsDropped()
    {
        var tooFar = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(6);
        var host = Host(new FoodImageAnalysisResult("Milk", FoodCategory.Drink, tooFar, 1, null, []));

        var result = await host.Service.AnalyzeAsync(
            TestImages.Jpeg(),
            "image/jpeg",
            Principals.For("alice"));

        Assert.True(result.Success);
        Assert.Null(result.Value!.ExpirationDate);
    }

    private static AnalysisHost Host(
        FoodImageAnalysisResult? okResult = null,
        OperationResult<FoodImageAnalysisResult>? analyzerResult = null,
        bool enabled = true)
    {
        var analyzer = new RecordingAnalyzer
        {
            Result = analyzerResult
                ?? OperationResult<FoodImageAnalysisResult>.Ok(okResult ?? FakeFoodImageAnalyzer.Sample)
        };
        return new AnalysisHost(analyzer, enabled);
    }

    private sealed class AnalysisHost
    {
        public RecordingAnalyzer Analyzer { get; }
        public FoodImageAnalysisService Service { get; }

        public AnalysisHost(RecordingAnalyzer analyzer, bool enabled)
        {
            Analyzer = analyzer;
            Service = new FoodImageAnalysisService(
                analyzer,
                new AiRateLimiter(),
                Options.Create(new GeminiOptions
                {
                    Enabled = enabled,
                    MaxRequestsPerUserPerHour = 20
                }));
        }
    }

    internal sealed class RecordingAnalyzer : IFoodImageAnalyzer
    {
        public int Calls { get; private set; }
        public byte[]? LastImage { get; private set; }
        public string? LastContentType { get; private set; }
        public OperationResult<FoodImageAnalysisResult> Result { get; set; } =
            OperationResult<FoodImageAnalysisResult>.Ok(FakeFoodImageAnalyzer.Sample);

        public Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
            byte[] processedImage,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastImage = processedImage;
            LastContentType = contentType;
            return Task.FromResult(Result);
        }
    }
}
