using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;

namespace FridgeManager.Services.Models;

public sealed class DashboardStats
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

    public static DashboardStats From(
        IReadOnlyList<Shelf> shelves,
        IReadOnlyList<ApplicationUser> users,
        DateOnly today)
    {
        var bands = shelves.Select(shelf =>
        {
            var onShelf = shelf.Items;
            var used = onShelf.Sum(i => i.SizeUnits);
            var chips = onShelf
                .OrderBy(i => i.ExpirationDate)
                .Select(i =>
                {
                    var state = ExpiryRules.Of(i.ExpirationDate, today);
                    return new ShelfChip(
                        i.Id,
                        i.Name,
                        FoodDisplay.OwnerLabel(i.Owner),
                        i.SizeUnits,
                        state == ExpiryState.Expired,
                        state == ExpiryState.ExpiringSoon,
                        $"{FoodDisplay.DateLabel(i.ExpirationDate)} ({FoodDisplay.RelativeDays(i.ExpirationDate, today)})");
                })
                .ToList();
            return new ShelfBand(shelf.Id, shelf.Name, used, shelf.CapacityUnits, chips);
        }).ToList();

        var usageByOwner = shelves
            .SelectMany(s => s.Items)
            .GroupBy(i => i.OwnerId)
            .ToDictionary(g => g.Key, g => g.Count());

        var members = users
            .Select(user =>
            {
                var used = usageByOwner.GetValueOrDefault(user.Id);
                return new MemberUsage(
                    user.Id,
                    FoodDisplay.OwnerLabel(user),
                    used,
                    user.ItemQuota,
                    used >= user.ItemQuota);
            })
            .OrderByDescending(m => m.AtLimit)
            .ThenBy(m => m.Name)
            .ToList();

        var usedUnits = bands.Sum(b => b.Used);
        var totalCapacity = bands.Sum(b => b.Capacity);
        var active = shelves.SelectMany(s => s.Items).ToList();

        return new DashboardStats
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
}

public sealed record ShelfBand(int ShelfId, string Name, int Used, int Capacity, IReadOnlyList<ShelfChip> Chips)
{
    public int Remaining => Math.Max(0, Capacity - Used);
}

public sealed record ShelfChip(
    int Id,
    string Name,
    string Owner,
    int SizeUnits,
    bool Expired,
    bool ExpiringSoon,
    string ExpirationLabel);

public sealed record MemberUsage(string UserId, string Name, int Used, int Quota, bool AtLimit);
