using FridgeManager.Data.Enums;
using FridgeManager.Services.Models;

namespace FridgeManager.Tests;

public sealed class FoodFilterTests
{
    [Fact]
    public void ToQuery_OmitsDefaultValues()
        => Assert.Equal("", new FoodFilter().ToQuery());

    [Fact]
    public void ToPath_WithNoFilters_IsBareFood()
        => Assert.Equal("food", new FoodFilter().ToPath());

    [Fact]
    public void ToQuery_IncludesOnlySetFilters()
    {
        var filter = new FoodFilter
        {
            Search = "kombucha",
            MineOnly = true,
            SharedOnly = true,
            Category = FoodCategory.Drink,
            ShelfId = 2,
            Status = FoodStatus.Consumed,
            Expiry = ExpiryState.ExpiringSoon,
            Sort = FoodSort.Name,
            SortDescending = true
        };

        Assert.Equal(
            "q=kombucha&mine=1&shared=1&category=Drink&shelf=2&status=Consumed&expiry=ExpiringSoon&sort=Name&dir=desc",
            filter.ToQuery());
        Assert.StartsWith("food?", filter.ToPath());
    }

    [Fact]
    public void FromQuery_ParsesFlagsAndEnums()
    {
        var filter = FoodFilter.FromQuery(
            q: " oat ",
            mine: "1",
            shared: "true",
            category: "Meal",
            shelf: "4",
            status: "Missing",
            expiry: "Expired",
            sort: "Created",
            dir: "asc",
            currentUserId: "alice-id");

        Assert.Equal("oat", filter.Search);
        Assert.True(filter.MineOnly);
        Assert.True(filter.SharedOnly);
        Assert.Equal(FoodCategory.Meal, filter.Category);
        Assert.Equal(4, filter.ShelfId);
        Assert.Equal(FoodStatus.Missing, filter.Status);
        Assert.Equal(ExpiryState.Expired, filter.Expiry);
        Assert.Equal(FoodSort.Created, filter.Sort);
        Assert.False(filter.SortDescending);
        Assert.False(filter.EffectiveDescending);
        Assert.Equal("alice-id", filter.CurrentUserId);
    }

    [Fact]
    public void FromQuery_UnknownValues_FallBackToDefaults()
    {
        var filter = FoodFilter.FromQuery(category: "Nope", status: "nope", sort: "nope", shelf: "x", expiry: "nope");

        Assert.Null(filter.Category);
        Assert.Equal(FoodStatus.Active, filter.Status);
        Assert.Equal(FoodSort.Expiry, filter.Sort);
        Assert.Null(filter.ShelfId);
        Assert.Null(filter.Expiry);
    }

    [Fact]
    public void ToQuery_EscapesSearchText()
    {
        var filter = new FoodFilter { Search = "oat milk" };
        Assert.Equal("q=oat%20milk", filter.ToQuery());
    }

    [Fact]
    public void ToQuery_OmitsDir_WhenItMatchesFieldDefault()
    {
        Assert.Equal("sort=Created", new FoodFilter { Sort = FoodSort.Created }.ToQuery());
        Assert.Equal("dir=desc", new FoodFilter { SortDescending = true }.ToQuery());
    }

    [Fact]
    public void ClearFilters_KeepsMineAndSort()
    {
        var filter = new FoodFilter
        {
            Search = "kombucha",
            MineOnly = true,
            SharedOnly = true,
            Category = FoodCategory.Drink,
            ShelfId = 2,
            Status = FoodStatus.Consumed,
            Expiry = ExpiryState.Expired,
            Sort = FoodSort.Owner,
            SortDescending = true
        };

        Assert.True(filter.HasClearableFilters);
        filter.ClearFilters();

        Assert.False(filter.HasClearableFilters);
        Assert.True(filter.MineOnly);
        Assert.Equal(FoodSort.Owner, filter.Sort);
        Assert.True(filter.SortDescending);
        Assert.Equal(FoodStatus.Active, filter.Status);
    }
}
