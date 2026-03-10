using DocControl.Maps.Core.Data;
using DocControl.Maps.Core.Interfaces;
using DocControl.Maps.Core.Models;
using DocControl.Maps.Core.Providers;
using DocControl.Maps.Core.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocControl.Maps.Core
{
    /// <summary>
    /// Головний модуль картографічного ядра
    /// Реалізує IKernelModule для інтеграції з системою
    /// </summary>
    public class MapModule : IKernelModule
    {
        public string ModuleName => "DocControl.Maps.Core";
        public string Version => "0.5.0";
        public bool IsInitialized { get; private set; }

        // Сервіси
        public IMapService MapService { get; private set; }
        public IGeoCoder GeoCoder { get; private set; }
        public IOfflineCache OfflineCache { get; private set; }
        public NetworkMonitor NetworkMonitor { get; private set; }
        public MapDataService DataService { get; private set; }

        // Конфігурація
        public MapConfiguration Configuration { get; private set; }

        // Репозиторій
        private MapCacheRepository _cacheRepository;

        public MapModule()
        {
            LoadConfiguration();
        }

        public async Task InitializeAsync()
        {
            if (IsInitialized)
                return;

            Console.WriteLine($"🗺️ Initializing {ModuleName} v{Version}...");

            try
            {
                // 1. Ініціалізація репозиторію
                _cacheRepository = new MapCacheRepository(Configuration.CachePath);

                // 2. Ініціалізація Network Monitor
                NetworkMonitor = new NetworkMonitor();
                NetworkMonitor.NetworkStatusChanged += OnNetworkStatusChanged;

                // 3. Вибір провайдера карт
                IMapProvider provider = Configuration.DefaultProvider switch
                {
                    "Google" => new GoogleMapProvider(),
                    "Bing" => new BingMapProvider(),
                    _ => new OpenStreetMapProvider()
                };

                // 4. Ініціалізація сервісів
                OfflineCache = new OfflineCacheService(_cacheRepository, provider);
                MapService = new MapTileService(provider, OfflineCache, NetworkMonitor);
                GeoCoder = new GeoCoderService(Configuration.NominatimUrl, NetworkMonitor);
                DataService = new MapDataService();

                // 5. Очищення старого кешу
                if (Configuration.CacheExpirationDays > 0)
                {
                    await OfflineCache.ClearOldCacheAsync(Configuration.CacheExpirationDays);
                }

                IsInitialized = true;
                Console.WriteLine($"✅ {ModuleName} initialized successfully");

                // Показ статистики
                await ShowStatisticsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error initializing {ModuleName}: {ex.Message}");
                throw;
            }
        }

        public async Task ShutdownAsync()
        {
            if (!IsInitialized)
                return;

            Console.WriteLine($"🗺️ Shutting down {ModuleName}...");

            try
            {
                _cacheRepository?.Dispose();

                if (NetworkMonitor != null)
                {
                    NetworkMonitor.NetworkStatusChanged -= OnNetworkStatusChanged;
                }

                IsInitialized = false;
                Console.WriteLine($"✅ {ModuleName} shutdown complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error shutting down {ModuleName}: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private void LoadConfiguration()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps.config.json");

            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    Configuration = JsonSerializer.Deserialize<MapConfiguration>(json);
                    Console.WriteLine($"📄 Loaded configuration from {configPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Error loading config: {ex.Message}. Using defaults.");
                    Configuration = new MapConfiguration();
                }
            }
            else
            {
                Configuration = new MapConfiguration();
                SaveConfiguration();
            }
        }

        public void SaveConfiguration()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maps.config.json");
                string json = JsonSerializer.Serialize(Configuration, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
                Console.WriteLine($"💾 Configuration saved to {configPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error saving configuration: {ex.Message}");
            }
        }

        private async Task ShowStatisticsAsync()
        {
            try
            {
                long cacheSize = await OfflineCache.GetCacheSizeAsync();
                double cacheSizeMB = cacheSize / (1024.0 * 1024.0);

                Console.WriteLine($"📊 Cache size: {cacheSizeMB:F2} MB");
                Console.WriteLine($"🌐 Network: {(NetworkMonitor.IsNetworkAvailable() ? "Online" : "Offline")}");
                Console.WriteLine($"🗺️ Provider: {MapService.CurrentProvider.ProviderName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error showing statistics: {ex.Message}");
            }
        }

        private void OnNetworkStatusChanged(object sender, bool isOnline)
        {
            Console.WriteLine($"🌐 Network status changed: {(isOnline ? "Online" : "Offline")}");
        }
    }
}