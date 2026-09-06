using FridgeManager.Data.Enums;
using FridgeManager.Services;
using FridgeManager.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FridgeManager.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task CreateItemAsync_WhenUserIsAtQuota_FailsAndNamesQuota()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfBId, size: 1, name: "Extra snack");

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Contains("2", result.Error);
        Assert.Contains("limit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task CreateItemAsync_WhenShelfLacksCapacity_FailsAndNamesRemainingUnits()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfAId, size: 3, name: "Oversized tray");

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.BobId));

        Assert.False(result.Success);
        Assert.Contains("Shelf A", result.Error);
        Assert.Contains("0 units remaining", result.Error);
        Assert.Contains("requires 3 units", result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task CreateItemAsync_WhenQuotaAndCapacityPass_PersistsActiveItem()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfBId, size: 2, name: "Cold brew");

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.BobId));

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        Assert.Equal(FoodStatus.Active, result.Value.Status);
        Assert.Equal("Cold brew", result.Value.Name);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Name == "Cold brew");
        Assert.Equal(FoodStatus.Active, stored.Status);
        Assert.Equal(host.Seed.BobId, stored.OwnerId);
        Assert.Equal(2, stored.SizeUnits);
    }

    [Fact]
    public async Task UpdateItemAsync_ByNonOwnerNonAdmin_FailsForbidden()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfAId, size: 3, name: "Hijacked milk");

        var result = await host.Inventory.UpdateItemAsync(
            host.Seed.BobsMilkId,
            form,
            Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Contains("own items", result.Error, StringComparison.OrdinalIgnoreCase);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
        Assert.Equal("Milk", stored.Name);
    }

    [Fact]
    public async Task UpdateItemAsync_ByAdminOnAnothersItem_Succeeds()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfAId, size: 3, name: "Relabeled milk");

        var result = await host.Inventory.UpdateItemAsync(
            host.Seed.BobsMilkId,
            form,
            Principals.For(host.Seed.AdminId, admin: true));

        Assert.True(result.Success);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
        Assert.Equal("Relabeled milk", stored.Name);
        Assert.Equal(host.Seed.BobId, stored.OwnerId);
    }

    [Fact]
    public async Task ChangeStatusAsync_ByNonOwnerNonAdmin_FailsForbidden()
    {
        using var host = new ServiceHost();

        var result = await host.Inventory.ChangeStatusAsync(
            host.Seed.BobsMilkId,
            FoodStatus.Consumed,
            Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Contains("own items", result.Error, StringComparison.OrdinalIgnoreCase);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
        Assert.Equal(FoodStatus.Active, stored.Status);
    }

    [Fact]
    public async Task UpdateItemAsync_WhenShelfIsFull_AllowsSameItemToKeepItsUnits()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfAId, size: 3, name: "Milk");

        var result = await host.Inventory.UpdateItemAsync(
            host.Seed.BobsMilkId,
            form,
            Principals.For(host.Seed.BobId));

        Assert.True(result.Success);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
        Assert.Equal(3, stored.SizeUnits);
        Assert.Equal(host.Seed.ShelfAId, stored.ShelfId);
    }

    [Fact]
    public async Task GetItemsAsync_DefaultSort_OrdersByExpirationThenName()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter());

        Assert.Equal(["Bread", "Juice", "Milk", "Baking soda"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_SortByName_OrdersAlphabetically()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Sort = FoodSort.Name });

        Assert.Equal(["Baking soda", "Bread", "Juice", "Milk"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_SortByCategory_OrdersCategoryThenName()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Sort = FoodSort.Category });

        Assert.Equal(["Juice", "Milk", "Baking soda", "Bread"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_SortByOwner_OrdersUserNameThenName()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Sort = FoodSort.Owner });

        Assert.Equal(["Baking soda", "Bread", "Juice", "Milk"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_SortByNameDescending_ReversesAlphabet()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter
        {
            Sort = FoodSort.Name,
            SortDescending = true
        });

        Assert.Equal(["Milk", "Juice", "Bread", "Baking soda"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_SortByCreated_OrdersNewestFirst()
    {
        using var host = new ServiceHost();
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesBreadId)).CreatedAt = now.AddHours(-4);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesJuiceId)).CreatedAt = now.AddHours(-3);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId)).CreatedAt = now.AddHours(-1);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AdminsFillerId)).CreatedAt = now.AddHours(-2);
            await db.SaveChangesAsync();
        }

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Sort = FoodSort.Created });

        Assert.Equal(["Milk", "Baking soda", "Juice", "Bread"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_SortByUpdated_OrdersNewestFirst()
    {
        using var host = new ServiceHost();
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            var now = DateTime.UtcNow;
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesBreadId)).UpdatedAt = now.AddHours(-1);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesJuiceId)).UpdatedAt = now.AddHours(-4);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId)).UpdatedAt = now.AddHours(-2);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AdminsFillerId)).UpdatedAt = now.AddHours(-3);
            await db.SaveChangesAsync();
        }

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Sort = FoodSort.Updated });

        Assert.Equal(["Bread", "Milk", "Baking soda", "Juice"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_ExpiryExpired_ReturnsOnlyPastDates()
    {
        using var host = new ServiceHost();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesBreadId)).ExpirationDate = today.AddDays(-1);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesJuiceId)).ExpirationDate = today.AddDays(2);
            await db.SaveChangesAsync();
        }

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Expiry = ExpiryState.Expired });

        Assert.Equal(["Bread"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_ExpiryExpiringSoon_ReturnsTodayThroughPlusThree()
    {
        using var host = new ServiceHost();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesBreadId)).ExpirationDate = today.AddDays(-1);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesJuiceId)).ExpirationDate = today.AddDays(2);
            await db.SaveChangesAsync();
        }

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Expiry = ExpiryState.ExpiringSoon });

        Assert.Equal(["Juice"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_ExpiryNormal_ReturnsBeyondSoonWindow()
    {
        using var host = new ServiceHost();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesBreadId)).ExpirationDate = today.AddDays(-1);
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesJuiceId)).ExpirationDate = today.AddDays(2);
            await db.SaveChangesAsync();
        }

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Expiry = ExpiryState.Normal });

        Assert.Equal(["Milk", "Baking soda"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_Search_MatchesNameCaseInsensitively()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Search = "iLk" });

        Assert.Equal(["Milk"], items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetItemsAsync_MineOnly_ReturnsCurrentUsersItems()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter
        {
            MineOnly = true,
            CurrentUserId = host.Seed.AliceId
        });

        Assert.Equal(["Bread", "Juice"], items.Select(i => i.Name).ToArray());
        Assert.All(items, i => Assert.Equal(host.Seed.AliceId, i.OwnerId));
    }

    [Fact]
    public async Task GetItemsAsync_SharedOnly_ReturnsSharedItems()
    {
        using var host = new ServiceHost();
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId)).IsShared = true;
            await db.SaveChangesAsync();
        }

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { SharedOnly = true });

        Assert.Equal(["Milk"], items.Select(i => i.Name).ToArray());
        Assert.True(items[0].IsShared);
    }

    [Fact]
    public async Task GetItemsAsync_Category_ReturnsMatchingCategory()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Category = FoodCategory.Drink });

        Assert.Equal(["Juice", "Milk"], items.Select(i => i.Name).ToArray());
        Assert.All(items, i => Assert.Equal(FoodCategory.Drink, i.Category));
    }

    [Fact]
    public async Task GetItemsAsync_ShelfId_ReturnsItemsOnThatShelf()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { ShelfId = host.Seed.ShelfAId });

        Assert.Equal(["Juice", "Milk"], items.Select(i => i.Name).ToArray());
        Assert.All(items, i => Assert.Equal(host.Seed.ShelfAId, i.ShelfId));
    }

    [Fact]
    public async Task GetItemsAsync_StatusConsumed_ReturnsOnlyConsumed()
    {
        using var host = new ServiceHost();
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId)).Status = FoodStatus.Consumed;
            await db.SaveChangesAsync();
        }

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { Status = FoodStatus.Consumed });

        Assert.Equal(["Milk"], items.Select(i => i.Name).ToArray());
        Assert.Equal(FoodStatus.Consumed, items[0].Status);
    }

    [Fact]
    public async Task GetItemsAsync_MineOnlyWithoutCurrentUserId_ReturnsEmpty()
    {
        using var host = new ServiceHost();

        var items = await host.Inventory.GetItemsAsync(new FoodFilter { MineOnly = true });

        Assert.Empty(items);
    }

    [Fact]
    public async Task UpdateItemAsync_WhenIncreasingSizeBeyondRemaining_FailsAndNamesUnits()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfAId, size: 3, name: "Juice");

        var result = await host.Inventory.UpdateItemAsync(
            host.Seed.AlicesJuiceId,
            form,
            Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Contains("Shelf A", result.Error);
        Assert.Contains("2 units remaining", result.Error);
        Assert.Contains("requires 3 units", result.Error);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesJuiceId);
        Assert.Equal(2, stored.SizeUnits);
        Assert.Equal("Juice", stored.Name);
    }

    [Fact]
    public async Task UpdateItemAsync_WhenMovingToFullShelf_FailsAndNamesUnits()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfAId, size: 1, name: "Bread");

        var result = await host.Inventory.UpdateItemAsync(
            host.Seed.AlicesBreadId,
            form,
            Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Contains("Shelf A", result.Error);
        Assert.Contains("0 units remaining", result.Error);
        Assert.Contains("requires 1 units", result.Error);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesBreadId);
        Assert.Equal(host.Seed.ShelfBId, stored.ShelfId);
    }

    [Fact]
    public async Task ChangeStatusAsync_ReactivateNonActive_Fails()
    {
        using var host = new ServiceHost();
        var consumed = await host.Inventory.ChangeStatusAsync(
            host.Seed.BobsMilkId,
            FoodStatus.Consumed,
            Principals.For(host.Seed.BobId));
        Assert.True(consumed.Success);

        var result = await host.Inventory.ChangeStatusAsync(
            host.Seed.BobsMilkId,
            FoodStatus.Active,
            Principals.For(host.Seed.BobId));

        Assert.False(result.Success);
        Assert.Contains("reactivated", result.Error, StringComparison.OrdinalIgnoreCase);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
        Assert.Equal(FoodStatus.Consumed, stored.Status);
    }

    [Fact]
    public async Task ChangeStatusAsync_ByAdminOnAnothersItem_Succeeds()
    {
        using var host = new ServiceHost();

        var result = await host.Inventory.ChangeStatusAsync(
            host.Seed.BobsMilkId,
            FoodStatus.Missing,
            Principals.For(host.Seed.AdminId, admin: true));

        Assert.True(result.Success);

        await using var db = await host.Factory.CreateDbContextAsync();
        var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
        Assert.Equal(FoodStatus.Missing, stored.Status);
        Assert.Equal(host.Seed.BobId, stored.OwnerId);
    }

    [Fact]
    public async Task CreateItemAsync_WhenUnsignedIn_Fails()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfBId, size: 1, name: "Ghost snack");

        var result = await host.Inventory.CreateItemAsync(form, new System.Security.Claims.ClaimsPrincipal());

        Assert.False(result.Success);
        Assert.Contains("signed in", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Value);

        await using var db = await host.Factory.CreateDbContextAsync();
        Assert.False(await db.FoodItems.AnyAsync(f => f.Name == "Ghost snack"));
    }

    [Fact]
    public async Task CreateItemAsync_WhenShelfMissing_Fails()
    {
        using var host = new ServiceHost();
        var form = ValidForm(shelfId: 9999, size: 1, name: "Lost tray");

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.BobId));

        Assert.False(result.Success);
        Assert.Contains("Shelf not found", result.Error);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task CreateItemAsync_WhenSizeIsNotOneToThree_Fails(int size)
    {
        using var host = new ServiceHost();
        var name = $"Bad size {size}";
        var form = ValidForm(host.Seed.ShelfBId, size, name);

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.BobId));

        Assert.False(result.Success);
        Assert.Contains("Size must be Small", result.Error);
        Assert.Null(result.Value);

        await using var db = await host.Factory.CreateDbContextAsync();
        Assert.False(await db.FoodItems.AnyAsync(f => f.Name == name));
    }

    [Fact]
    public async Task CreateItemAsync_WhenNameExceeds200Characters_Fails()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfBId, size: 1, name: new string('a', 201));

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.BobId));

        Assert.False(result.Success);
        Assert.Contains("200", result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task CreateItemAsync_WhenImagePathIsNotAnUpload_Fails()
    {
        using var host = new ServiceHost();
        var form = ValidForm(host.Seed.ShelfBId, size: 1, name: "Tracked juice");
        form.ImagePath = "javascript:alert(1)";

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.BobId));

        Assert.False(result.Success);
        Assert.Contains("photo", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task UpdateItemAsync_WhenImageKeyChanges_DeletesPreviousObject()
    {
        using var host = new ServiceHost();
        const string oldKey = "food-images/2026/09/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp";
        const string newKey = "food-images/2026/09/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp";
        host.Storage.Seed(oldKey, [1]);
        host.Storage.Seed(newKey, [2]);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            var item = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
            item.ImagePath = oldKey;
            await db.SaveChangesAsync();
        }

        var form = ValidForm(host.Seed.ShelfAId, size: 3, name: "Milk");
        form.ImagePath = newKey;

        var result = await host.Inventory.UpdateItemAsync(
            host.Seed.BobsMilkId,
            form,
            Principals.For(host.Seed.BobId));

        Assert.True(result.Success);
        Assert.Contains(oldKey, host.Storage.Deleted);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
            Assert.Equal(newKey, stored.ImagePath);
        }
    }

    [Fact]
    public async Task UpdateItemAsync_WhenForbidden_DoesNotDeletePreviousObject()
    {
        using var host = new ServiceHost();
        const string oldKey = "food-images/2026/09/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp";
        const string newKey = "food-images/2026/09/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp";
        host.Storage.Seed(oldKey, [1]);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            var item = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
            item.ImagePath = oldKey;
            await db.SaveChangesAsync();
        }

        var form = ValidForm(host.Seed.ShelfAId, size: 3, name: "Hijacked milk");
        form.ImagePath = newKey;

        var result = await host.Inventory.UpdateItemAsync(
            host.Seed.BobsMilkId,
            form,
            Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Empty(host.Storage.Deleted);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            var stored = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
            Assert.Equal(oldKey, stored.ImagePath);
        }
    }

    private static FoodItemForm ValidForm(int shelfId, int size, string name) => new()
    {
        Name = name,
        Category = FoodCategory.Drink,
        ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
        SizeUnits = size,
        ShelfId = shelfId
    };

    private sealed class ServiceHost : IDisposable
    {
        public SqliteDbFactory Factory { get; } = new();
        public FakeImageStorage Storage { get; } = new();
        public SeedData Seed { get; }
        public InventoryService Inventory { get; }

        public ServiceHost()
        {
            Seed = TestData.Seed(Factory);
            Inventory = new InventoryService(Factory, Storage, NullLogger<InventoryService>.Instance);
        }

        public void Dispose() => Factory.Dispose();
    }
}
