using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;

namespace FridgeManager.Services.Models;

public sealed class FridgeOverview
{
    public int ActiveCount { get; init; }
    public int ExpiringSoonCount { get; init; }
    public int ExpiredCount { get; init; }
    public int SharedCount { get; init; }
    public int UsedUnits { get; init; }
    public int TotalCapacity { get; init; }
    public int UtilisationPercent { get; init; }
    public string RefrigeratorName { get; init; } = "Refrigerator";
    public IReadOnlyList<ShelfBand> Shelves { get; init; } = [];
    public IReadOnlyList<MemberUsage> Members { get; init; } = [];

    public static FridgeOverview From(IReadOnlyList<FoodItem> items, DateOnly today)
    {
        var active = items.Where(i => i.Status == FoodStatus.Active).ToList();
        var shelves = active
            .Select(i => i.Shelf)
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.SortOrder)
            .ToList();

        var bands = shelves.Select(shelf =>
        {
            var onShelf = active.Where(i => i.ShelfId == shelf.Id).ToList();
            var used = onShelf.Sum(i => i.SizeUnits);
            var chips = onShelf
                .OrderBy(i => i.ExpirationDate)
                .Select(i => new ShelfChip(
                    i.Name,
                    ChipSubtitle(i, today),
                    i.SizeUnits,
                    ExpiryRules.Of(i.ExpirationDate, today) == ExpiryState.Expired))
                .ToList();
            return new ShelfBand(shelf.Name, used, shelf.CapacityUnits, chips);
        }).ToList();

        var members = active
            .GroupBy(i => i.OwnerId)
            .Select(g =>
            {
                var owner = g.First().Owner;
                var used = g.Count();
                return new MemberUsage(FoodDisplay.OwnerLabel(owner), used, owner.ItemQuota, used >= owner.ItemQuota);
            })
            .OrderByDescending(m => m.AtLimit)
            .ThenBy(m => m.Name)
            .ToList();

        var usedUnits = active.Sum(i => i.SizeUnits);
        var totalCapacity = shelves.Sum(s => s.CapacityUnits);

        return new FridgeOverview
        {
            ActiveCount = active.Count,
            ExpiringSoonCount = active.Count(i => ExpiryRules.Of(i.ExpirationDate, today) == ExpiryState.ExpiringSoon),
            ExpiredCount = active.Count(i => ExpiryRules.Of(i.ExpirationDate, today) == ExpiryState.Expired),
            SharedCount = active.Count(i => i.IsShared),
            UsedUnits = usedUnits,
            TotalCapacity = totalCapacity,
            UtilisationPercent = totalCapacity == 0 ? 0 : (int)Math.Round(100.0 * usedUnits / totalCapacity),
            RefrigeratorName = shelves.FirstOrDefault()?.Refrigerator?.Name ?? "Refrigerator",
            Shelves = bands,
            Members = members
        };
    }

    private static string ChipSubtitle(FoodItem item, DateOnly today)
    {
        var state = ExpiryRules.Of(item.ExpirationDate, today);
        if (state == ExpiryState.Expired)
        {
            return $"expired {FoodDisplay.DateLabel(item.ExpirationDate)}";
        }

        if (state == ExpiryState.ExpiringSoon)
        {
            return FoodDisplay.DateLabel(item.ExpirationDate);
        }

        return FoodDisplay.OwnerLabel(item.Owner);
    }
}

public sealed record ShelfBand(string Name, int Used, int Capacity, IReadOnlyList<ShelfChip> Chips)
{
    public int Remaining => Math.Max(0, Capacity - Used);
}

public sealed record ShelfChip(string Name, string Subtitle, int SizeUnits, bool Expired);

public sealed record MemberUsage(string Name, int Used, int Quota, bool AtLimit);
