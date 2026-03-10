using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Services
{
    /// <summary>
    /// Моніторинг стану мережі
    /// </summary>
    public class NetworkMonitor
    {
        public event EventHandler<bool> NetworkStatusChanged;

        private bool _lastStatus;

        public NetworkMonitor()
        {
            _lastStatus = IsNetworkAvailable();
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChange;
        }

        public bool IsNetworkAvailable()
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }

        public async Task<bool> TestInternetConnectionAsync()
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("8.8.8.8", 3000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        private void OnNetworkChange(object sender, NetworkAvailabilityEventArgs e)
        {
            bool currentStatus = e.IsAvailable;

            if (currentStatus != _lastStatus)
            {
                _lastStatus = currentStatus;
                NetworkStatusChanged?.Invoke(this, currentStatus);

                Console.WriteLine($"Network status changed: {(currentStatus ? "Online" : "Offline")}");
            }
        }
    }
}