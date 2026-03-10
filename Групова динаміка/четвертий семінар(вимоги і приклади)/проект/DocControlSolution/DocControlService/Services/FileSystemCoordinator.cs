using DocControlNetworkCore.Services;
using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DocControlService.Services
{
    /// <summary>
    /// Координатор між локальною та мережевою файловими системами
    /// </summary>
    public class FileSystemCoordinator
    {
        private readonly DatabaseManager _dbManager;
        private readonly DeviceRepository _deviceRepository;
        private readonly NetworkAccessRepository _networkAccessRepository;
        private readonly DirectoryRepository _directoryRepository;
        private readonly LocalFileSystemService _localFileSystem;
        private readonly ConcurrentDictionary<Guid, RemoteFileSystemService> _remoteFileSystems;

        // Компоненти NetworkCore
        private SelfIdentityService? _identityService;
        private DiscoveryService? _discoveryService;
        private PeerRegistryService? _peerRegistry;
        private CommandLayerService? _commandLayer;
        private FileTransferService? _fileTransfer;
        private SecurityService? _securityService;

        private PeerIdentity? _localIdentity;
        private bool _isNetworkCoreStarted = false;

        /// <summary>
        /// Подія зміни списку віддалених вузлів
        /// </summary>
        public event Action<List<RemoteNode>>? RemoteNodesChanged;

        public FileSystemCoordinator(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            _deviceRepository = new DeviceRepository(dbManager);
            _networkAccessRepository = new NetworkAccessRepository(dbManager);
            _directoryRepository = new DirectoryRepository(dbManager);
            _localFileSystem = new LocalFileSystemService(dbManager);
            _remoteFileSystems = new ConcurrentDictionary<Guid, RemoteFileSystemService>();
        }

        /// <summary>
        /// Запустити мережеве ядро
        /// </summary>
        public void StartNetworkCore(string sharedDirectory)
        {
            if (_isNetworkCoreStarted)
                return;

            Console.WriteLine("[FileSystemCoordinator] Запуск мережевого ядра...");

            // 1. Ініціалізація ідентифікації
            _identityService = new SelfIdentityService(".");
            _localIdentity = _identityService.GetOrCreateIdentity();

            // 2. Ініціалізація безпеки
            _securityService = new SecurityService(sharedDirectory, whitelistEnabled: false);

            // 3. Ініціалізація реєстру вузлів
            _peerRegistry = new PeerRegistryService(timeoutSeconds: 30);
            _peerRegistry.PeerAdded += OnPeerAdded;
            _peerRegistry.PeerRemoved += OnPeerRemoved;
            _peerRegistry.PeersChanged += OnPeersChanged;
            _peerRegistry.Start();

            // 4. Ініціалізація Discovery Service
            _discoveryService = new DiscoveryService(_localIdentity, _localIdentity.UdpPort);
            _discoveryService.BroadcastIntervalSeconds = 10;
            _discoveryService.PeerDiscovered += OnPeerDiscovered;
            _discoveryService.PeerHeartbeat += (peer) => _peerRegistry?.AddOrUpdatePeer(peer);
            _discoveryService.Start();

            // 5. Ініціалізація Command Layer
            _commandLayer = new CommandLayerService(_localIdentity, sharedDirectory);

            // Підключити перевірку доступу до директорій через БД
            _commandLayer.CheckAccessCallback = (senderIp, requestedPath) =>
            {
                try
                {
                    // Знайти пристрій за IP адресою
                    var allDevices = _deviceRepository.GetAllDevices();
                    var device = allDevices.FirstOrDefault(d => d.Name != null && d.Name.Contains(senderIp));

                    if (device == null)
                    {
                        Console.WriteLine($"[AccessCheck] Пристрій з IP {senderIp} не знайдено в БД");
                        return false; // Пристрій не зареєстрований - заборонити
                    }

                    if (!device.Access)
                    {
                        Console.WriteLine($"[AccessCheck] Пристрій {device.Name} має Access=false");
                        return false; // Пристрій заблокований глобально
                    }

                    // Отримати всі директорії та знайти відповідну за шляхом
                    var directories = _directoryRepository.GetAllDirectories();
                    var targetDirectory = directories.FirstOrDefault(dir =>
                        requestedPath.StartsWith(dir.Browse, StringComparison.OrdinalIgnoreCase));

                    if (targetDirectory == null)
                    {
                        Console.WriteLine($"[AccessCheck] Директорія для шляху {requestedPath} не знайдена");
                        return true; // Якщо директорія не визначена, дозволити доступ (для сумісності)
                    }

                    // Перевірити чи є доступ до цієї директорії в NetworkAccesDirectory
                    var accessList = _networkAccessRepository.GetAccessByDirectory(targetDirectory.Id);
                    var hasAccess = accessList.Any(a => a.DeviceId == device.Id && a.Status);

                    Console.WriteLine($"[AccessCheck] {device.Name} -> {targetDirectory.Name}: {(hasAccess ? "✅ ДОЗВОЛЕНО" : "❌ ЗАБОРОНЕНО")}");
                    return hasAccess;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AccessCheck] Помилка перевірки доступу: {ex.Message}");
                    return false; // При помилці - заборонити доступ
                }
            };

            _commandLayer.Start();

            // 6. Ініціалізація File Transfer
            _fileTransfer = new FileTransferService(sharedDirectory);

            _isNetworkCoreStarted = true;
            Console.WriteLine("[FileSystemCoordinator] Мережеве ядро запущено");
        }

        /// <summary>
        /// Зупинити мережеве ядро
        /// </summary>
        public void StopNetworkCore()
        {
            if (!_isNetworkCoreStarted)
                return;

            Console.WriteLine("[FileSystemCoordinator] Зупинка мережевого ядра...");

            _discoveryService?.Stop();
            _commandLayer?.Stop();
            _peerRegistry?.Stop();

            _discoveryService?.Dispose();
            _commandLayer?.Dispose();
            _peerRegistry?.Dispose();

            _isNetworkCoreStarted = false;
            Console.WriteLine("[FileSystemCoordinator] Мережеве ядро зупинено");
        }

        /// <summary>
        /// Отримати локальну файлову систему
        /// </summary>
        public IFileSystemService GetLocalFileSystem()
        {
            return _localFileSystem;
        }

        /// <summary>
        /// Отримати віддалену файлову систему за ID вузла
        /// </summary>
        public IFileSystemService? GetRemoteFileSystem(Guid peerId)
        {
            if (_remoteFileSystems.TryGetValue(peerId, out var remoteFs))
                return remoteFs;

            return null;
        }

        /// <summary>
        /// Отримати всі активні віддалені вузли
        /// </summary>
        public List<RemoteNode> GetRemoteNodes()
        {
            if (_peerRegistry == null)
                return new List<RemoteNode>();

            var peers = _peerRegistry.GetAllPeers();
            return peers.Select(p => new RemoteNode
            {
                InstanceId = p.InstanceId,
                UserName = p.UserName,
                MachineName = p.MachineName,
                IpAddress = p.IpAddress,
                TcpPort = p.TcpPort,
                IsOnline = true,
                LastSeen = p.LastSeen
            }).ToList();
        }

        /// <summary>
        /// Чи запущено мережеве ядро
        /// </summary>
        public bool IsNetworkCoreRunning => _isNetworkCoreStarted;

        /// <summary>
        /// Отримати локальну ідентичність
        /// </summary>
        public PeerIdentity? GetLocalIdentity() => _localIdentity;

        #region Event Handlers

        private void OnPeerDiscovered(PeerIdentity peer)
        {
            Console.WriteLine($"[FileSystemCoordinator] Виявлено вузол: {peer}");
            _peerRegistry?.AddOrUpdatePeer(peer);
        }

        private void OnPeerAdded(PeerIdentity peer)
        {
            Console.WriteLine($"[FileSystemCoordinator] Вузол приєднався: {peer}");
            LogToEventLog($"Вузол приєднався: {peer}", EventLogEntryType.Information);

            // Перевірка чи це не власний вузол (не зберігаємо сам себе)
            bool isSelf = false;
            if (_localIdentity != null)
            {
                Console.WriteLine($"[DEBUG] Порівняння вузлів:");
                Console.WriteLine($"  Peer:  {peer.UserName}@{peer.MachineName} ({peer.IpAddress})");
                Console.WriteLine($"  Local: {_localIdentity.UserName}@{_localIdentity.MachineName} ({_localIdentity.IpAddress})");

                // Порівнюємо UserName, MachineName та IpAddress
                if (peer.UserName == _localIdentity.UserName &&
                    peer.MachineName == _localIdentity.MachineName &&
                    peer.IpAddress == _localIdentity.IpAddress)
                {
                    isSelf = true;
                    Console.WriteLine($"[FileSystemCoordinator] ⚠️ Пропускаємо власний вузол: {peer}");
                    LogToEventLog($"Пропущено власний вузол (не зберігається в БД): {peer}", EventLogEntryType.Information);
                }
                else
                {
                    Console.WriteLine($"[DEBUG] ✅ Вузол НЕ є власним, буде збережено в БД");
                }
            }

            // Зберегти пристрій в БД тільки якщо це НЕ власний вузол
            if (!isSelf)
            {
                try
                {
                    string deviceName = $"{peer.UserName}@{peer.MachineName} ({peer.IpAddress})";
                    Console.WriteLine($"[DEBUG] 💾 Спроба збереження пристрою в БД: {deviceName}");
                    LogToEventLog($"Спроба збереження пристрою: {deviceName}", EventLogEntryType.Information);

                    var device = _deviceRepository.GetOrCreateDevice(deviceName, defaultAccess: false);

                    Console.WriteLine($"[FileSystemCoordinator] ✅ Пристрій збережено в БД: {deviceName}, ID={device.Id}, Access={device.Access}");
                    LogToEventLog($"Пристрій збережено в БД: {deviceName}, ID={device.Id}, Access={device.Access}", EventLogEntryType.Information);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileSystemCoordinator] ❌ ПОМИЛКА збереження пристрою: {ex.Message}");
                    Console.WriteLine($"   StackTrace: {ex.StackTrace}");
                    LogToEventLog($"ПОМИЛКА збереження пристрою: {ex.Message}\nStack: {ex.StackTrace}", EventLogEntryType.Error);
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] ⏭️ Пропускаємо збереження (власний вузол)");
            }

            // Створити RemoteFileSystemService для нового вузла
            if (_commandLayer != null && _fileTransfer != null)
            {
                var remoteFs = new RemoteFileSystemService(peer, _commandLayer, _fileTransfer);
                _remoteFileSystems.TryAdd(peer.InstanceId, remoteFs);
            }

            // Викликати подію для UI
            NotifyRemoteNodesChanged();
        }

        private void OnPeerRemoved(PeerIdentity peer)
        {
            Console.WriteLine($"[FileSystemCoordinator] Вузол відключився: {peer}");

            // Видалити RemoteFileSystemService
            _remoteFileSystems.TryRemove(peer.InstanceId, out _);

            // Викликати подію для UI
            NotifyRemoteNodesChanged();
        }

        private void OnPeersChanged(List<PeerIdentity> peers)
        {
            NotifyRemoteNodesChanged();
        }

        private void NotifyRemoteNodesChanged()
        {
            var nodes = GetRemoteNodes();
            RemoteNodesChanged?.Invoke(nodes);
        }

        private void LogToEventLog(string message, EventLogEntryType type)
        {
            try
            {
                if (!EventLog.SourceExists("DocControlService"))
                {
                    EventLog.CreateEventSource("DocControlService", "Application");
                }
                EventLog.WriteEntry("DocControlService", $"[NetworkCore] {message}", type);
            }
            catch
            {
                // Якщо не вдалось записати в EventLog - ігноруємо
            }
        }

        #endregion
    }
}
