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
    public IReadOnlySet<string> ApplyTo(
        FoodItemForm form,
        IReadOnlySet<string>? userEnteredFields = null)
    {
        ArgumentNullException.ThrowIfNull(form);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        if (Name is not null && !IsUserEntered(userEnteredFields, nameof(FoodItemForm.Name)))
        {
            form.Name = Name;
            applied.Add(nameof(FoodItemForm.Name));
        }

        if (Category is FoodCategory category
            && !IsUserEntered(userEnteredFields, nameof(FoodItemForm.Category)))
        {
            form.Category = category;
            applied.Add(nameof(FoodItemForm.Category));
        }

        if (ExpirationDate is DateOnly expiration
            && !IsUserEntered(userEnteredFields, nameof(FoodItemForm.ExpirationDate)))
        {
            form.ExpirationDate = expiration;
            applied.Add(nameof(FoodItemForm.ExpirationDate));
        }

        if (SizeUnits is int size && !IsUserEntered(userEnteredFields, nameof(FoodItemForm.SizeUnits)))
        {
            form.SizeUnits = size;
            applied.Add(nameof(FoodItemForm.SizeUnits));
        }

        if (Note is not null && !IsUserEntered(userEnteredFields, nameof(FoodItemForm.Note)))
        {
            form.Note = Note;
            applied.Add(nameof(FoodItemForm.Note));
        }

        return applied;
    }

    private static bool IsUserEntered(IReadOnlySet<string>? userEnteredFields, string field)
        => userEnteredFields is not null && userEnteredFields.Contains(field);
}
