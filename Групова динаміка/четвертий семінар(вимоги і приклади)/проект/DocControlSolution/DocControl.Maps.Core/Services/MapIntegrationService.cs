using DocControl.Maps.Core;
using DocControl.Maps.Core.Interfaces;
using DocControl.Maps.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Services
{
    /// <summary>
    /// Сервіс інтеграції між UI та Maps Core.
    /// Забезпечує offline/online роботу карт.
    /// </summary>
    public class MapIntegrationService
    {
        private readonly MapModule _mapModule;
        private readonly IMapService _mapService;
        private readonly IGeoCoder _geoCoder;
        private readonly IOfflineCache _offlineCache;

        public MapIntegrationService()
        {
            _mapModule = new MapModule();
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                await _mapModule.InitializeAsync();
                return _mapModule.IsInitialized;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка ініціалізації Maps Core: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Отримати tile для WebView2 (з кешу або онлайн)
        /// </summary>
        public async Task<byte[]> GetTileAsync(int x, int y, int zoom, bool allowOnline = true)
        {
            if (_mapModule?.MapService == null)
                return null;

            try
            {
                var tile = await _mapModule.MapService.GetTileAsync(x, y, zoom, allowOnline);
                return tile?.ImageData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка отримання tile: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Геокодування адреси (адреса → координати)
        /// </summary>
        public async Task<(bool success, double lat, double lng, string address)> GeocodeAsync(string address)
        {
            if (_mapModule?.GeoCoder == null)
                return (false, 0, 0, "GeoCoder не ініціалізовано");

            try
            {
                var result = await _mapModule.GeoCoder.GeocodeAsync(address);
                if (result != null)
                    return (true, result.Latitude, result.Longitude, result.Address);

                return (false, 0, 0, "Адресу не знайдено");
            }
            catch (Exception ex)
            {
                return (false, 0, 0, $"Помилка: {ex.Message}");
            }
        }

        /// <summary>
        /// Зворотне геокодування (координати → адреса)
        /// </summary>
        public async Task<string> ReverseGeocodeAsync(double latitude, double longitude)
        {
            if (_mapModule?.GeoCoder == null)
                return "GeoCoder не ініціалізовано";

            try
            {
                return await _mapModule.GeoCoder.ReverseGeocodeAsync(latitude, longitude);
            }
            catch (Exception ex)
            {
                return $"Помилка: {ex.Message}";
            }
        }

        /// <summary>
        /// Розрахунок відстані між точками
        /// </summary>
        public double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            if (_mapModule?.DataService == null)
                return CalculateHaversineDistance(lat1, lon1, lat2, lon2);

            var p1 = new GeoPoint(lat1, lon1);
            var p2 = new GeoPoint(lat2, lon2);
            return _mapModule.DataService.CalculateDistance(p1, p2);
        }

        private double CalculateHaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // км
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double degrees) => degrees * Math.PI / 180.0;

        /// <summary>
        /// Завантажити регіон для offline використання
        /// </summary>
        public async Task<bool> DownloadRegionAsync(
            double minLat, double minLon,
            double maxLat, double maxLon,
            int minZoom, int maxZoom)
        {
            if (_mapModule?.OfflineCache == null)
                return false;

            try
            {
                var region = await _mapModule.OfflineCache.DownloadRegionAsync(
                    minLat, minLon, maxLat, maxLon, minZoom, maxZoom);

                Console.WriteLine($"Завантажено регіон: {region.TileCount} тайлів, {region.SizeBytes / 1024 / 1024:F2} MB");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка завантаження регіону: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Очистити кеш тайлів
        /// </summary>
        public async Task<bool> ClearCacheAsync()
        {
            if (_mapModule?.OfflineCache == null)
                return false;

            try
            {
                await _mapModule.OfflineCache.ClearCacheAsync();
                Console.WriteLine("Кеш карт очищено.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка очищення кешу: {ex.Message}");
                return false;
            }
        }
    }
}
