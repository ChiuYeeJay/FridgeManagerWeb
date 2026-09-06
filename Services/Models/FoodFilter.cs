using FridgeManager.Data.Enums;

namespace FridgeManager.Services.Models;

public class FoodFilter
{
    public string? Search { get; set; }
    public bool MineOnly { get; set; }
    public bool SharedOnly { get; set; }
    public FoodCategory? Category { get; set; }
    public int? ShelfId { get; set; }
    public FoodStatus? Status { get; set; } = FoodStatus.Active;
    public ExpiryState? Expiry { get; set; }
    public FoodSort Sort { get; set; } = FoodSort.Expiry;
    public bool? SortDescending { get; set; }
    public string? OwnerId { get; set; }
    public DateOnly? Today { get; set; }
    public string? CurrentUserId { get; set; }

    public bool EffectiveDescending => SortDescending ?? FoodSortRules.DefaultDescending(Sort);

    public bool HasClearableFilters
        => !string.IsNullOrWhiteSpace(Search)
           || SharedOnly
           || Category is not null
           || ShelfId is not null
           || !string.IsNullOrEmpty(OwnerId)
           || Status != FoodStatus.Active
           || Expiry is not null;

    public void ClearFilters()
    {
        Search = null;
        SharedOnly = false;
        Category = null;
        ShelfId = null;
        OwnerId = null;
        Status = FoodStatus.Active;
        Expiry = null;
    }

    public string ToQuery()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            parts.Add($"q={Uri.EscapeDataString(Search.Trim())}");
        }

        if (MineOnly)
        {
            parts.Add("mine=1");
        }

        if (SharedOnly)
        {
            parts.Add("shared=1");
        }

        if (Category is FoodCategory category)
        {
            parts.Add($"category={category}");
        }

        if (ShelfId is int shelfId)
        {
            parts.Add($"shelf={shelfId}");
        }

        if (!string.IsNullOrEmpty(OwnerId))
        {
            parts.Add($"owner={Uri.EscapeDataString(OwnerId)}");
        }

        if (Status is FoodStatus status && status != FoodStatus.Active)
        {
            parts.Add($"status={status}");
        }

        if (Expiry is ExpiryState expiry)
        {
            parts.Add($"expiry={expiry}");
        }

        if (Sort != FoodSort.Expiry)
        {
            parts.Add($"sort={Sort}");
        }

        var descending = EffectiveDescending;
        if (descending != FoodSortRules.DefaultDescending(Sort))
        {
            parts.Add(descending ? "dir=desc" : "dir=asc");
        }

        return string.Join("&", parts);
    }

    public string ToPath()
    {
        var query = ToQuery();
        return string.IsNullOrEmpty(query) ? "food" : $"food?{query}";
    }

    public static FoodFilter FromQuery(
        string? q = null,
        string? mine = null,
        string? shared = null,
        string? category = null,
        string? shelf = null,
        string? status = null,
        string? expiry = null,
        string? sort = null,
        string? dir = null,
        string? currentUserId = null,
        string? owner = null,
        DateOnly? today = null)
    {
        var parsedSort = Enum.TryParse<FoodSort>(sort, ignoreCase: true, out var sortValue)
            ? sortValue
            : FoodSort.Expiry;

        bool? descending = dir?.ToLowerInvariant() switch
        {
            "desc" or "descending" => true,
            "asc" or "ascending" => false,
            _ => null
        };

        return new FoodFilter
        {
            Search = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            MineOnly = IsFlag(mine),
            SharedOnly = IsFlag(shared),
            Category = Enum.TryParse<FoodCategory>(category, ignoreCase: true, out var parsedCategory)
                ? parsedCategory
                : null,
            ShelfId = int.TryParse(shelf, out var shelfId) ? shelfId : null,
            Status = Enum.TryParse<FoodStatus>(status, ignoreCase: true, out var parsedStatus)
                ? parsedStatus
                : FoodStatus.Active,
            Expiry = Enum.TryParse<ExpiryState>(expiry, ignoreCase: true, out var parsedExpiry)
                ? parsedExpiry
                : null,
            Sort = parsedSort,
            SortDescending = descending,
            OwnerId = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim(),
            Today = today,
            CurrentUserId = currentUserId
        };
    }

    private static bool IsFlag(string? value)
        => value is "1" or "true" or "True" or "yes" or "on";
}
