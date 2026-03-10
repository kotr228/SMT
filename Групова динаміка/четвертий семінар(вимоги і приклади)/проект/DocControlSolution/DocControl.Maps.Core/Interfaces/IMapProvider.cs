using System.Threading.Tasks;

namespace DocControl.Maps.Core.Interfaces
{
    /// <summary>
    /// Інтерфейс провайдера карт (OSM, Google, Bing)
    /// </summary>
    public interface IMapProvider
    {
        string ProviderName { get; }
        string TileUrlTemplate { get; }
        int MinZoom { get; }
        int MaxZoom { get; }

        string GetTileUrl(int x, int y, int zoom);
        Task<byte[]> DownloadTileAsync(int x, int y, int zoom);
    }
}