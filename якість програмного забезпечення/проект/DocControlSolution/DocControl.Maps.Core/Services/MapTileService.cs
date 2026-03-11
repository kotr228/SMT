using DocControl.Maps.Core.Interfaces;
using DocControl.Maps.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Services
{
    /// <summary>
    /// Головний сервіс роботи з тайлами карт
    /// </summary>
    public class MapTileService : IMapService
    {
        private IMapProvider _currentProvider;
        private readonly IOfflineCache _cache;
        private readonly NetworkMonitor _networkMonitor;

        public IMapProvider CurrentProvider => _currentProvider;

        public MapTileService(IMapProvider provider, IOfflineCache cache, NetworkMonitor networkMonitor)
        {
            _currentProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
        }

        public void SetProvider(IMapProvider provider)
        {
            _currentProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<MapTile> GetTileAsync(int x, int y, int zoom, bool allowOnline = true)
        {
            // 1. Спробувати завантажити з кешу
            var cachedTile = await _cache.GetCachedTileAsync(x, y, zoom);
            if (cachedTile != null)
            {
                Console.WriteLine($"Tile {x},{y},{zoom} loaded from cache");
                return cachedTile;
            }

            // 2. Якщо немає в кеші і дозволено онлайн
            if (allowOnline && _networkMonitor.IsNetworkAvailable())
            {
                try
                {
                    var imageData = await _currentProvider.DownloadTileAsync(x, y, zoom);

                    if (imageData != null && imageData.Length > 0)
                    {
                        var tile = new MapTile
                        {
                            X = x,
                            Y = y,
                            Zoom = zoom,
                            Provider = _currentProvider.ProviderName,
                            ImageData = imageData,
                            DownloadedAt = DateTime.Now,
                            IsCached = false
                        };

                        // Зберігаємо в кеш для наступного використання
                        await _cache.SaveTileAsync(tile);

                        Console.WriteLine($"Tile {x},{y},{zoom} downloaded from {_currentProvider.ProviderName}");
                        return tile;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error downloading tile: {ex.Message}");
                }
            }

            Console.WriteLine($"Tile {x},{y},{zoom} not available");
            return null;
        }

        public async Task<List<MapTile>> GetTilesForBoundsAsync(double minLat, double minLon,
            double maxLat, double maxLon, int zoom)
        {
            var tiles = new List<MapTile>();

            var (minX, minY, maxX, maxY) = LatLonToTile(minLat, minLon, maxLat, maxLon, zoom);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var tile = await GetTileAsync(x, y, zoom);
                    if (tile != null)
                    {
                        tiles.Add(tile);
                    }
                }
            }

            return tiles;
        }

        public async Task ClearCacheAsync()
        {
            await _cache.ClearCacheAsync();
        }

        public async Task<long> GetCacheSizeAsync()
        {
            return await _cache.GetCacheSizeAsync();
        }

        private (int minX, int minY, int maxX, int maxY) LatLonToTile(
            double minLat, double minLon, double maxLat, double maxLon, int zoom)
        {
            int minX = LonToTileX(minLon, zoom);
            int maxX = LonToTileX(maxLon, zoom);
            int minY = LatToTileY(maxLat, zoom);
            int maxY = LatToTileY(minLat, zoom);

            return (minX, minY, maxX, maxY);
        }

        private int LonToTileX(double lon, int zoom)
        {
            return (int)Math.Floor((lon + 180.0) / 360.0 * Math.Pow(2, zoom));
        }

        private int LatToTileY(double lat, int zoom)
        {
            double latRad = lat * Math.PI / 180.0;
            return (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * Math.Pow(2, zoom));
        }
    }
}