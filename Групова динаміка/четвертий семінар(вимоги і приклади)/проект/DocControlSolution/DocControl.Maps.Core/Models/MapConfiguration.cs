namespace DocControl.Maps.Core.Models
{
    /// <summary>
    /// Конфігурація картографічного ядра
    /// </summary>
    public class MapConfiguration
    {
        public string DefaultProvider { get; set; } = "OpenStreetMap";
        public string CachePath { get; set; } = "MapCache";
        public long MaxCacheSizeMB { get; set; } = 500;
        public int CacheExpirationDays { get; set; } = 30;

        public bool EnableOfflineMode { get; set; } = true;
        public bool AutoDownloadTiles { get; set; } = true;

        public string NominatimUrl { get; set; } = "https://nominatim.openstreetmap.org";
        public int MaxConcurrentDownloads { get; set; } = 4;
    }
}