using System.Collections.Generic;

namespace DocControl.Maps.Core.Models
{
    /// <summary>
    /// Маршрут між точками
    /// </summary>
    public class GeoRoute
    {
        public string Name { get; set; }
        public List<GeoPoint> Points { get; set; } = new List<GeoPoint>();
        public double TotalDistanceKm { get; set; }
        public string Color { get; set; } = "#2196F3";
        public int StrokeWidth { get; set; } = 2;

        public void CalculateDistance()
        {
            TotalDistanceKm = 0;
            for (int i = 0; i < Points.Count - 1; i++)
            {
                TotalDistanceKm += CalculateDistance(Points[i], Points[i + 1]);
            }
        }

        private double CalculateDistance(GeoPoint p1, GeoPoint p2)
        {
            const double R = 6371; // км
            var dLat = ToRadians(p2.Latitude - p1.Latitude);
            var dLon = ToRadians(p2.Longitude - p1.Longitude);

            var a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                    System.Math.Cos(ToRadians(p1.Latitude)) *
                    System.Math.Cos(ToRadians(p2.Latitude)) *
                    System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);

            var c = 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double degrees) => degrees * System.Math.PI / 180.0;
    }
}