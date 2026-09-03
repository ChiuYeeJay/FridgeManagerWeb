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
    public bool ExpiringSoon { get; set; }
    public bool Expired { get; set; }
    public string? CurrentUserId { get; set; }
}
