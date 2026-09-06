using System.Net;
using System.Text;
using System.Text.Json;
using FridgeManager.Data.Enums;
using FridgeManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FridgeManager.Tests;

public sealed class GeminiFoodImageAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ValidJson_MapsFieldsAndSendsExpectedRequest()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk(Envelope("""{"name":"Milk","category":"Drink","expirationDate":"2026-12-31","sizeUnits":2,"note":"Keep upright","warnings":[]}"""))
        };
        var analyzer = Create(handler, apiKey: "test-key", model: "gemini-3.5-flash-lite");

        var result = await analyzer.AnalyzeAsync([1, 2, 3], ImageNormalizer.WebpContentType);

        Assert.True(result.Success);
        Assert.Equal("Milk", result.Value!.Name);
        Assert.Equal(FoodCategory.Drink, result.Value.Category);
        Assert.Equal(new DateOnly(2026, 12, 31), result.Value.ExpirationDate);
        Assert.Equal(2, result.Value.SizeUnits);
        Assert.Equal("Keep upright", result.Value.Note);

        Assert.NotNull(handler.Request);
        Assert.Equal("test-key", handler.Request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Contains("v1beta/models/gemini-3.5-flash-lite:generateContent", handler.Request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("inlineData", handler.Body, StringComparison.Ordinal);
        Assert.Contains("responseSchema", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"thinkingLevel\":\"minimal\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("both palms can wrap around it", handler.Body, StringComparison.Ordinal);
        Assert.Contains("note is an optional short note", handler.Body, StringComparison.Ordinal);
        Assert.Contains("problems with this analysis", handler.Body, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String([1, 2, 3]), handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_UnknownCategory_MapsToNull()
    {
        var handler = OkHandler("""{"name":"Soda","category":"Beverage","expirationDate":null,"sizeUnits":1,"warnings":[]}""");
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.True(result.Success);
        Assert.Equal("Soda", result.Value!.Name);
        Assert.Null(result.Value.Category);
        Assert.Equal(1, result.Value.SizeUnits);
    }

    [Fact]
    public async Task AnalyzeAsync_MalformedDate_MapsToNull()
    {
        var handler = OkHandler("""{"name":"Yogurt","category":"Snack","expirationDate":"31/12/2026","sizeUnits":1,"warnings":[]}""");
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.True(result.Success);
        Assert.Equal("Yogurt", result.Value!.Name);
        Assert.Null(result.Value.ExpirationDate);
    }

    [Fact]
    public async Task AnalyzeAsync_SkipsThoughtPartAndUsesJsonText()
    {
        var envelope = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    finishReason = "STOP",
                    content = new
                    {
                        parts = new object[]
                        {
                            new { thought = true, text = "I am thinking about the yogurt." },
                            new { text = """{"name":"Greek Yogurt","category":"Snack","expirationDate":null,"sizeUnits":1,"warnings":[]}""" }
                        }
                    }
                }
            }
        });
        var handler = new StubHandler { Response = _ => JsonOk(envelope) };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.True(result.Success);
        Assert.Equal("Greek Yogurt", result.Value!.Name);
        Assert.Equal(FoodCategory.Snack, result.Value.Category);
    }

    [Fact]
    public async Task AnalyzeAsync_UnwrapsMarkdownFence()
    {
        var fenced = """
            ```json
            {"name":"Milk","category":"Drink","expirationDate":null,"sizeUnits":2,"warnings":[]}
            ```
            """;
        var handler = new StubHandler { Response = _ => JsonOk(Envelope(fenced)) };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.True(result.Success);
        Assert.Equal("Milk", result.Value!.Name);
        Assert.Equal(2, result.Value.SizeUnits);
    }

    [Fact]
    public async Task AnalyzeAsync_Http500_FailsWithoutRetry()
    {
        var handler = new StubHandler
        {
            Response = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(GeminiFoodImageAnalyzer.FailureMessage, result.Error);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_Http503ThenSuccess_RetriesOnce()
    {
        var handler = new StubHandler
        {
            Response = n => n == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("""{"error":{"status":"UNAVAILABLE"}}""", Encoding.UTF8, "application/json")
                }
                : JsonOk(Envelope("""{"name":"Milk","category":"Drink","expirationDate":null,"sizeUnits":1,"warnings":[]}"""))
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.True(result.Success);
        Assert.Equal("Milk", result.Value!.Name);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_Http503Twice_FailsUnavailable()
    {
        var handler = new StubHandler
        {
            Response = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"error":{"status":"UNAVAILABLE"}}""", Encoding.UTF8, "application/json")
            }
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(GeminiFoodImageAnalyzer.UnavailableMessage, result.Error);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_Http429_FailsQuota()
    {
        var handler = new StubHandler
        {
            Response = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(GeminiFoodImageAnalyzer.QuotaMessage, result.Error);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyCandidates_Fails()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk("""{"candidates":[]}""")
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(GeminiFoodImageAnalyzer.FailureMessage, result.Error);
    }

    [Fact]
    public async Task AnalyzeAsync_NonJsonCandidateText_Fails()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk(Envelope("not json"))
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(GeminiFoodImageAnalyzer.FailureMessage, result.Error);
    }

    [Fact]
    public async Task AnalyzeAsync_Timeout_Fails()
    {
        var handler = new StubHandler
        {
            Response = _ => throw new TaskCanceledException("timeout")
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(GeminiFoodImageAnalyzer.TimeoutMessage, result.Error);
    }

    [Fact]
    public async Task AnalyzeAsync_CancelledToken_Throws()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk(Envelope("""{"name":"Milk","category":"Drink","expirationDate":null,"sizeUnits":1,"warnings":[]}"""))
        };
        var analyzer = Create(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType, cts.Token));
    }

    private static StubHandler OkHandler(string suggestionJson)
        => new() { Response = _ => JsonOk(Envelope(suggestionJson)) };

    private static GeminiFoodImageAnalyzer Create(
        StubHandler handler,
        string apiKey = "test-key",
        string model = "gemini-3.5-flash-lite")
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        return new GeminiFoodImageAnalyzer(
            new StubHttpClientFactory(client),
            Options.Create(new GeminiOptions { ApiKey = apiKey, Model = model, Enabled = true }),
            NullLogger<GeminiFoodImageAnalyzer>.Instance);
    }

    private static string Envelope(string suggestionJson)
        => JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = suggestionJson } } } }
            }
        });

    private static HttpResponseMessage JsonOk(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public int Calls { get; private set; }
        public required Func<int, HttpResponseMessage> Response { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response(Calls);
        }
    }
}
