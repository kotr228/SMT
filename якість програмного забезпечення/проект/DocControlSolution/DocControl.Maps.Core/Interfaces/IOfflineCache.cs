using DocControl.Maps.Core.Models;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Interfaces
{
    /// <summary>
    /// Сервіс кешування тайлів
    /// </summary>
    public interface IOfflineCache
    {
        Task<MapTile> GetCachedTileAsync(int x, int y, int zoom);
        Task SaveTileAsync(MapTile tile);
        Task<bool> IsTileCachedAsync(int x, int y, int zoom);

        Task<long> GetCacheSizeAsync();
        Task ClearCacheAsync();
        Task ClearOldCacheAsync(int daysOld);

        Task<CachedRegion> DownloadRegionAsync(double minLat, double minLon,
            double maxLat, double maxLon, int minZoom, int maxZoom);
    }
}