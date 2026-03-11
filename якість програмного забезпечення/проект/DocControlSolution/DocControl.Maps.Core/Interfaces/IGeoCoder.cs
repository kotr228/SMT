using DocControl.Maps.Core.Models;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Interfaces
{
    /// <summary>
    /// Сервіс геокодування (адреса ↔ координати)
    /// </summary>
    public interface IGeoCoder
    {
        Task<GeoPoint> GeocodeAsync(string address);
        Task<string> ReverseGeocodeAsync(double latitude, double longitude);

        bool IsOnlineAvailable { get; }
        bool HasOfflineDatabase { get; }
    }
}