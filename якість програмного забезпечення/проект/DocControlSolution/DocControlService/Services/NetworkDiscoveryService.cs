using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DocControlService.Services
{
    /// <summary>
    /// Сервіс для виявлення пристроїв та мереж
    /// </summary>
    public class NetworkDiscoveryService
    {
        public List<NetworkInterfaceInfo> GetNetworkInterfaces()
        {
            var interfaces = new List<NetworkInterfaceInfo>();

            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;

                    var ipProps = nic.GetIPProperties();
                    var ipv4 = ipProps.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (ipv4 != null)
                    {
                        interfaces.Add(new NetworkInterfaceInfo
                        {
                            Name = nic.Name,
                            IpAddress = ipv4.Address.ToString(),
                            MacAddress = BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()),
                            NetworkType = nic.NetworkInterfaceType.ToString(),
                            IsActive = nic.OperationalStatus == OperationalStatus.Up
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка отримання мережевих інтерфейсів: {ex.Message}");
            }

            return interfaces;
        }

        public async Task<List<NetworkDevice>> ScanNetworkAsync()
        {
            var devices = new List<NetworkDevice>();

            try
            {
                // Отримуємо локальну IP адресу
                var localIp = GetLocalIPAddress();
                if (localIp == null)
                {
                    Console.WriteLine("Не вдалося визначити локальну IP адресу");
                    return devices;
                }

                Console.WriteLine($"Сканування мережі з IP: {localIp}");

                // Отримуємо підмережу (наприклад 192.168.1.x)
                var ipParts = localIp.Split('.');
                var subnet = $"{ipParts[0]}.{ipParts[1]}.{ipParts[2]}";

                // Сканування ARP таблиці (швидко)
                devices.AddRange(GetArpTable());

                // Додатково ping сканування (повільніше, але знаходить більше)
                await PingScanSubnet(subnet, devices);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка сканування мережі: {ex.Message}");
            }

            return devices.DistinctBy(d => d.IpAddress).ToList();
        }

        private string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка отримання IP: {ex.Message}");
            }
            return null;
        }

        private List<NetworkDevice> GetArpTable()
        {
            var devices = new List<NetworkDevice>();

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = "-a",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    // Парсимо рядки типу: "192.168.1.1   00-11-22-33-44-55   dynamic"
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out var ip))
                    {
                        var mac = parts[1];
                        if (mac.Contains("-") || mac.Contains(":"))
                        {
                            devices.Add(new NetworkDevice
                            {
                                IpAddress = ip.ToString(),
                                MacAddress = mac,
                                IsOnline = true,
                                LastSeen = DateTime.Now
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка читання ARP таблиці: {ex.Message}");
            }

            return devices;
        }

        private async Task PingScanSubnet(string subnet, List<NetworkDevice> devices)
        {
            var pingTasks = new List<Task>();

            for (int i = 1; i <= 254; i++)
            {
                string ip = $"{subnet}.{i}";

                pingTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var ping = new Ping();
                        var reply = await ping.SendPingAsync(ip, 100);

                        if (reply.Status == IPStatus.Success)
                        {

                            string hostName = await GetHostName(ip);

                            // Перевіряємо чи вже є в списку
                            lock (devices)
                            {
                                if (!devices.Any(d => d.IpAddress == ip))
                                {
                                    devices.Add(new NetworkDevice
                                    {
                                        IpAddress = ip,
                                        MacAddress = "Unknown",
                                        HostName = hostName,
                                        IsOnline = true,
                                        LastSeen = DateTime.Now
                                    });
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ігноруємо помилки ping
                    }
                }));
            }

            await Task.WhenAll(pingTasks);
        }

        private async Task<string> GetHostName(string ipAddress)
        {
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                return hostEntry.HostName;
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}