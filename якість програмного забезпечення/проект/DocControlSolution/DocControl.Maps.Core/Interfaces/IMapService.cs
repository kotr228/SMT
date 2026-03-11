using DocControl.Maps.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Interfaces
{
    /// <summary>
    /// Головний сервіс роботи з картами
    /// </summary>
    public interface IMapService
    {
        Task<MapTile> GetTileAsync(int x, int y, int zoom, bool allowOnline = true);
        Task<List<MapTile>> GetTilesForBoundsAsync(double minLat, double minLon,
            double maxLat, double maxLon, int zoom);

        Task ClearCacheAsync();
        Task<long> GetCacheSizeAsync();

        void SetProvider(IMapProvider provider);
        IMapProvider CurrentProvider { get; }
    }
}