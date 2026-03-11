using DocControl.Maps.Core.Interfaces;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Providers
{
    /// <summary>
    /// Провайдер Google Maps (потрібен API ключ)
    /// </summary>
    public class GoogleMapProvider : IMapProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _apiKey;

        public string ProviderName => "Google Maps";
        public string TileUrlTemplate => "https://mt1.google.com/vt/lyrs=m&x={x}&y={y}&z={z}";
        public int MinZoom => 0;
        public int MaxZoom => 21;

        public GoogleMapProvider(string apiKey = null)
        {
            _apiKey = apiKey;
        }

        public string GetTileUrl(int x, int y, int zoom)
        {
            var url = TileUrlTemplate
                .Replace("{z}", zoom.ToString())
                .Replace("{x}", x.ToString())
                .Replace("{y}", y.ToString());

            if (!string.IsNullOrEmpty(_apiKey))
                url += $"&key={_apiKey}";

            return url;
        }

        public async Task<byte[]> DownloadTileAsync(int x, int y, int zoom)
        {
            try
            {
                var url = GetTileUrl(x, y, zoom);
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading Google tile: {ex.Message}");
                return null;
            }
        }
    }
}