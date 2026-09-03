using FridgeManager.Data;
using FridgeManager.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Services;

internal static class CapacityQueries
{
    public static async Task<int> ShelfUsageAsync(AppDbContext db, int shelfId, int? excludeItemId = null)
    {
        var query = db.FoodItems.Where(f => f.ShelfId == shelfId && f.Status == FoodStatus.Active);
        if (excludeItemId is int id)
        {
            query = query.Where(f => f.Id != id);
        }

        return await query.SumAsync(f => (int?)f.SizeUnits) ?? 0;
    }

    public static Task<int> UserUsageAsync(AppDbContext db, string userId)
        => db.FoodItems.CountAsync(f => f.OwnerId == userId && f.Status == FoodStatus.Active);
}
