using System.Security.Claims;
using FridgeManager.Data;
using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Services;

public class InventoryService(IDbContextFactory<AppDbContext> factory) : IInventoryService
{
    public async Task<List<FoodItem>> GetItemsAsync(FoodFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var db = await factory.CreateDbContextAsync();

        var query = db.FoodItems
            .AsNoTracking()
            .Include(f => f.Owner)
            .Include(f => f.Shelf)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(f => f.Name.ToLower().Contains(term));
        }

        if (filter.MineOnly)
        {
            if (string.IsNullOrEmpty(filter.CurrentUserId))
            {
                return [];
            }

            query = query.Where(f => f.OwnerId == filter.CurrentUserId);
        }

        if (filter.SharedOnly)
        {
            query = query.Where(f => f.IsShared);
        }

        if (filter.Category is FoodCategory category)
        {
            query = query.Where(f => f.Category == category);
        }

        if (filter.ShelfId is int shelfId)
        {
            query = query.Where(f => f.ShelfId == shelfId);
        }

        if (filter.Status is FoodStatus status)
        {
            query = query.Where(f => f.Status == status);
        }

        if (filter.ExpiringSoon || filter.Expired)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var soon = today.AddDays(3);

            if (filter.ExpiringSoon && filter.Expired)
            {
                query = query.Where(f => f.ExpirationDate <= soon);
            }
            else if (filter.ExpiringSoon)
            {
                query = query.Where(f => f.ExpirationDate >= today && f.ExpirationDate <= soon);
            }
            else
            {
                query = query.Where(f => f.ExpirationDate < today);
            }
        }

        return await query
            .OrderBy(f => f.ExpirationDate)
            .ThenBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<FoodItem?> GetItemAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.FoodItems
            .AsNoTracking()
            .Include(f => f.Owner)
            .Include(f => f.Shelf)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<OperationResult<FoodItem>> CreateItemAsync(FoodItemForm form, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(user);

        var ownerId = UserClaims.GetUserId(user);
        if (string.IsNullOrEmpty(ownerId))
        {
            return OperationResult<FoodItem>.Fail("You must be signed in to add an item.");
        }

        var validation = ValidateForm(form);
        if (validation is not null)
        {
            return OperationResult<FoodItem>.Fail(validation);
        }

        await using var db = await factory.CreateDbContextAsync();

        var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == ownerId);
        if (owner is null)
        {
            return OperationResult<FoodItem>.Fail("You must be signed in to add an item.");
        }

        var shelf = await db.Shelves.FirstOrDefaultAsync(s => s.Id == form.ShelfId);
        if (shelf is null)
        {
            return OperationResult<FoodItem>.Fail("Shelf not found.");
        }

        var usedSlots = await CapacityQueries.UserUsageAsync(db, owner.Id);
        if (usedSlots >= owner.ItemQuota)
        {
            return OperationResult<FoodItem>.Fail($"You have reached your limit of {owner.ItemQuota} active items.");
        }

        var usedUnits = await CapacityQueries.ShelfUsageAsync(db, shelf.Id);
        var remaining = shelf.CapacityUnits - usedUnits;
        if (remaining < form.SizeUnits)
        {
            return OperationResult<FoodItem>.Fail(
                $"{shelf.Name} has {Math.Max(0, remaining)} units remaining; this item requires {form.SizeUnits} units.");
        }

        var now = DateTime.UtcNow;
        var item = new FoodItem
        {
            OwnerId = owner.Id,
            Status = FoodStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        form.ApplyTo(item);

        db.FoodItems.Add(item);
        await db.SaveChangesAsync();

        await db.Entry(item).Reference(i => i.Owner).LoadAsync();
        await db.Entry(item).Reference(i => i.Shelf).LoadAsync();
        return OperationResult<FoodItem>.Ok(item);
    }

    public async Task<OperationResult> UpdateItemAsync(int id, FoodItemForm form, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(user);

        var validation = ValidateForm(form);
        if (validation is not null)
        {
            return OperationResult.Fail(validation);
        }

        await using var db = await factory.CreateDbContextAsync();
        var item = await db.FoodItems.Include(f => f.Shelf).FirstOrDefaultAsync(f => f.Id == id);
        if (item is null)
        {
            return OperationResult.Fail("Item not found.");
        }

        if (!UserClaims.CanModify(user, item))
        {
            return OperationResult.Fail("You can only edit your own items.");
        }

        var shelfOrSizeChanged = item.ShelfId != form.ShelfId || item.SizeUnits != form.SizeUnits;
        if (item.Status == FoodStatus.Active && shelfOrSizeChanged)
        {
            var shelf = await db.Shelves.FirstOrDefaultAsync(s => s.Id == form.ShelfId);
            if (shelf is null)
            {
                return OperationResult.Fail("Shelf not found.");
            }

            var usedUnits = await CapacityQueries.ShelfUsageAsync(db, shelf.Id, excludeItemId: item.Id);
            var remaining = shelf.CapacityUnits - usedUnits;
            if (remaining < form.SizeUnits)
            {
                return OperationResult.Fail(
                    $"{shelf.Name} has {Math.Max(0, remaining)} units remaining; this item requires {form.SizeUnits} units.");
            }
        }

        form.ApplyTo(item);
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public async Task<OperationResult> ChangeStatusAsync(int id, FoodStatus status, ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var db = await factory.CreateDbContextAsync();
        var item = await db.FoodItems.FirstOrDefaultAsync(f => f.Id == id);
        if (item is null)
        {
            return OperationResult.Fail("Item not found.");
        }

        if (!UserClaims.CanModify(user, item))
        {
            return OperationResult.Fail("You can only change the status of your own items.");
        }

        if (item.Status == status)
        {
            return OperationResult.Ok();
        }

        if (status == FoodStatus.Active && item.Status != FoodStatus.Active)
        {
            return OperationResult.Fail("An item that has left the fridge cannot be reactivated.");
        }

        item.Status = status;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    private static string? ValidateForm(FoodItemForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Name))
        {
            return "Name is required.";
        }

        if (form.SizeUnits is < 1 or > 3)
        {
            return "Size must be Small (1), Medium (2), or Large (3).";
        }

        if (form.ShelfId < 1)
        {
            return "Choose a shelf.";
        }

        return null;
    }
}
