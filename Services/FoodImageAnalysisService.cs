using System.Security.Claims;
using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;
using Microsoft.Extensions.Options;

namespace FridgeManager.Services;

public sealed class FoodImageAnalysisService(
    IFoodImageAnalyzer analyzer,
    AiRateLimiter limiter,
    IOptions<GeminiOptions> options) : IFoodImageAnalysisService
{
    public const string SignInMessage = "Sign in to use AI autofill.";
    public const string UnavailableMessage = "AI autofill is not available.";
    public const string RateLimitMessage =
        "You have used all AI analyses for this hour. You can continue filling the form manually.";
    public const string FailureMessage =
        "AI analysis could not be completed. You can continue filling the form manually.";

    public async Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] originalImage,
        string contentType,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalImage);
        ArgumentNullException.ThrowIfNull(user);

        var userId = UserClaims.GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            return OperationResult<FoodImageAnalysisResult>.Fail(SignInMessage);
        }

        var gemini = options.Value;
        if (!gemini.Enabled)
        {
            return OperationResult<FoodImageAnalysisResult>.Fail(UnavailableMessage);
        }

        var limit = gemini.MaxRequestsPerUserPerHour;
        if (!limiter.TryAcquire(userId, limit, TimeSpan.FromHours(1)))
        {
            return OperationResult<FoodImageAnalysisResult>.Fail(RateLimitMessage);
        }

        var normalized = ImageNormalizer.Normalize(originalImage, ImageNormalizer.AiMaxLongEdge);
        if (normalized is null)
        {
            return OperationResult<FoodImageAnalysisResult>.Fail(FailureMessage);
        }

        OperationResult<FoodImageAnalysisResult> analyzed;
        try
        {
            analyzed = await analyzer.AnalyzeAsync(
                normalized.Bytes,
                normalized.ContentType,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        if (!analyzed.Success || analyzed.Value is null)
        {
            return OperationResult<FoodImageAnalysisResult>.Fail(UserFacingError(analyzed.Error));
        }

        return OperationResult<FoodImageAnalysisResult>.Ok(Sanitize(analyzed.Value));
    }

    internal static FoodImageAnalysisResult Sanitize(FoodImageAnalysisResult raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var warnings = raw.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();

        var name = raw.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = null;
        }
        else if (name.Length > 200)
        {
            name = name[..200];
        }

        FoodCategory? category = raw.Category is FoodCategory c && Enum.IsDefined(c) ? c : null;
        if (raw.Category is not null && category is null)
        {
            warnings.Add("The suggested category was ignored.");
        }

        DateOnly? expiration = null;
        if (raw.ExpirationDate is DateOnly date)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var min = new DateOnly(2000, 1, 1);
            var max = today.AddYears(5);
            if (date >= min && date <= max)
            {
                expiration = date;
            }
            else
            {
                warnings.Add("The suggested expiration date was ignored.");
            }
        }

        int? size = raw.SizeUnits is >= 1 and <= 3 ? raw.SizeUnits : null;
        if (raw.SizeUnits is not null && size is null)
        {
            warnings.Add("The suggested size was ignored.");
        }

        var note = raw.Note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            note = null;
        }
        else if (note.Length > 1000)
        {
            note = note[..1000];
        }

        return new FoodImageAnalysisResult(name, category, expiration, size, note, warnings);
    }

    private static string UserFacingError(string? error)
        => error is GeminiFoodImageAnalyzer.TimeoutMessage
            or GeminiFoodImageAnalyzer.UnavailableMessage
            or GeminiFoodImageAnalyzer.QuotaMessage
            ? error
            : FailureMessage;
}
