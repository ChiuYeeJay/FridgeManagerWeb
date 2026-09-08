using System.Net;
using System.Text;
using System.Text.Json;
using FridgeManager.Data.Enums;
using FridgeManager.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FridgeManager.Tests;

public sealed class OpenRouterFoodImageAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_ValidJson_MapsFieldsAndSendsExpectedRequest()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk(Completion("""{"name":"Milk","category":"Drink","expirationDate":"2026-12-31","sizeUnits":2,"note":"Keep upright","warnings":[]}"""))
        };
        var analyzer = Create(handler, apiKey: "test-key", model: "openai/gpt-4o-mini");

        var result = await analyzer.AnalyzeAsync([1, 2, 3], ImageNormalizer.WebpContentType);

        Assert.True(result.Success);
        Assert.Equal("Milk", result.Value!.Name);
        Assert.Equal(FoodCategory.Drink, result.Value.Category);
        Assert.Equal(new DateOnly(2026, 12, 31), result.Value.ExpirationDate);
        Assert.Equal(2, result.Value.SizeUnits);
        Assert.Equal("Keep upright", result.Value.Note);

        Assert.NotNull(handler.Request);
        Assert.Equal("Bearer test-key", handler.Request.Headers.Authorization?.ToString());
        Assert.Contains("/chat/completions", handler.Request.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("openai/gpt-4o-mini", handler.Body, StringComparison.Ordinal);
        Assert.Contains("food_image_analysis", handler.Body, StringComparison.Ordinal);
        Assert.Contains("json_schema", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"reasoning\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"effort\":\"none\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"enabled\":false", handler.Body, StringComparison.Ordinal);
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
    public async Task AnalyzeAsync_UnwrapsMarkdownFence()
    {
        var fenced = """
            ```json
            {"name":"Milk","category":"Drink","expirationDate":null,"sizeUnits":2,"warnings":[]}
            ```
            """;
        var handler = new StubHandler { Response = _ => JsonOk(Completion(fenced)) };
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
            {
                Content = new StringContent("""{"error":{"message":"boom"}}""", Encoding.UTF8, "application/json")
            }
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(OpenRouterFoodImageAnalyzer.FailureMessage, result.Error);
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
                    Content = new StringContent("""{"error":{"message":"unavailable"}}""", Encoding.UTF8, "application/json")
                }
                : JsonOk(Completion("""{"name":"Milk","category":"Drink","expirationDate":null,"sizeUnits":1,"warnings":[]}"""))
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
                Content = new StringContent("""{"error":{"message":"unavailable"}}""", Encoding.UTF8, "application/json")
            }
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(OpenRouterFoodImageAnalyzer.UnavailableMessage, result.Error);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_Http429_FailsQuota()
    {
        var handler = new StubHandler
        {
            Response = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":{"message":"rate limited"}}""", Encoding.UTF8, "application/json")
            }
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(OpenRouterFoodImageAnalyzer.QuotaMessage, result.Error);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyChoices_Fails()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk("""{"id":"chatcmpl-1","object":"chat.completion","choices":[]}""")
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(OpenRouterFoodImageAnalyzer.FailureMessage, result.Error);
    }

    [Fact]
    public async Task AnalyzeAsync_NonJsonCandidateText_Fails()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk(Completion("not json"))
        };
        var analyzer = Create(handler);

        var result = await analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType);

        Assert.False(result.Success);
        Assert.Equal(OpenRouterFoodImageAnalyzer.FailureMessage, result.Error);
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
        Assert.Equal(OpenRouterFoodImageAnalyzer.TimeoutMessage, result.Error);
    }

    [Fact]
    public async Task AnalyzeAsync_CancelledToken_Throws()
    {
        var handler = new StubHandler
        {
            Response = _ => JsonOk(Completion("""{"name":"Milk","category":"Drink","expirationDate":null,"sizeUnits":1,"warnings":[]}"""))
        };
        var analyzer = Create(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync([1], ImageNormalizer.WebpContentType, cts.Token));
    }

    private static StubHandler OkHandler(string suggestionJson)
        => new() { Response = _ => JsonOk(Completion(suggestionJson)) };

    private static OpenRouterFoodImageAnalyzer Create(
        StubHandler handler,
        string apiKey = "test-key",
        string model = "openai/gpt-4o-mini")
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };
        var chat = OpenRouterFoodImageAnalyzer.CreateChatClient(
            new OpenRouterOptions { ApiKey = apiKey, Model = model, Enabled = true },
            client);
        return new OpenRouterFoodImageAnalyzer(chat, NullLogger<OpenRouterFoodImageAnalyzer>.Instance);
    }

    private static string Completion(string suggestionJson)
        => JsonSerializer.Serialize(new
        {
            id = "chatcmpl-test",
            @object = "chat.completion",
            created = 1_700_000_000,
            model = "openai/gpt-4o-mini",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = suggestionJson },
                    finish_reason = "stop"
                }
            }
        });

    private static HttpResponseMessage JsonOk(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

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
