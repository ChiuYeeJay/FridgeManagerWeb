using System.Security.Claims;
using FridgeManager.Services.Models;

namespace FridgeManager.Services;

public interface IFoodImageAnalysisService
{
    Task<OperationResult<FoodImageAnalysisResult>> AnalyzeAsync(
        byte[] originalImage,
        string contentType,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);
}
