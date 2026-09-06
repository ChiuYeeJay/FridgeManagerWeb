using FridgeManager.Data.Enums;
using FridgeManager.Services;
using FridgeManager.Services.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FridgeManager.Tests;

public sealed class AiSuggestionsTests
{
    [Fact]
    public void ApplyTo_FillsNonNullWhenNoFieldsAreUserEntered()
    {
        var form = new FoodItemForm
        {
            Name = "Manual name",
            Category = FoodCategory.Other,
            ExpirationDate = new DateOnly(2026, 1, 1),
            SizeUnits = 3,
            ShelfId = 4,
            IsShared = true,
            PositionNote = "back left",
            Note = "keep"
        };

        var applied = new FoodImageAnalysisResult(
            "Greek Yogurt",
            FoodCategory.Snack,
            null,
            1,
            "Keep upright",
            []).ApplyTo(form);

        Assert.Equal("Greek Yogurt", form.Name);
        Assert.Equal(FoodCategory.Snack, form.Category);
        Assert.Equal(new DateOnly(2026, 1, 1), form.ExpirationDate);
        Assert.Equal(1, form.SizeUnits);
        Assert.Equal(4, form.ShelfId);
        Assert.True(form.IsShared);
        Assert.Equal("back left", form.PositionNote);
        Assert.Equal("Keep upright", form.Note);
        Assert.Contains(nameof(FoodItemForm.Name), applied);
        Assert.Contains(nameof(FoodItemForm.Category), applied);
        Assert.Contains(nameof(FoodItemForm.SizeUnits), applied);
        Assert.Contains(nameof(FoodItemForm.Note), applied);
        Assert.DoesNotContain(nameof(FoodItemForm.ExpirationDate), applied);
    }

    [Fact]
    public void ApplyTo_DoesNotOverwriteUserEnteredFields()
    {
        var form = new FoodItemForm
        {
            Name = "Manual name",
            Category = FoodCategory.Other,
            ExpirationDate = new DateOnly(2026, 1, 1),
            SizeUnits = 3,
            Note = "keep"
        };
        var userEntered = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(FoodItemForm.Name),
            nameof(FoodItemForm.Category),
            nameof(FoodItemForm.ExpirationDate),
            nameof(FoodItemForm.SizeUnits),
            nameof(FoodItemForm.Note)
        };

        var applied = new FoodImageAnalysisResult(
            "Greek Yogurt",
            FoodCategory.Snack,
            new DateOnly(2026, 6, 15),
            1,
            "Keep upright",
            []).ApplyTo(form, userEntered);

        Assert.Equal("Manual name", form.Name);
        Assert.Equal(FoodCategory.Other, form.Category);
        Assert.Equal(new DateOnly(2026, 1, 1), form.ExpirationDate);
        Assert.Equal(3, form.SizeUnits);
        Assert.Equal("keep", form.Note);
        Assert.Empty(applied);
    }

    [Fact]
    public void ApplyTo_FillsOnlyFieldsTheUserDidNotEnter()
    {
        var form = new FoodItemForm
        {
            Name = "Manual name",
            Category = FoodCategory.Drink,
            ExpirationDate = new DateOnly(2026, 1, 1),
            SizeUnits = 1,
            Note = null
        };
        var userEntered = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(FoodItemForm.Name),
            nameof(FoodItemForm.ExpirationDate)
        };

        var applied = new FoodImageAnalysisResult(
            "Greek Yogurt",
            FoodCategory.Snack,
            new DateOnly(2026, 6, 15),
            2,
            "Keep upright",
            []).ApplyTo(form, userEntered);

        Assert.Equal("Manual name", form.Name);
        Assert.Equal(FoodCategory.Snack, form.Category);
        Assert.Equal(new DateOnly(2026, 1, 1), form.ExpirationDate);
        Assert.Equal(2, form.SizeUnits);
        Assert.Equal("Keep upright", form.Note);
        Assert.DoesNotContain(nameof(FoodItemForm.Name), applied);
        Assert.DoesNotContain(nameof(FoodItemForm.ExpirationDate), applied);
        Assert.Contains(nameof(FoodItemForm.Category), applied);
        Assert.Contains(nameof(FoodItemForm.SizeUnits), applied);
        Assert.Contains(nameof(FoodItemForm.Note), applied);
    }

    [Fact]
    public async Task CreateItemAsync_AiFilledForm_StillEnforcesQuota()
    {
        using var host = new ServiceHost();
        var form = AiFilled(host.Seed.ShelfBId, size: 1, name: "Greek Yogurt");

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Contains("limit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task CreateItemAsync_AiFilledForm_StillEnforcesShelfCapacity()
    {
        using var host = new ServiceHost();
        var form = AiFilled(host.Seed.ShelfAId, size: 3, name: "Oversized tray");

        var result = await host.Inventory.CreateItemAsync(form, Principals.For(host.Seed.BobId));

        Assert.False(result.Success);
        Assert.Contains("Shelf A", result.Error);
        Assert.Contains("0 units remaining", result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task UpdateItemAsync_AiFilledForm_StillEnforcesAuthorization()
    {
        using var host = new ServiceHost();
        var form = AiFilled(host.Seed.ShelfAId, size: 3, name: "Hijacked milk");

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

    private static FoodItemForm AiFilled(int shelfId, int size, string name)
    {
        var form = new FoodItemForm
        {
            Name = "Placeholder",
            Category = FoodCategory.Other,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
            SizeUnits = 2,
            ShelfId = shelfId
        };
        new FoodImageAnalysisResult(name, FoodCategory.Meal, null, size, null, []).ApplyTo(form);
        return form;
    }

    private sealed class ServiceHost : IDisposable
    {
        public SqliteDbFactory Factory { get; } = new();
        public SeedData Seed { get; }
        public InventoryService Inventory { get; }

        public ServiceHost()
        {
            Seed = TestData.Seed(Factory);
            Inventory = new InventoryService(Factory, new FakeImageStorage(), NullLogger<InventoryService>.Instance);
        }

        public void Dispose() => Factory.Dispose();
    }
}
