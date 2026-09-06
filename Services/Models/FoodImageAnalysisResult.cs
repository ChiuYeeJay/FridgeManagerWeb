using FridgeManager.Data.Enums;

namespace FridgeManager.Services.Models;

public sealed record FoodImageAnalysisResult(
    string? Name,
    FoodCategory? Category,
    DateOnly? ExpirationDate,
    int? SizeUnits,
    string? Note,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlySet<string> ApplyTo(FoodItemForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        if (Name is not null)
        {
            form.Name = Name;
            applied.Add(nameof(FoodItemForm.Name));
        }

        if (Category is FoodCategory category)
        {
            form.Category = category;
            applied.Add(nameof(FoodItemForm.Category));
        }

        if (ExpirationDate is DateOnly expiration)
        {
            form.ExpirationDate = expiration;
            applied.Add(nameof(FoodItemForm.ExpirationDate));
        }

        if (SizeUnits is int size)
        {
            form.SizeUnits = size;
            applied.Add(nameof(FoodItemForm.SizeUnits));
        }

        if (Note is not null)
        {
            form.Note = Note;
            applied.Add(nameof(FoodItemForm.Note));
        }

        return applied;
    }
}
