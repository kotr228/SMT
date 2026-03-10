using DocControlNetworkCore.Models;
using DocControlService.Shared;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DocControlNetworkCore.Services
{
    /// <summary>
    /// Сервіс для виявлення інших вузлів у локальній мережі
    /// </summary>
    public class DiscoveryService : IDisposable
    {
        private readonly PeerIdentity _localIdentity;
        private readonly int _udpPort;
        private UdpClient? _udpListener;
        private UdpClient? _udpBroadcaster;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listenerTask;
        private Task? _broadcasterTask;

        /// <summary>
        /// Подія виявлення нового вузла
        /// </summary>
        public event Action<PeerIdentity>? PeerDiscovered;

        /// <summary>
        /// Подія отримання heartbeat від існуючого вузла
        /// </summary>
        public event Action<PeerIdentity>? PeerHeartbeat;

        /// <summary>
        /// Інтервал broadcast повідомлень (в секундах)
        /// </summary>
        public int BroadcastIntervalSeconds { get; set; } = 10;

        public DiscoveryService(PeerIdentity localIdentity, int udpPort = 9000)
        {
            _localIdentity = localIdentity;
            _udpPort = udpPort;
        }

        /// <summary>
        /// Запустити сервіс виявлення
        /// </summary>
        public void Start()
        {
            if (_cancellationTokenSource != null)
            {
                Console.WriteLine("[Discovery] Сервіс вже запущено");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            // Запуск UDP Listener
            _listenerTask = Task.Run(() => RunListenerAsync(_cancellationTokenSource.Token));

            // Запуск UDP Broadcaster
            _broadcasterTask = Task.Run(() => RunBroadcasterAsync(_cancellationTokenSource.Token));

            Console.WriteLine($"[Discovery] Сервіс запущено на UDP порту {_udpPort}");
        }

        /// <summary>
        /// Зупинити сервіс виявлення
        /// </summary>
        public void Stop()
        {
            Console.WriteLine("[Discovery] Зупинка сервісу...");

            _cancellationTokenSource?.Cancel();

            try
            {
                Task.WaitAll(new[] { _listenerTask, _broadcasterTask }
                    .Where(t => t != null).ToArray()!,
                    TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Помилка при зупинці: {ex.Message}");
            }

            _udpListener?.Close();
            _udpBroadcaster?.Close();

            Console.WriteLine("[Discovery] Сервіс зупинено");
        }

        /// <summary>
        /// UDP Listener - слухає повідомлення від інших вузлів
        /// </summary>
        private async Task RunListenerAsync(CancellationToken cancellationToken)
        {
            try
            {
                _udpListener = new UdpClient(_udpPort);
                _udpListener.EnableBroadcast = true;

                Console.WriteLine($"[Discovery] UDP Listener запущено на порту {_udpPort}");

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = await _udpListener.ReceiveAsync(cancellationToken);
                        var message = Encoding.UTF8.GetString(result.Buffer);

                        // Парсинг отриманого повідомлення
                        var peerIdentity = JsonSerializer.Deserialize<PeerIdentity>(message);

                        if (peerIdentity != null)
                        {
                            // Фільтрація власних пакетів
                            if (peerIdentity.IsSelf(_localIdentity.InstanceId))
                            {
                                continue; // Ігнорувати самого себе
                            }

                            // Оновити IP адресу з фактичної адреси відправника
                            peerIdentity.IpAddress = result.RemoteEndPoint.Address.ToString();
                            peerIdentity.LastSeen = DateTime.Now;

                            Console.WriteLine($"[Discovery] Отримано повідомлення від {peerIdentity}");

                            // Викликати подію
                            if (PeerDiscovered != null || PeerHeartbeat != null)
                            {
                                PeerDiscovered?.Invoke(peerIdentity);
                                PeerHeartbeat?.Invoke(peerIdentity);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Discovery] Помилка обробки повідомлення: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Критична помилка UDP Listener: {ex.Message}");
            }
            finally
            {
                _udpListener?.Close();
            }
        }

        /// <summary>
        /// UDP Broadcaster - періодично відправляє broadcast повідомлення
        /// </summary>
        private async Task RunBroadcasterAsync(CancellationToken cancellationToken)
        {
            try
            {
                _udpBroadcaster = new UdpClient();
                _udpBroadcaster.EnableBroadcast = true;

                Console.WriteLine("[Discovery] UDP Broadcaster запущено");

                // Відправити одразу при старті
                await SendBroadcastAsync();

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(BroadcastIntervalSeconds), cancellationToken);
                        await SendBroadcastAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Discovery] Помилка відправки broadcast: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Критична помилка UDP Broadcaster: {ex.Message}");
            }
            finally
            {
                _udpBroadcaster?.Close();
            }
        }

        /// <summary>
        /// Відправити broadcast повідомлення
        /// </summary>
        private async Task SendBroadcastAsync()
        {
            try
            {
                var message = JsonSerializer.Serialize(_localIdentity);
                var data = Encoding.UTF8.GetBytes(message);

                var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, _udpPort);
                await _udpBroadcaster!.SendAsync(data, data.Length, broadcastEndpoint);

                Console.WriteLine($"[Discovery] Broadcast відправлено: {_localIdentity.UserName}@{_localIdentity.MachineName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discovery] Помилка відправки broadcast: {ex.Message}");
            }
        }

        /// <summary>
        /// Ручне відправлення broadcast (для форсування оновлення)
        /// </summary>
        public async Task SendImmediateBroadcastAsync()
        {
            await SendBroadcastAsync();
        }

        public void Dispose()
        {
            Stop();
            _udpListener?.Dispose();
            _udpBroadcaster?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
