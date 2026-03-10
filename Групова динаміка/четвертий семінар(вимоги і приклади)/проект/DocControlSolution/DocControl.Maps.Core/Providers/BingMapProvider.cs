using DocControl.Maps.Core.Interfaces;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Providers
{
    /// <summary>
    /// Провайдер Bing Maps
    /// </summary>
    public class BingMapProvider : IMapProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public string ProviderName => "Bing Maps";
        public string TileUrlTemplate => "http://ecn.t3.tiles.virtualearth.net/tiles/r{quadkey}.jpeg?g=1";
        public int MinZoom => 1;
        public int MaxZoom => 19;

        public string GetTileUrl(int x, int y, int zoom)
        {
            string quadkey = TileToQuadKey(x, y, zoom);
            return TileUrlTemplate.Replace("{quadkey}", quadkey);
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
                Console.WriteLine($"Error downloading Bing tile: {ex.Message}");
                return null;
            }
        }

        private string TileToQuadKey(int x, int y, int zoom)
        {
            char[] quadKey = new char[zoom];
            for (int i = zoom; i > 0; i--)
            {
                char digit = '0';
                int mask = 1 << (i - 1);
                if ((x & mask) != 0) digit++;
                if ((y & mask) != 0) digit++;
                quadKey[zoom - i] = digit;
            }
            return new string(quadKey);
        }
    }
}