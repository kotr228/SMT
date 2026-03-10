using DocControl.Maps.Core.Interfaces;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Providers
{
    /// <summary>
    /// Провайдер OpenStreetMap
    /// </summary>
    public class OpenStreetMapProvider : IMapProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public string ProviderName => "OpenStreetMap";
        public string TileUrlTemplate => "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        public int MinZoom => 0;
        public int MaxZoom => 19;

        public OpenStreetMapProvider()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DocControl/0.5");
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
                Console.WriteLine($"Error downloading tile: {ex.Message}");
                return null;
            }
        }
    }
}