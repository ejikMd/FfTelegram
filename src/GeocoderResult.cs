public class GeocoderResult
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string FormattedAddress { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Provider { get; set; } = string.Empty;
}
