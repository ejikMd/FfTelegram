using System;
using System.Collections.Generic;

// GasBuddy API response models
public class FuelStationData
{
    public Data data { get; set; } = new Data();
}

public class Data
{
    public LocationBySearchTerm locationBySearchTerm { get; set; } = new LocationBySearchTerm();
}

public class LocationBySearchTerm
{
    public string displayName { get; set; } = string.Empty;
    public double latitude { get; set; }
    public double longitude { get; set; }
    public Stations stations { get; set; } = new Stations();
}

public class Stations
{
    public int count { get; set; }
    public Cursor cursor { get; set; } = new Cursor();
    public List<StationResult> results { get; set; } = new List<StationResult>();
}

public class Cursor
{
    public string? next { get; set; }
}

public class StationResult
{
    public string id { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public Address address { get; set; } = new Address();
    public List<PriceInfo> prices { get; set; } = new List<PriceInfo>();
}

public class Address
{
    public string country { get; set; } = string.Empty;
    public string line1 { get; set; } = string.Empty;
    public string line2 { get; set; } = string.Empty;
    public string locality { get; set; } = string.Empty;
    public string postalCode { get; set; } = string.Empty;
    public string region { get; set; } = string.Empty;
}

public class PriceInfo
{
    public string fuelProduct { get; set; } = string.Empty;
    public CreditPrice credit { get; set; } = new CreditPrice();
}

public class CreditPrice
{
    public string nickname { get; set; } = string.Empty;
    public string postedTime { get; set; } = string.Empty;
    public decimal price { get; set; }
    public string formattedPrice { get; set; } = string.Empty;
}