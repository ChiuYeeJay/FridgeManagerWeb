using FridgeManager.Data.Enums;
using FridgeManager.Services;
using FridgeManager.Services.Models;
using Microsoft.EntityFrameworkCore;

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
        public SeedData Seed { get; }
        public InventoryService Inventory { get; }

        public ServiceHost()
        {
            Seed = TestData.Seed(Factory);
            Inventory = new InventoryService(Factory);
        }

        public void Dispose() => Factory.Dispose();
    }
}
