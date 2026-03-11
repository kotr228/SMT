namespace DocControl.Maps.Core.Models
{
    /// <summary>
    /// Географічна точка
    /// </summary>
    public class GeoPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Address { get; set; }
        public string Name { get; set; }

        public GeoPoint() { }

        public GeoPoint(double lat, double lon)
        {
            Latitude = lat;
            Longitude = lon;
        }

        public override string ToString() => $"{Latitude:F6}, {Longitude:F6}";
    }
}