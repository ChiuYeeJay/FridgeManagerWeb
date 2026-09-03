namespace FridgeManager.Data.Entities;

public class Refrigerator
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Shelf> Shelves { get; set; } = [];
}
