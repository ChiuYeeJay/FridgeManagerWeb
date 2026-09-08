using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Net;
using System.Text.Json;
using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;
using OpenAI;
using OpenAI.Chat;

namespace FridgeManager.Services;

public sealed class OpenRouterFoodImageAnalyzer(
    ChatClient chatClient,
    ILogger<OpenRouterFoodImageAnalyzer> logger) : IFoodImageAnalyzer
{
    public const string FailureMessage =
        "AI analysis could not be completed. You can continue filling the form manually.";
    public const string TimeoutMessage =
        "AI analysis timed out. You can continue filling the form manually.";
    public const string UnavailableMessage =
        "AI analysis is temporarily unavailable. You can continue filling the form manually.";
    public const string QuotaMessage =
        "AI analysis quota was reached. You can continue filling the form manually.";

    public const string Instruction =
        """
        You are analyzing a photo for a shared refrigerator inventory form.

        Return only information supported by the image.

        Do not invent an expiration date.
        Only return expirationDate if an expiration, best-by, use-by,
        or equivalent date is visibly readable. Format it as yyyy-MM-dd.

        category must be exactly one of:
        Drink, Snack, Meal, Ingredient, Other.

        sizeUnits must be 1, 2, or 3 based on how a person would pick the item up:
        - 1 (small): both palms can wrap around it (a yogurt cup, a can, an apple)
        - 2 (medium): too big for both palms, but one hand can lift it (a milk carton, a loaf)
        - 3 (large): needs both hands to lift (a watermelon, a family-size tray, a pot)
        Judge the physical item in the photo, not the packaging text.

        name is a short product name, at most a few words.

        note is an optional short note for the team about the food
        (storage hint, leftover, opened). Put packaging caution text,
        allergen statements, or cooking instructions here if useful.
        Do not invent a note.

        warnings is only for problems with this analysis
        (no food visible, image too blurry, printed date unreadable).
        Do not copy packaging labels or allergen warnings into warnings.

        If a value cannot be determined reliably, return null.

        Do not infer owner, shelf, sharing status, or permissions.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly BinaryData SuggestionSchema = BinaryData.FromBytes("""
        {
          "type": "object",
          "properties": {
            "name": { "type": ["string", "null"] },
            "category": {
              "type": ["string", "null"],
              "enum": ["Drink", "Snack", "Meal", "Ingredient", "Other", null]
            },
            "expirationDate": {
              "type": ["string", "null"],
              "description": "yyyy-MM-dd, only if visibly printed"
            },
            "sizeUnits": {
              "type": ["integer", "null"],
              "description": "1 = both palms wrap around it; 2 = one-handed lift; 3 = needs both hands"
            },
            "note": {
              "type": ["string", "null"],
              "description": "optional team note; packaging cautions go here, not in warnings"
            },
            "warnings": {
              "type": "array",
              "items": { "type": "string" },
              "description": "analysis problems only, not packaging labels"
            }
          },
          "required": ["name", "category", "expirationDate", "sizeUnits", "note", "warnings"],
          "additionalProperties": false
        }
        """u8.ToArray());

    private static long _lastWarmupTick;

    internal static ChatClient CreateChatClient(OpenRouterOptions options, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);

        var timeoutSeconds = options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 45;
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? OpenRouterOptions.DefaultBaseUrl
            : options.BaseUrl.TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(options.Model)
            ? OpenRouterOptions.DefaultModel
            : options.Model;

        return new ChatClient(
            model,
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl),
                Transport = new HttpClientPipelineTransport(httpClient),
                NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
            });
    }

    public async Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] processedImage,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processedImage);

        var messages = CreateAnalysisMessages(processedImage);
        var chatOptions = CreateAnalysisOptions();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            ChatCompletion completion;
            try
            {
                completion = await chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "OpenRouter analysis timed out.");
                return OperationResult<FoodImageAnalysisResult>.Fail(TimeoutMessage);
            }
            catch (ClientResultException ex)
            {
                var status = ex.Status;
                logger.LogWarning(
                    "OpenRouter analysis returned HTTP {StatusCode}: {ErrorBody}",
                    status,
                    Truncate(RawBody(ex)));

                if (status == (int)HttpStatusCode.ServiceUnavailable && attempt < 2)
                {
                    try
                    {
                        await Task.Delay(400, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    continue;
                }

                return OperationResult<FoodImageAnalysisResult>.Fail(status switch
                {
                    (int)HttpStatusCode.ServiceUnavailable => UnavailableMessage,
                    (int)HttpStatusCode.TooManyRequests => QuotaMessage,
                    _ => FailureMessage
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OpenRouter analysis request failed.");
                return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
            }

            return ParseCompletion(completion);
        }

        return OperationResult<FoodImageAnalysisResult>.Fail(UnavailableMessage);
    }

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarmupTick);
        if (last != 0 && now - last < 120_000)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastWarmupTick, now, last) != last)
        {
            return;
        }

        try
        {
            await chatClient.CompleteChatAsync(
                [new UserChatMessage("ok")],
                DisableReasoning(new ChatCompletionOptions { MaxOutputTokenCount = 1 }),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "OpenRouter warmup request did not complete.");
        }
    }

    private OperationResult<FoodImageAnalysisResult> ParseCompletion(ChatCompletion completion)
    {
        string? text;
        try
        {
            text = completion.Content?
                .Where(part => part.Kind == ChatMessageContentPartKind.Text)
                .Select(part => part.Text)
                .LastOrDefault(t => !string.IsNullOrWhiteSpace(t));
        }
        catch (ArgumentOutOfRangeException)
        {
            logger.LogWarning("OpenRouter analysis returned no completion choices.");
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning(
                "OpenRouter analysis returned no candidate text (finishReason={FinishReason}, refusal={Refusal}).",
                completion.FinishReason,
                completion.Refusal);
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        text = UnwrapJson(text);

        SuggestionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SuggestionDto>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OpenRouter analysis returned invalid suggestion JSON: {Preview}", Truncate(text));
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        if (dto is null)
        {
            logger.LogWarning("OpenRouter analysis returned empty suggestion JSON: {Preview}", Truncate(text));
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        var mapped = Map(dto);
        logger.LogInformation(
            "OpenRouter suggested name={Name}, category={Category}, expiration={Expiration}, size={Size}, note={Note}, warnings={WarningCount}.",
            mapped.Name,
            mapped.Category,
            mapped.ExpirationDate,
            mapped.SizeUnits,
            mapped.Note,
            mapped.Warnings.Count);
        return OperationResult<FoodImageAnalysisResult>.Ok(mapped);
    }

    private static List<ChatMessage> CreateAnalysisMessages(byte[] processedImage) =>
    [
        new SystemChatMessage(Instruction),
        new UserChatMessage(
            ChatMessageContentPart.CreateTextPart("Analyze this refrigerator item photo."),
            ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(processedImage),
                ImageNormalizer.WebpContentType))
    ];

    private static ChatCompletionOptions CreateAnalysisOptions()
        => DisableReasoning(new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "food_image_analysis",
                jsonSchema: SuggestionSchema,
                jsonSchemaFormatDescription: "Shared refrigerator inventory suggestions from a photo.",
                jsonSchemaIsStrict: false)
        });

#pragma warning disable SCME0001
    private static ChatCompletionOptions DisableReasoning(ChatCompletionOptions options)
    {
        options.Patch.Set("$.reasoning.effort"u8, "none");
        options.Patch.Set("$.reasoning.enabled"u8, false);
        return options;
    }
#pragma warning restore SCME0001

    private static FoodImageAnalysisResult Map(SuggestionDto dto)
    {
        FoodCategory? category = null;
        if (!string.IsNullOrWhiteSpace(dto.Category)
            && Enum.TryParse<FoodCategory>(dto.Category.Trim(), ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed))
        {
            category = parsed;
        }

        DateOnly? expiration = null;
        if (!string.IsNullOrWhiteSpace(dto.ExpirationDate)
            && DateOnly.TryParseExact(
                dto.ExpirationDate.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            expiration = date;
        }

        IReadOnlyList<string> warnings = dto.Warnings is { Count: > 0 }
            ? dto.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToArray()
            : [];

        return new FoodImageAnalysisResult(dto.Name, category, expiration, dto.SizeUnits, dto.Note, warnings);
    }

    private static string UnwrapJson(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('\n');
        var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return start >= 0 && end > start
            ? trimmed[(start + 1)..end].Trim()
            : trimmed;
    }

    private static string RawBody(ClientResultException ex)
    {
        try
        {
            return ex.GetRawResponse()?.Content?.ToString() ?? ex.Message;
        }
        catch (Exception)
        {
            return ex.Message;
        }
    }

    private static string Truncate(string? text, int max = 400)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.Length <= max ? text : text[..max] + "…";
    }

    private sealed class SuggestionDto
    {
        public string? Name { get; set; }
        public string? Category { get; set; }
        public string? ExpirationDate { get; set; }
        public int? SizeUnits { get; set; }
        public string? Note { get; set; }
        public List<string>? Warnings { get; set; }
    }
}
