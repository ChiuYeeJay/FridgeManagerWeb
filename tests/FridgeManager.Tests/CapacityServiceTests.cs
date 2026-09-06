using FridgeManager.Data.Enums;
using FridgeManager.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FridgeManager.Tests;

public sealed class CapacityServiceTests
{
    [Fact]
    public async Task ChangeStatusAsync_ToConsumed_DecreasesShelfUsageBySizeUnits()
    {
        using var host = new ServiceHost();
        var before = await host.Capacity.GetShelfUsageAsync(host.Seed.ShelfAId);
        Assert.Equal(5, before);

        var result = await host.Inventory.ChangeStatusAsync(
            host.Seed.BobsMilkId,
            FoodStatus.Consumed,
            Principals.For(host.Seed.BobId));

        Assert.True(result.Success);
        var after = await host.Capacity.GetShelfUsageAsync(host.Seed.ShelfAId);
        Assert.Equal(2, after);
        Assert.Equal(before - 3, after);
    }

    [Fact]
    public async Task ChangeStatusAsync_ToConsumed_DecreasesUserUsageByOne()
    {
        using var host = new ServiceHost();
        var before = await host.Capacity.GetUserUsageAsync(host.Seed.BobId);
        Assert.Equal(1, before);

        var result = await host.Inventory.ChangeStatusAsync(
            host.Seed.BobsMilkId,
            FoodStatus.Consumed,
            Principals.For(host.Seed.BobId));

        Assert.True(result.Success);
        var after = await host.Capacity.GetUserUsageAsync(host.Seed.BobId);
        Assert.Equal(0, after);
        Assert.Equal(before - 1, after);
    }

    [Fact]
    public async Task GetAllShelfUsageAsync_ReturnsUsedAndRemainingPerShelf()
    {
        using var host = new ServiceHost();

        var shelves = await host.Capacity.GetAllShelfUsageAsync();

        var shelfA = shelves.Single(s => s.ShelfId == host.Seed.ShelfAId);
        Assert.Equal("Shelf A", shelfA.Name);
        Assert.Equal(5, shelfA.CapacityUnits);
        Assert.Equal(5, shelfA.UsedUnits);
        Assert.Equal(0, shelfA.Remaining);

        var shelfB = shelves.Single(s => s.ShelfId == host.Seed.ShelfBId);
        Assert.Equal(3, shelfB.UsedUnits);
        Assert.Equal(17, shelfB.Remaining);
    }

    [Fact]
    public async Task GetShelfRemainingAsync_UnknownShelf_ReturnsZero()
    {
        using var host = new ServiceHost();

        Assert.Equal(0, await host.Capacity.GetShelfRemainingAsync(host.Seed.ShelfAId + 10_000));
    }

    [Fact]
    public async Task GetShelfRemainingAsync_KnownShelves_MatchesCapacityMinusUsage()
    {
        using var host = new ServiceHost();

        Assert.Equal(0, await host.Capacity.GetShelfRemainingAsync(host.Seed.ShelfAId));
        Assert.Equal(17, await host.Capacity.GetShelfRemainingAsync(host.Seed.ShelfBId));
    }

    [Fact]
    public async Task GetDashboardStatsAsync_CountsActiveOnlyAndOmitsDisabledMembers()
    {
        using var host = new ServiceHost();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = await host.Factory.CreateDbContextAsync())
        {
            var milk = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.BobsMilkId);
            milk.ExpirationDate = today.AddDays(-1);
            milk.IsShared = false;

            var juice = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesJuiceId);
            juice.ExpirationDate = today.AddDays(2);

            var bread = await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AlicesBreadId);
            bread.IsShared = true;

            (await db.FoodItems.SingleAsync(f => f.Id == host.Seed.AdminsFillerId)).Status = FoodStatus.Consumed;
            (await db.Users.SingleAsync(u => u.Id == host.Seed.BobId)).IsActive = false;
            await db.SaveChangesAsync();
        }

        var stats = await host.Capacity.GetDashboardStatsAsync();

        Assert.Equal(3, stats.ActiveCount);
        Assert.Equal(1, stats.ExpiringSoonCount);
        Assert.Equal(1, stats.ExpiredCount);
        Assert.Equal(1, stats.SharedCount);
        Assert.Equal(6, stats.UsedUnits);
        Assert.Equal(25, stats.TotalCapacity);
        Assert.Equal(24, stats.UtilisationPercent);
        Assert.Equal([host.Seed.AliceId, host.Seed.AdminId], stats.Members.Select(m => m.UserId).ToArray());
        Assert.DoesNotContain(stats.Members, m => m.UserId == host.Seed.BobId);
        var alice = stats.Members.Single(m => m.UserId == host.Seed.AliceId);
        Assert.Equal("alice", alice.Name);
        Assert.Equal(2, alice.Used);
        Assert.Equal(2, alice.Quota);
        Assert.True(alice.AtLimit);
        var admin = stats.Members.Single(m => m.UserId == host.Seed.AdminId);
        Assert.Equal(0, admin.Used);
        Assert.False(admin.AtLimit);
    }

    private sealed class ServiceHost : IDisposable
    {
        public SqliteDbFactory Factory { get; } = new();
        public SeedData Seed { get; }
        public InventoryService Inventory { get; }
        public CapacityService Capacity { get; }

        public ServiceHost()
        {
            Seed = TestData.Seed(Factory);
            Inventory = new InventoryService(Factory, new FakeImageStorage(), NullLogger<InventoryService>.Instance);
            Capacity = new CapacityService(Factory);
        }

        public void Dispose() => Factory.Dispose();
    }
}
