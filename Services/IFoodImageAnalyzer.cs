using FridgeManager.Services.Models;

namespace FridgeManager.Services;

public interface IFoodImageAnalyzer
{
    Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] processedImage,
        string contentType,
        CancellationToken cancellationToken = default);
}
