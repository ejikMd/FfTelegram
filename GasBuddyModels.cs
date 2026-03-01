using System.Collections.Generic;

// Station details response models
public class StationDetailsResponse
{
    public StationData station { get; set; } = new StationData();
}

public class StationData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int BrandId { get; set; }
}

// Gas price map response models
public class GasPriceMapResponse
{
    public List<GasStationMapItem> primaryStations { get; set; } = new List<GasStationMapItem>();
    public List<GasStationMapItem> secondaryStations { get; set; } = new List<GasStationMapItem>();
}

public class GasStationMapItem
{
    public int id { get; set; }
    public double lat { get; set; }
    public double lng { get; set; }
    public string price { get; set; } = string.Empty;
    public bool iscash { get; set; }
    public string tme { get; set; } = string.Empty;
    public int brand_id { get; set; }
}