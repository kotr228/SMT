using DocControl.Maps.Core.Interfaces;
using DocControl.Maps.Core.Models;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace DocControl.Maps.Core.Services
{
    /// <summary>
    /// Сервіс геокодування (адреса ↔ координати)
    /// </summary>
    public class GeoCoderService : IGeoCoder
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _nominatimUrl;
        private readonly NetworkMonitor _networkMonitor;

        public bool IsOnlineAvailable => _networkMonitor.IsNetworkAvailable();
        public bool HasOfflineDatabase => false; // TODO: Реалізувати офлайн-базу

        public GeoCoderService(string nominatimUrl, NetworkMonitor networkMonitor)
        {
            _nominatimUrl = nominatimUrl ?? "https://nominatim.openstreetmap.org";
            _networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DocControl/0.5");
        }

        public async Task<GeoPoint> GeocodeAsync(string address)
        {
            if (!IsOnlineAvailable)
            {
                Console.WriteLine("Network not available for geocoding");
                return null;
            }

            try
            {
                var encodedAddress = HttpUtility.UrlEncode(address);
                var url = $"{_nominatimUrl}/search?q={encodedAddress}&format=json&limit=1";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var results = JsonSerializer.Deserialize<JsonElement[]>(content);

                if (results != null && results.Length > 0)
                {
                    var result = results[0];

                    return new GeoPoint
                    {
                        Latitude = double.Parse(result.GetProperty("lat").GetString()),
                        Longitude = double.Parse(result.GetProperty("lon").GetString()),
                        Address = result.GetProperty("display_name").GetString(),
                        Name = address
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Geocoding error: {ex.Message}");
            }

            return null;
        }

        public async Task<string> ReverseGeocodeAsync(double latitude, double longitude)
        {
            if (!IsOnlineAvailable)
            {
                Console.WriteLine("Network not available for reverse geocoding");
                return "Offline - location unavailable";
            }

            try
            {
                var url = $"{_nominatimUrl}/reverse?lat={latitude}&lon={longitude}&format=json";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(content);

                if (result.TryGetProperty("display_name", out var displayName))
                {
                    return displayName.GetString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reverse geocoding error: {ex.Message}");
            }

            return "Address not found";
        }
    }
}