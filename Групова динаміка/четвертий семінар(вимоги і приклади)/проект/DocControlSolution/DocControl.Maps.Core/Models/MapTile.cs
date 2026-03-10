using System;

namespace DocControl.Maps.Core.Models
{
    /// <summary>
    /// Представлення тайлу карти
    /// </summary>
    public class MapTile
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Zoom { get; set; }

        public byte[] ImageData { get; set; }
        public DateTime DownloadedAt { get; set; }
        public string Provider { get; set; }

        public bool IsCached { get; set; }

        public string GetKey() => $"{Provider}_{Zoom}_{X}_{Y}";
    }
}