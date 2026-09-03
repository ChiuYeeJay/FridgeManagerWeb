using FridgeManager.Data.Enums;

namespace FridgeManager.Data.Entities;

public class FoodItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public FoodCategory Category { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public int SizeUnits { get; set; }
    public bool IsShared { get; set; }
    public FoodStatus Status { get; set; } = FoodStatus.Active;
    public int ShelfId { get; set; }
    public string? PositionNote { get; set; }
    public string? Note { get; set; }
    public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ApplicationUser Owner { get; set; } = default!;
    public Shelf Shelf { get; set; } = default!;
}
