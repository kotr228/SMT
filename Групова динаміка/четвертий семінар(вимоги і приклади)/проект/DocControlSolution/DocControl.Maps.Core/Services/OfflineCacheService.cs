using DocControl.Maps.Core.Data;
using DocControl.Maps.Core.Interfaces;
using DocControl.Maps.Core.Models;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace DocControl.Maps.Core.Services
{
    /// <summary>
    /// Сервіс офлайн-кешування карт
    /// </summary>
    public class OfflineCacheService : IOfflineCache
    {
        private readonly MapCacheRepository _repository;
        private readonly IMapProvider _provider;

        private CancellationTokenSource _downloadCts;
        private readonly ManualResetEventSlim _pauseEvent = new(true); // true = початково не на паузі

        public OfflineCacheService(MapCacheRepository repository, IMapProvider provider)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<MapTile> GetCachedTileAsync(int x, int y, int zoom)
        {
            return await _repository.GetTileAsync(x, y, zoom, _provider.ProviderName);
        }

        public async Task SaveTileAsync(MapTile tile)
        {
            await _repository.SaveTileAsync(tile);
        }

        public async Task<bool> IsTileCachedAsync(int x, int y, int zoom)
        {
            return await _repository.IsTileCachedAsync(x, y, zoom, _provider.ProviderName);
        }

        public async Task<long> GetCacheSizeAsync()
        {
            return await _repository.GetCacheSizeAsync();
        }

        public async Task ClearCacheAsync()
        {
            await _repository.ClearCacheAsync();
        }

        public async Task ClearOldCacheAsync(int daysOld)
        {
            await _repository.ClearOldCacheAsync(daysOld);
        }

        public async Task<CachedRegion> DownloadRegionAsync(double minLat, double minLon,
    double maxLat, double maxLon, int minZoom, int maxZoom)
        {
            // Встановлюємо новий CancellationTokenSource для цього завантаження
            _downloadCts = new CancellationTokenSource();
            var token = _downloadCts.Token;
            _pauseEvent.Set(); // Переконуємося, що не на паузі з минулого разу

            var region = new CachedRegion
            {
                Name = $"Region_{DateTime.Now:yyyyMMdd_HHmmss}",
                MinLatitude = minLat,
                MinLongitude = minLon,
                MaxLatitude = maxLat,
                MaxLongitude = maxLon,
                MinZoom = minZoom,
                MaxZoom = maxZoom,
                Provider = _provider.ProviderName,
                DownloadedAt = DateTime.Now
            };

            int totalTiles = 0;
            long totalSize = 0;

            try
            {
                for (int zoom = minZoom; zoom <= maxZoom; zoom++)
                {
                    var (minX, minY, maxX, maxY) = LatLonToTile(minLat, minLon, maxLat, maxLon, zoom);

                    for (int x = minX; x <= maxX; x++)
                    {
                        for (int y = minY; y <= maxY; y++)
                        {
                            // --- ДОДАНО ЛОГІКУ ПАУЗИ ТА СКАСУВАННЯ ---
                            token.ThrowIfCancellationRequested(); // Перевірка на скасування
                            _pauseEvent.Wait(token); // Чекає тут, якщо _pauseEvent.Reset() був викликаний
                                                     // ----------------------------------------

                            try
                            {
                                // Перевіряємо чи вже є в кеші
                                if (await IsTileCachedAsync(x, y, zoom))
                                    continue;

                                // Завантажуємо тайл
                                var imageData = await _provider.DownloadTileAsync(x, y, zoom);

                                if (imageData != null && imageData.Length > 0)
                                {
                                    var tile = new MapTile
                                    {
                                        X = x,
                                        Y = y,
                                        Zoom = zoom,
                                        Provider = _provider.ProviderName,
                                        ImageData = imageData,
                                        DownloadedAt = DateTime.Now,
                                        IsCached = true
                                    };

                                    await SaveTileAsync(tile);

                                    totalTiles++;
                                    totalSize += imageData.Length;
                                }

                                // Затримка щоб не перевантажувати сервер
                                await Task.Delay(100, token); // Передаємо token
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                Console.WriteLine($"Error downloading tile {x},{y} at zoom {zoom}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Download was cancelled.");
                region.Name += " (Cancelled)";
            }
            finally
            {
                _downloadCts.Dispose();
                _downloadCts = null;
            }

            region.TileCount = totalTiles;
            region.SizeBytes = totalSize;

            region.Id = await _repository.SaveRegionAsync(region);

            return region;
        }

        public void PauseDownload()
        {
            _pauseEvent.Reset(); // Встановлює "сигнал" на паузу
        }

        public void ResumeDownload()
        {
            _pauseEvent.Set(); // Знімає "сигнал" з паузи
        }

        public void CancelDownload()
        {
            _downloadCts?.Cancel(); // Надсилає сигнал скасування
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