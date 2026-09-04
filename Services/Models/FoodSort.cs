namespace FridgeManager.Services.Models;

public enum FoodSort
{
    Expiry,
    Created,
    Updated,
    Name,
    Category,
    Owner
}

public static class FoodSortRules
{
    public static bool DefaultDescending(FoodSort sort)
        => sort is FoodSort.Created or FoodSort.Updated;
}
