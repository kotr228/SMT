using DocControl.Maps.Core.Interfaces;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Providers
{
    /// <summary>
    /// Кастомний провайдер для власних tile-серверів
    /// </summary>
    public class CustomTileProvider : IMapProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _customUrl;

        public string ProviderName { get; }
        public string TileUrlTemplate { get; }
        public int MinZoom { get; }
        public int MaxZoom { get; }

        public CustomTileProvider(string name, string urlTemplate, int minZoom = 0, int maxZoom = 18)
        {
            ProviderName = name;
            TileUrlTemplate = urlTemplate;
            MinZoom = minZoom;
            MaxZoom = maxZoom;
        }

        public string GetTileUrl(int x, int y, int zoom)
        {
            return TileUrlTemplate
                .Replace("{z}", zoom.ToString())
                .Replace("{x}", x.ToString())
                .Replace("{y}", y.ToString());
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
                Console.WriteLine($"Error downloading custom tile: {ex.Message}");
                return null;
            }
        }
    }
}