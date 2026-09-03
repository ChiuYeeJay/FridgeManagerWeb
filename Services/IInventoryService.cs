using FridgeManager.Data.Entities;
using FridgeManager.Services.Models;

namespace FridgeManager.Services;

public interface IInventoryService
{
    Task<List<FoodItem>> GetItemsAsync(FoodFilter filter);
}
