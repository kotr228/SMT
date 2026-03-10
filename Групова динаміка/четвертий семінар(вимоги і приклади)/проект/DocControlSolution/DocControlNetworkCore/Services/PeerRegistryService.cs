using DocControlNetworkCore.Models;
using DocControlService.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocControlNetworkCore.Services
{
    /// <summary>
    /// Реєстр активних користувачів (вузлів) у мережі
    /// </summary>
    public class PeerRegistryService : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, PeerIdentity> _peers;
        private readonly int _timeoutSeconds;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _cleanupTask;

        /// <summary>
        /// Подія додавання нового вузла
        /// </summary>
        public event Action<PeerIdentity>? PeerAdded;

        /// <summary>
        /// Подія оновлення існуючого вузла
        /// </summary>
        public event Action<PeerIdentity>? PeerUpdated;

        /// <summary>
        /// Подія видалення вузла (тайм-аут)
        /// </summary>
        public event Action<PeerIdentity>? PeerRemoved;

        /// <summary>
        /// Подія зміни списку вузлів (для UI)
        /// </summary>
        public event Action<List<PeerIdentity>>? PeersChanged;

        public PeerRegistryService(int timeoutSeconds = 30)
        {
            _peers = new ConcurrentDictionary<Guid, PeerIdentity>();
            _timeoutSeconds = timeoutSeconds;
        }

        /// <summary>
        /// Запустити моніторинг вузлів
        /// </summary>
        public void Start()
        {
            if (_cancellationTokenSource != null)
            {
                Console.WriteLine("[PeerRegistry] Сервіс вже запущено");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _cleanupTask = Task.Run(() => RunCleanupAsync(_cancellationTokenSource.Token));

            Console.WriteLine($"[PeerRegistry] Сервіс запущено (timeout: {_timeoutSeconds}s)");
        }

        /// <summary>
        /// Зупинити моніторинг
        /// </summary>
        public void Stop()
        {
            Console.WriteLine("[PeerRegistry] Зупинка сервісу...");

            _cancellationTokenSource?.Cancel();

            try
            {
                _cleanupTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PeerRegistry] Помилка при зупинці: {ex.Message}");
            }

            Console.WriteLine("[PeerRegistry] Сервіс зупинено");
        }

        /// <summary>
        /// Додати або оновити вузол
        /// </summary>
        public void AddOrUpdatePeer(PeerIdentity peer)
        {
            bool isNew = !_peers.ContainsKey(peer.InstanceId);

            peer.LastSeen = DateTime.Now;

            _peers.AddOrUpdate(
                peer.InstanceId,
                peer,
                (key, existingPeer) =>
                {
                    // Оновлення існуючого вузла
                    existingPeer.LastSeen = peer.LastSeen;
                    existingPeer.IpAddress = peer.IpAddress;
                    existingPeer.TcpPort = peer.TcpPort;
                    existingPeer.UdpPort = peer.UdpPort;
                    return existingPeer;
                });

            if (isNew)
            {
                Console.WriteLine($"[PeerRegistry] Новий вузол: {peer}");
                PeerAdded?.Invoke(peer);
            }
            else
            {
                Console.WriteLine($"[PeerRegistry] Оновлення вузла: {peer}");
                PeerUpdated?.Invoke(peer);
            }

            // Викликати подію зміни списку
            PeersChanged?.Invoke(GetAllPeers());
        }

        /// <summary>
        /// Отримати вузол за ID
        /// </summary>
        public PeerIdentity? GetPeer(Guid instanceId)
        {
            _peers.TryGetValue(instanceId, out var peer);
            return peer;
        }

        /// <summary>
        /// Отримати всі активні вузли
        /// </summary>
        public List<PeerIdentity> GetAllPeers()
        {
            return _peers.Values.OrderBy(p => p.UserName).ToList();
        }

        /// <summary>
        /// Отримати кількість активних вузлів
        /// </summary>
        public int GetPeerCount()
        {
            return _peers.Count;
        }

        /// <summary>
        /// Видалити вузол
        /// </summary>
        public void RemovePeer(Guid instanceId)
        {
            if (_peers.TryRemove(instanceId, out var peer))
            {
                Console.WriteLine($"[PeerRegistry] Вузол видалено: {peer}");
                PeerRemoved?.Invoke(peer);
                PeersChanged?.Invoke(GetAllPeers());
            }
        }

        /// <summary>
        /// Очистити всі вузли
        /// </summary>
        public void Clear()
        {
            _peers.Clear();
            PeersChanged?.Invoke(new List<PeerIdentity>());
            Console.WriteLine("[PeerRegistry] Реєстр очищено");
        }

        /// <summary>
        /// Періодична перевірка та видалення неактивних вузлів
        /// </summary>
        private async Task RunCleanupAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("[PeerRegistry] Cleanup task запущено");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                    var now = DateTime.Now;
                    var timeoutThreshold = now.AddSeconds(-_timeoutSeconds);

                    // Знайти неактивні вузли
                    var inactivePeers = _peers.Values
                        .Where(p => p.LastSeen < timeoutThreshold)
                        .ToList();

                    foreach (var peer in inactivePeers)
                    {
                        Console.WriteLine($"[PeerRegistry] Вузол неактивний (timeout): {peer}");
                        RemovePeer(peer.InstanceId);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PeerRegistry] Помилка cleanup: {ex.Message}");
                }
            }

            Console.WriteLine("[PeerRegistry] Cleanup task зупинено");
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}
