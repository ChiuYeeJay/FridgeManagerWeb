using FridgeManager.Data;
using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Tests;

public sealed record SeedData(
    string AdminId,
    string AliceId,
    string BobId,
    int ShelfAId,
    int ShelfBId,
    int BobsMilkId,
    int AlicesJuiceId,
    int AlicesBreadId,
    int AdminsFillerId);

public static class TestData
{
    public const string AdminId = "admin-id";
    public const string AliceId = "alice-id";
    public const string BobId = "bob-id";

    public static SeedData Seed(IDbContextFactory<AppDbContext> factory)
    {
        using var db = factory.CreateDbContext();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        db.Users.AddRange(
            User(AdminId, "admin@fridge.local", quota: 10),
            User(AliceId, "alice@fridge.local", quota: 2),
            User(BobId, "bob@fridge.local", quota: 5));

        var fridge = new Refrigerator { Name = "Office Fridge" };
        db.Refrigerators.Add(fridge);
        db.SaveChanges();

        var shelfA = new Shelf { RefrigeratorId = fridge.Id, Name = "Shelf A", CapacityUnits = 5, SortOrder = 1 };
        var shelfB = new Shelf { RefrigeratorId = fridge.Id, Name = "Shelf B", CapacityUnits = 20, SortOrder = 2 };
        db.Shelves.AddRange(shelfA, shelfB);
        db.SaveChanges();

        var bobsMilk = Item("Milk", BobId, FoodCategory.Drink, today.AddDays(10), 3, shelfA.Id, now);
        var alicesJuice = Item("Juice", AliceId, FoodCategory.Drink, today.AddDays(7), 2, shelfA.Id, now);
        var alicesBread = Item("Bread", AliceId, FoodCategory.Snack, today.AddDays(4), 1, shelfB.Id, now);
        var adminsFiller = Item("Baking soda", AdminId, FoodCategory.Other, today.AddDays(100), 2, shelfB.Id, now);
        db.FoodItems.AddRange(bobsMilk, alicesJuice, alicesBread, adminsFiller);
        db.SaveChanges();

        return new SeedData(
            AdminId,
            AliceId,
            BobId,
            shelfA.Id,
            shelfB.Id,
            bobsMilk.Id,
            alicesJuice.Id,
            alicesBread.Id,
            adminsFiller.Id);
    }

    private static ApplicationUser User(string id, string email, int quota) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        ItemQuota = quota,
        IsActive = true,
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static FoodItem Item(
        string name,
        string ownerId,
        FoodCategory category,
        DateOnly expiration,
        int size,
        int shelfId,
        DateTime now)
        => new()
        {
            Name = name,
            OwnerId = ownerId,
            Category = category,
            ExpirationDate = expiration,
            SizeUnits = size,
            IsShared = false,
            Status = FoodStatus.Active,
            ShelfId = shelfId,
            CreatedAt = now,
            UpdatedAt = now
        };
}
