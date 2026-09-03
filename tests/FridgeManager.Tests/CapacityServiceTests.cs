using FridgeManager.Data.Enums;
using FridgeManager.Services;

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

    private sealed class ServiceHost : IDisposable
    {
        public SqliteDbFactory Factory { get; } = new();
        public SeedData Seed { get; }
        public InventoryService Inventory { get; }
        public CapacityService Capacity { get; }

        public ServiceHost()
        {
            Seed = TestData.Seed(Factory);
            Inventory = new InventoryService(Factory);
            Capacity = new CapacityService(Factory);
        }

        public void Dispose() => Factory.Dispose();
    }
}
