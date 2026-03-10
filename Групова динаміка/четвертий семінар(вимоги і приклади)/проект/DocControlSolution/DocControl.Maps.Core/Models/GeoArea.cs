using System.Collections.Generic;

namespace DocControl.Maps.Core.Models
{
    /// <summary>
    /// Географічна область (полігон)
    /// </summary>
    public class GeoArea
    {
        public string Name { get; set; }
        public List<GeoPoint> Boundary { get; set; } = new List<GeoPoint>();
        public string FillColor { get; set; } = "#2196F3";
        public string StrokeColor { get; set; } = "#1976D2";
        public double Opacity { get; set; } = 0.3;
    }
}