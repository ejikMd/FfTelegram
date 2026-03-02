public class FuelStation
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public FuelStation(string name, string address, decimal price)
    {
        Name = name;
        Address = address;
        Price = price;
    }
}