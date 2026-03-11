using DocControlNetworkCore.Models;
using DocControlService.Shared;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace DocControlNetworkCore.Services
{
    /// <summary>
    /// Сервіс для ідентифікації та конфігурації локального вузла
    /// </summary>
    public class SelfIdentityService
    {
        private const string ConfigFileName = "network_identity.json";
        private readonly string _configPath;
        private PeerIdentity? _identity;

        public SelfIdentityService(string configDirectory = ".")
        {
            _configPath = Path.Combine(configDirectory, ConfigFileName);
        }

        /// <summary>
        /// Отримати або створити ідентифікаційні дані
        /// </summary>
        public PeerIdentity GetOrCreateIdentity(int? preferredTcpPort = null, int? preferredUdpPort = null)
        {
            if (_identity != null)
                return _identity;

            // Спробувати завантажити існуючий ідентифікатор
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    _identity = JsonSerializer.Deserialize<PeerIdentity>(json);

                    if (_identity != null)
                    {
                        // Оновити динамічні дані
                        _identity.IpAddress = GetLocalIpAddress();
                        _identity.LastSeen = DateTime.Now;

                        Console.WriteLine($"[SelfIdentity] Завантажено існуючий ідентифікатор: {_identity.InstanceId}");
                        return _identity;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SelfIdentity] Помилка завантаження ідентифікатора: {ex.Message}");
                }
            }

            // Створити новий ідентифікатор
            _identity = new PeerIdentity
            {
                InstanceId = Guid.NewGuid(),
                UserName = Environment.UserName,
                MachineName = Environment.MachineName,
                IpAddress = GetLocalIpAddress(),
                TcpPort = preferredTcpPort ?? FindAvailablePort(8000),
                UdpPort = preferredUdpPort ?? FindAvailablePort(9000),
                LastSeen = DateTime.Now
            };

            // Зберегти ідентифікатор
            SaveIdentity();

            Console.WriteLine($"[SelfIdentity] Створено новий ідентифікатор: {_identity.InstanceId}");
            Console.WriteLine($"[SelfIdentity] {_identity}");

            return _identity;
        }

        /// <summary>
        /// Отримати поточний ідентифікатор
        /// </summary>
        public PeerIdentity? GetCurrentIdentity()
        {
            return _identity;
        }

        /// <summary>
        /// Оновити IP адресу (наприклад, при зміні мережі)
        /// </summary>
        public void RefreshIpAddress()
        {
            if (_identity != null)
            {
                var newIp = GetLocalIpAddress();
                if (_identity.IpAddress != newIp)
                {
                    Console.WriteLine($"[SelfIdentity] IP змінилася: {_identity.IpAddress} -> {newIp}");
                    _identity.IpAddress = newIp;
                    SaveIdentity();
                }
            }
        }

        /// <summary>
        /// Зберегти ідентифікатор у файл
        /// </summary>
        private void SaveIdentity()
        {
            if (_identity == null)
                return;

            try
            {
                var json = JsonSerializer.Serialize(_identity, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_configPath, json);
                Console.WriteLine($"[SelfIdentity] Ідентифікатор збережено: {_configPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SelfIdentity] Помилка збереження ідентифікатора: {ex.Message}");
            }
        }

        /// <summary>
        /// Отримати локальну IP адресу
        /// </summary>
        private string GetLocalIpAddress()
        {
            try
            {
                // Спробувати знайти активний мережевий інтерфейс
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip) &&
                        !ip.ToString().StartsWith("169.254")) // Ігнорувати APIPA
                    {
                        return ip.ToString();
                    }
                }

                // Якщо не знайдено, використати перший доступний
                var activeInterface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                         ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                if (activeInterface != null)
                {
                    var ipProps = activeInterface.GetIPProperties();
                    var ipv4 = ipProps.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (ipv4 != null)
                        return ipv4.Address.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SelfIdentity] Помилка отримання IP: {ex.Message}");
            }

            return "127.0.0.1"; // Fallback
        }

        /// <summary>
        /// Знайти вільний порт
        /// </summary>
        private int FindAvailablePort(int startPort)
        {
            for (int port = startPort; port < startPort + 1000; port++)
            {
                if (IsPortAvailable(port))
                    return port;
            }

            throw new InvalidOperationException($"Не вдалося знайти вільний порт починаючи з {startPort}");
        }

        /// <summary>
        /// Перевірити чи порт доступний
        /// </summary>
        private bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
