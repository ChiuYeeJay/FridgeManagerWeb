using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;

namespace FridgeManager.Services;

public sealed class FakeFoodImageAnalyzer : IFoodImageAnalyzer
{
    public static readonly FoodImageAnalysisResult Sample = new(
        "Greek Yogurt",
        FoodCategory.Snack,
        null,
        1,
        null,
        ["Sample result from the fake analyzer"]);

    private readonly FoodImageAnalysisResult _result;

    public FakeFoodImageAnalyzer(FoodImageAnalysisResult? result = null)
        => _result = result ?? Sample;

    public Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] processedImage,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processedImage);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<FoodImageAnalysisResult>.Ok(_result));
    }
}
