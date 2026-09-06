using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;
using Microsoft.Extensions.Options;

namespace FridgeManager.Services;

public sealed class GeminiFoodImageAnalyzer(
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiOptions> options,
    ILogger<GeminiFoodImageAnalyzer> logger) : IFoodImageAnalyzer
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

    public async Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] processedImage,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processedImage);

        var gemini = options.Value;
        var client = httpClientFactory.CreateClient(GeminiOptions.HttpClientName);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var request = CreateRequest(processedImage, gemini);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "Gemini analysis timed out.");
                return OperationResult<FoodImageAnalysisResult>.Fail(TimeoutMessage);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gemini analysis request failed.");
                return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    string body;
                    try
                    {
                        body = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Gemini analysis response could not be read.");
                        return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
                    }

                    return ParseResponse(body);
                }

                var status = (int)response.StatusCode;
                var errorBody = Truncate(await SafeReadBodyAsync(response, cancellationToken));
                logger.LogWarning("Gemini analysis returned HTTP {StatusCode}: {ErrorBody}", status, errorBody);

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
        }

        return OperationResult<FoodImageAnalysisResult>.Fail(UnavailableMessage);
    }

    private HttpRequestMessage CreateRequest(byte[] processedImage, GeminiOptions gemini)
    {
        var url = $"v1beta/models/{Uri.EscapeDataString(gemini.Model)}:generateContent";
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", gemini.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(BuildRequestBody(processedImage), Encoding.UTF8, "application/json");
        return request;
    }

    private OperationResult<FoodImageAnalysisResult> ParseResponse(string body)
    {
        GenerateContentResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<GenerateContentResponse>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Gemini analysis returned a non-JSON envelope: {Preview}", Truncate(body));
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        var candidate = envelope?.Candidates?.FirstOrDefault();
        var text = candidate?.Content?.Parts?
            .Where(p => !p.Thought)
            .Select(p => p.Text)
            .LastOrDefault(t => !string.IsNullOrWhiteSpace(t));

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning(
                "Gemini analysis returned no candidate text (finishReason={FinishReason}): {Preview}",
                candidate?.FinishReason,
                Truncate(body));
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
            logger.LogWarning(ex, "Gemini analysis returned invalid suggestion JSON: {Preview}", Truncate(text));
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        if (dto is null)
        {
            logger.LogWarning("Gemini analysis returned empty suggestion JSON: {Preview}", Truncate(text));
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        var mapped = Map(dto);
        logger.LogInformation(
            "Gemini suggested name={Name}, category={Category}, expiration={Expiration}, size={Size}, note={Note}, warnings={WarningCount}.",
            mapped.Name,
            mapped.Category,
            mapped.ExpirationDate,
            mapped.SizeUnits,
            mapped.Note,
            mapped.Warnings.Count);
        return OperationResult<FoodImageAnalysisResult>.Ok(mapped);
    }

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

    private static string BuildRequestBody(byte[] processedImage)
    {
        var payload = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = Instruction },
                        new JsonObject
                        {
                            ["inlineData"] = new JsonObject
                            {
                                ["mimeType"] = ImageNormalizer.WebpContentType,
                                ["data"] = Convert.ToBase64String(processedImage)
                            }
                        }
                    }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = ResponseSchema(),
                ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = "MINIMAL" }
            }
        };

        return payload.ToJsonString();
    }

    private static JsonObject ResponseSchema() => new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "STRING", ["nullable"] = true },
            ["category"] = new JsonObject
            {
                ["type"] = "STRING",
                ["nullable"] = true,
                ["enum"] = new JsonArray("Drink", "Snack", "Meal", "Ingredient", "Other")
            },
            ["expirationDate"] = new JsonObject
            {
                ["type"] = "STRING",
                ["nullable"] = true,
                ["description"] = "yyyy-MM-dd, only if visibly printed"
            },
            ["sizeUnits"] = new JsonObject
            {
                ["type"] = "INTEGER",
                ["nullable"] = true,
                ["description"] = "1 = both palms wrap around it; 2 = one-handed lift; 3 = needs both hands"
            },
            ["note"] = new JsonObject
            {
                ["type"] = "STRING",
                ["nullable"] = true,
                ["description"] = "optional team note; packaging cautions go here, not in warnings"
            },
            ["warnings"] = new JsonObject
            {
                ["type"] = "ARRAY",
                ["items"] = new JsonObject { ["type"] = "STRING" },
                ["description"] = "analysis problems only, not packaging labels"
            }
        },
        ["required"] = new JsonArray("name", "category", "expirationDate", "sizeUnits", "note", "warnings")
    };

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

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return "";
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

    private sealed class GenerateContentResponse
    {
        public List<CandidateDto>? Candidates { get; set; }
    }

    private sealed class CandidateDto
    {
        public ContentDto? Content { get; set; }
        public string? FinishReason { get; set; }
    }

    private sealed class ContentDto
    {
        public List<PartDto>? Parts { get; set; }
    }

    private sealed class PartDto
    {
        public string? Text { get; set; }
        public bool Thought { get; set; }
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
