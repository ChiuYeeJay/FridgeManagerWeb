namespace FridgeManager.Services.Models;

public sealed record ShelfUsageDto(int ShelfId, string Name, int SortOrder, int CapacityUnits, int UsedUnits)
{
    public int Remaining => Math.Max(0, CapacityUnits - UsedUnits);
}
