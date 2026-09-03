using FridgeManager.Data;
using FridgeManager.Data.Entities;
using FridgeManager.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Services;

public class InventoryService(IDbContextFactory<AppDbContext> factory) : IInventoryService
{
    public async Task<List<FoodItem>> GetItemsAsync(FoodFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var db = await factory.CreateDbContextAsync();

        return await db.FoodItems
            .AsNoTracking()
            .Include(f => f.Owner)
            .Include(f => f.Shelf)
                .ThenInclude(s => s.Refrigerator)
            .Where(f => filter.Status == null || f.Status == filter.Status)
            .OrderBy(f => f.ExpirationDate)
            .ThenBy(f => f.Name)
            .ToListAsync();
    }
}
