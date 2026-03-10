using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace DocControlService.Services
{
    /// <summary>
    /// Сервіс для роботи з геокодуванням та картами
    /// </summary>
    public class GeoMappingService
    {
        private readonly HttpClient _httpClient;
        private const string NominatimBaseUrl = "https://nominatim.openstreetmap.org";

        public GeoMappingService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DocControlService/0.3");
        }

        #region Geocoding (Адреса -> Координати)

        /// <summary>
        /// Геокодування адреси в координати (OpenStreetMap Nominatim)
        /// </summary>
        public async Task<GeocodeResponse> GeocodeAddressAsync(string address)
        {
            try
            {
                var encodedAddress = HttpUtility.UrlEncode(address);
                var url = $"{NominatimBaseUrl}/search?q={encodedAddress}&format=json&limit=1";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var results = JsonSerializer.Deserialize<List<NominatimResult>>(content);

                if (results != null && results.Count > 0)
                {
                    var result = results[0];
                    return new GeocodeResponse
                    {
                        Success = true,
                        Latitude = double.Parse(result.lat),
                        Longitude = double.Parse(result.lon),
                        FormattedAddress = result.display_name
                    };
                }

                return new GeocodeResponse
                {
                    Success = false,
                    FormattedAddress = "Адресу не знайдено"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка геокодування: {ex.Message}");
                return new GeocodeResponse
                {
                    Success = false,
                    FormattedAddress = $"Помилка: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Зворотне геокодування (Координати -> Адреса)
        /// </summary>
        public async Task<string> ReverseGeocodeAsync(double latitude, double longitude)
        {
            try
            {
                var url = $"{NominatimBaseUrl}/reverse?lat={latitude}&lon={longitude}&format=json";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<NominatimResult>(content);

                return result?.display_name ?? "Адресу не знайдено";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка зворотного геокодування: {ex.Message}");
                return "Помилка отримання адреси";
            }
        }

        #endregion

        #region Route Calculation

        /// <summary>
        /// Розрахунок відстані між двома точками (формула Haversine)
        /// </summary>
        public double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Радіус Землі в км

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var distance = R * c;

            return distance;
        }

        private double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        /// <summary>
        /// Розрахунок оптимального маршруту через декілька точок
        /// Проста евристика - найближчий сусід
        /// </summary>
        public List<GeoRoadmapNode> CalculateOptimalRoute(List<GeoRoadmapNode> nodes)
        {
            if (nodes == null || nodes.Count <= 2)
                return nodes;

            var optimizedRoute = new List<GeoRoadmapNode>();
            var remaining = new List<GeoRoadmapNode>(nodes);

            // Починаємо з першого вузла
            var current = remaining[0];
            optimizedRoute.Add(current);
            remaining.Remove(current);

            // Жадібний алгоритм - вибираємо найближчу точку
            while (remaining.Count > 0)
            {
                GeoRoadmapNode nearest = null;
                double minDistance = double.MaxValue;

                foreach (var node in remaining)
                {
                    var distance = CalculateDistance(
                        current.Latitude, current.Longitude,
                        node.Latitude, node.Longitude);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearest = node;
                    }
                }

                if (nearest != null)
                {
                    optimizedRoute.Add(nearest);
                    remaining.Remove(nearest);
                    current = nearest;
                }
                else
                {
                    break;
                }
            }

            return optimizedRoute;
        }

        #endregion

        #region Map Utilities

        /// <summary>
        /// Генерація URL для статичної карти (OpenStreetMap)
        /// </summary>
        public string GenerateStaticMapUrl(double centerLat, double centerLon, int zoom, int width = 800, int height = 600)
        {
            // Використовуємо StaticMapLite або аналогічний сервіс
            return $"https://staticmap.openstreetmap.de/staticmap.php?center={centerLat},{centerLon}&zoom={zoom}&size={width}x{height}&maptype=mapnik";
        }

        /// <summary>
        /// Отримання меж (bounds) для набору точок
        /// </summary>
        public (double minLat, double minLon, double maxLat, double maxLon) CalculateBounds(List<GeoRoadmapNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return (0, 0, 0, 0);

            double minLat = double.MaxValue;
            double minLon = double.MaxValue;
            double maxLat = double.MinValue;
            double maxLon = double.MinValue;

            foreach (var node in nodes)
            {
                if (node.Latitude < minLat) minLat = node.Latitude;
                if (node.Longitude < minLon) minLon = node.Longitude;
                if (node.Latitude > maxLat) maxLat = node.Latitude;
                if (node.Longitude > maxLon) maxLon = node.Longitude;
            }

            return (minLat, minLon, maxLat, maxLon);
        }

        /// <summary>
        /// Розрахунок центру та зуму для відображення всіх точок
        /// </summary>
        public (double centerLat, double centerLon, int zoom) CalculateOptimalView(List<GeoRoadmapNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return (50.4501, 30.5234, 5); // Київ за замовчуванням

            var bounds = CalculateBounds(nodes);

            double centerLat = (bounds.minLat + bounds.maxLat) / 2;
            double centerLon = (bounds.minLon + bounds.maxLon) / 2;

            // Простий розрахунок зуму на основі розміру області
            double latDiff = bounds.maxLat - bounds.minLat;
            double lonDiff = bounds.maxLon - bounds.minLon;
            double maxDiff = Math.Max(latDiff, lonDiff);

            int zoom;
            if (maxDiff > 10) zoom = 5;
            else if (maxDiff > 5) zoom = 6;
            else if (maxDiff > 2) zoom = 7;
            else if (maxDiff > 1) zoom = 8;
            else if (maxDiff > 0.5) zoom = 9;
            else if (maxDiff > 0.2) zoom = 10;
            else if (maxDiff > 0.1) zoom = 11;
            else if (maxDiff > 0.05) zoom = 12;
            else if (maxDiff > 0.01) zoom = 13;
            else zoom = 14;

            return (centerLat, centerLon, zoom);
        }

        #endregion

        #region Google Maps Integration (опціонально)

        /// <summary>
        /// Геокодування через Google Maps API (потрібен API ключ)
        /// </summary>
        public async Task<GeocodeResponse> GeocodeWithGoogleAsync(string address, string apiKey)
        {
            try
            {
                var encodedAddress = HttpUtility.UrlEncode(address);
                var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={encodedAddress}&key={apiKey}";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(content);

                if (result?.status == "OK" && result.results?.Count > 0)
                {
                    var location = result.results[0].geometry.location;
                    return new GeocodeResponse
                    {
                        Success = true,
                        Latitude = location.lat,
                        Longitude = location.lng,
                        FormattedAddress = result.results[0].formatted_address
                    };
                }

                return new GeocodeResponse { Success = false, FormattedAddress = "Не знайдено" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка Google Geocoding: {ex.Message}");
                return new GeocodeResponse { Success = false, FormattedAddress = $"Помилка: {ex.Message}" };
            }
        }

        #endregion

        #region Helper Classes

        private class NominatimResult
        {
            public string lat { get; set; }
            public string lon { get; set; }
            public string display_name { get; set; }
        }

        private class GoogleGeocodeResponse
        {
            public string status { get; set; }
            public List<GoogleResult> results { get; set; }
        }

        private class GoogleResult
        {
            public string formatted_address { get; set; }
            public GoogleGeometry geometry { get; set; }
        }

        private class GoogleGeometry
        {
            public GoogleLocation location { get; set; }
        }

        private class GoogleLocation
        {
            public double lat { get; set; }
            public double lng { get; set; }
        }

        #endregion
    }
}