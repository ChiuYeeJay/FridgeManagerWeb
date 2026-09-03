using System.ComponentModel.DataAnnotations;
using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;

namespace FridgeManager.Services.Models;

public class FoodItemForm
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    public FoodCategory Category { get; set; }

    public DateOnly ExpirationDate { get; set; }

    [Range(1, 3, ErrorMessage = "Size must be Small (1), Medium (2), or Large (3).")]
    public int SizeUnits { get; set; } = 1;

    public bool IsShared { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a shelf")]
    public int ShelfId { get; set; }

    [StringLength(200)]
    public string? PositionNote { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    public string? ImagePath { get; set; }

    public static FoodItemForm FromEntity(FoodItem item) => new()
    {
        Name = item.Name,
        Category = item.Category,
        ExpirationDate = item.ExpirationDate,
        SizeUnits = item.SizeUnits,
        IsShared = item.IsShared,
        ShelfId = item.ShelfId,
        PositionNote = item.PositionNote,
        Note = item.Note,
        ImagePath = item.ImagePath
    };

    public void ApplyTo(FoodItem item)
    {
        item.Name = Name.Trim();
        item.Category = Category;
        item.ExpirationDate = ExpirationDate;
        item.SizeUnits = SizeUnits;
        item.IsShared = IsShared;
        item.ShelfId = ShelfId;
        item.PositionNote = string.IsNullOrWhiteSpace(PositionNote) ? null : PositionNote.Trim();
        item.Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        item.ImagePath = ImagePath;
    }
}
