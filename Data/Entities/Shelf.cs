namespace FridgeManager.Data.Entities;

public class Shelf
{
    public int Id { get; set; }
    public int RefrigeratorId { get; set; }
    public string Name { get; set; } = "";
    public int CapacityUnits { get; set; }
    public int SortOrder { get; set; }
    public Refrigerator Refrigerator { get; set; } = default!;
    public List<FoodItem> Items { get; set; } = [];
}
