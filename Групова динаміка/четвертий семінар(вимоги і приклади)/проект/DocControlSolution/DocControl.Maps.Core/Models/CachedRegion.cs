using System;

namespace DocControl.Maps.Core.Models
{
    /// <summary>
    /// Інформація про завантажену офлайн-область
    /// </summary>
    public class CachedRegion
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public double MinLatitude { get; set; }
        public double MinLongitude { get; set; }
        public double MaxLatitude { get; set; }
        public double MaxLongitude { get; set; }

        public int MinZoom { get; set; }
        public int MaxZoom { get; set; }

        public DateTime DownloadedAt { get; set; }
        public long SizeBytes { get; set; }
        public int TileCount { get; set; }

        public string Provider { get; set; }
    }
}