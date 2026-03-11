using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace DocControlNetworkCore.Services
{
    /// <summary>
    /// Сервіс безпеки для валідації шляхів та контролю доступу
    /// </summary>
    public class SecurityService
    {
        private readonly string _allowedBasePath;
        private readonly HashSet<string> _ipWhitelist;
        private bool _whitelistEnabled;

        /// <summary>
        /// Подія спроби несанкціонованого доступу
        /// </summary>
        public event Action<string, string>? UnauthorizedAccessAttempt;

        public SecurityService(string allowedBasePath, bool whitelistEnabled = false)
        {
            _allowedBasePath = Path.GetFullPath(allowedBasePath);
            _ipWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _whitelistEnabled = whitelistEnabled;

            Console.WriteLine($"[Security] Дозволена базова директорія: {_allowedBasePath}");
            Console.WriteLine($"[Security] Whitelist: {(_whitelistEnabled ? "Увімкнено" : "Вимкнено")}");
        }

        /// <summary>
        /// Валідація шляху - перевірка що шлях не виходить за межі дозволеної директорії
        /// </summary>
        public bool ValidatePath(string relativePath, out string fullPath)
        {
            try
            {
                // Отримати абсолютний шлях
                fullPath = Path.GetFullPath(Path.Combine(_allowedBasePath, relativePath));

                // Перевірити що шлях не виходить за межі дозволеної директорії
                if (!fullPath.StartsWith(_allowedBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Security] ❌ Відхилено шлях (поза базовою директорією): {relativePath}");
                    UnauthorizedAccessAttempt?.Invoke(relativePath, "Path outside allowed directory");
                    return false;
                }

                // Перевірити на небезпечні паттерни
                if (ContainsDangerousPatterns(relativePath))
                {
                    Console.WriteLine($"[Security] ❌ Відхилено шлях (небезпечний паттерн): {relativePath}");
                    UnauthorizedAccessAttempt?.Invoke(relativePath, "Dangerous pattern detected");
                    return false;
                }

                Console.WriteLine($"[Security] ✓ Шлях валідний: {relativePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Security] Помилка валідації шляху: {ex.Message}");
                fullPath = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Перевірка на небезпечні паттерни в шляху
        /// </summary>
        private bool ContainsDangerousPatterns(string path)
        {
            var dangerousPatterns = new[]
            {
                "..",           // Спроба вийти на рівень вище
                "~",            // Домашня директорія
                "$",            // Змінні оточення
                "%",            // Змінні Windows
                ":",            // Абсолютний шлях (C:, D: тощо)
                "//",           // UNC шляхи
                "\\\\",         // UNC шляхи
            };

            foreach (var pattern in dangerousPatterns)
            {
                if (path.Contains(pattern))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Перевірка чи IP адреса знаходиться в whitelist
        /// </summary>
        public bool CheckIpAccess(string ipAddress)
        {
            // Якщо whitelist вимкнено, дозволити всім
            if (!_whitelistEnabled)
                return true;

            // Завжди дозволяти localhost
            if (IsLocalhost(ipAddress))
                return true;

            // Перевірити whitelist
            bool allowed = _ipWhitelist.Contains(ipAddress);

            if (!allowed)
            {
                Console.WriteLine($"[Security] ❌ Доступ відхилено для IP: {ipAddress}");
                UnauthorizedAccessAttempt?.Invoke(ipAddress, "IP not in whitelist");
            }

            return allowed;
        }

        /// <summary>
        /// Додати IP до whitelist
        /// </summary>
        public void AddToWhitelist(string ipAddress)
        {
            if (_ipWhitelist.Add(ipAddress))
            {
                Console.WriteLine($"[Security] IP додано до whitelist: {ipAddress}");
            }
        }

        /// <summary>
        /// Видалити IP з whitelist
        /// </summary>
        public void RemoveFromWhitelist(string ipAddress)
        {
            if (_ipWhitelist.Remove(ipAddress))
            {
                Console.WriteLine($"[Security] IP видалено з whitelist: {ipAddress}");
            }
        }

        /// <summary>
        /// Отримати список IP у whitelist
        /// </summary>
        public List<string> GetWhitelist()
        {
            return _ipWhitelist.ToList();
        }

        /// <summary>
        /// Очистити whitelist
        /// </summary>
        public void ClearWhitelist()
        {
            _ipWhitelist.Clear();
            Console.WriteLine("[Security] Whitelist очищено");
        }

        /// <summary>
        /// Увімкнути/вимкнути whitelist
        /// </summary>
        public void SetWhitelistEnabled(bool enabled)
        {
            _whitelistEnabled = enabled;
            Console.WriteLine($"[Security] Whitelist {(enabled ? "увімкнено" : "вимкнено")}");
        }

        /// <summary>
        /// Перевірити чи це localhost
        /// </summary>
        private bool IsLocalhost(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;

            return ipAddress == "127.0.0.1" ||
                   ipAddress == "::1" ||
                   ipAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Перевірити чи файл має дозволене розширення
        /// </summary>
        public bool IsFileExtensionAllowed(string filePath, string[] allowedExtensions)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (allowedExtensions == null || allowedExtensions.Length == 0)
                return true; // Якщо список порожній, дозволити всі

            bool allowed = allowedExtensions.Any(ext =>
                ext.Equals(extension, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                Console.WriteLine($"[Security] ❌ Розширення файлу не дозволено: {extension}");
                UnauthorizedAccessAttempt?.Invoke(filePath, $"File extension not allowed: {extension}");
            }

            return allowed;
        }

        /// <summary>
        /// Перевірити розмір файлу
        /// </summary>
        public bool ValidateFileSize(string fullPath, long maxSizeBytes)
        {
            if (!File.Exists(fullPath))
                return false;

            var fileInfo = new FileInfo(fullPath);

            if (fileInfo.Length > maxSizeBytes)
            {
                Console.WriteLine($"[Security] ❌ Файл занадто великий: {fileInfo.Length} > {maxSizeBytes}");
                UnauthorizedAccessAttempt?.Invoke(fullPath, $"File too large: {fileInfo.Length} bytes");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Логування спроби доступу
        /// </summary>
        public void LogAccessAttempt(string ipAddress, string resource, bool granted)
        {
            var status = granted ? "✓ Дозволено" : "❌ Відхилено";
            Console.WriteLine($"[Security] {status} | IP: {ipAddress} | Resource: {resource}");
        }
    }
}
