using System.Security.Claims;
using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;
using Microsoft.AspNetCore.Components.Forms;

namespace FridgeManager.Services;

public interface IInventoryService
{
    Task<List<FoodItem>> GetItemsAsync(FoodFilter filter);
    Task<FoodItem?> GetItemAsync(int id);
    Task<OperationResult<FoodItem>> CreateItemAsync(FoodItemForm form, ClaimsPrincipal user);
    Task<OperationResult> UpdateItemAsync(int id, FoodItemForm form, ClaimsPrincipal user);
    Task<OperationResult> ChangeStatusAsync(int id, FoodStatus status, ClaimsPrincipal user);
    Task<OperationResult<string>> SaveImageAsync(IBrowserFile file, ClaimsPrincipal user);
    Task DeleteImageAsync(string? imagePath);
}
