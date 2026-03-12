public class FuelStation
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public decimal Price { get; set; }
    public decimal Distance { get; set; }
    public int ProximityRating {get; set;} = 0; 

    public FuelStation(string name, string address, double latitude, double longitude, decimal price)
    {
        Name = name;
        Address = address;
        Latitude = latitude;
        Longitude = longitude;
        Price = price;
    }
}
