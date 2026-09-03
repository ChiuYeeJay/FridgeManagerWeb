using FridgeManager.Data;
using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Services;

public class CapacityService(IDbContextFactory<AppDbContext> factory) : ICapacityService
{
    public async Task<int> GetShelfUsageAsync(int shelfId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await CapacityQueries.ShelfUsageAsync(db, shelfId);
    }

    public async Task<int> GetShelfRemainingAsync(int shelfId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var shelf = await db.Shelves.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shelfId);
        if (shelf is null)
        {
            return 0;
        }

        var used = await CapacityQueries.ShelfUsageAsync(db, shelfId);
        return Math.Max(0, shelf.CapacityUnits - used);
    }

    public async Task<int> GetUserUsageAsync(string userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await CapacityQueries.UserUsageAsync(db, userId);
    }

    public async Task<List<ShelfUsageDto>> GetAllShelfUsageAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        var shelves = await db.Shelves
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var usedByShelf = await db.FoodItems
            .AsNoTracking()
            .Where(f => f.Status == FoodStatus.Active)
            .GroupBy(f => f.ShelfId)
            .Select(g => new { ShelfId = g.Key, Used = g.Sum(f => f.SizeUnits) })
            .ToDictionaryAsync(x => x.ShelfId, x => x.Used);

        return shelves
            .Select(s => new ShelfUsageDto(
                s.Id,
                s.Name,
                s.SortOrder,
                s.CapacityUnits,
                usedByShelf.GetValueOrDefault(s.Id)))
            .ToList();
    }

    public async Task<List<UserUsageDto>> GetAllUserUsageAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var usedByOwner = await db.FoodItems
            .AsNoTracking()
            .Where(f => f.Status == FoodStatus.Active)
            .GroupBy(f => f.OwnerId)
            .Select(g => new { OwnerId = g.Key, Used = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Used);

        return users
            .Select(u => new UserUsageDto(
                u.Id,
                FoodDisplay.OwnerLabel(u),
                usedByOwner.GetValueOrDefault(u.Id),
                u.ItemQuota,
                u.IsActive))
            .ToList();
    }

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();

        var shelves = await db.Shelves
            .AsNoTracking()
            .Include(s => s.Refrigerator)
            .Include(s => s.Items.Where(i => i.Status == FoodStatus.Active))
                .ThenInclude(i => i.Owner)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return DashboardStats.From(shelves, users, today);
    }
}
