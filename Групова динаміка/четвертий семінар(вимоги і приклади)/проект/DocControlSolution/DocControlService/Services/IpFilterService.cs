using DocControlService.Models;
using DocControlService.Shared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace DocControlService.Services
{
    /// <summary>
    /// Сервіс для IP-based фільтрації доступу
    /// </summary>
    public class IpFilterService
    {
        private readonly DatabaseManager _db;

        public IpFilterService(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #region CRUD Operations

        public int AddRule(IpFilterRule rule)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO IpFilterRules 
                (ruleName, ipAddress, action, isEnabled, description, directoryId, geoRoadmapId, createdAt)
                VALUES (@name, @ip, @action, @enabled, @desc, @dirId, @geoId, @time);";

            cmd.Parameters.AddWithValue("@name", rule.RuleName);
            cmd.Parameters.AddWithValue("@ip", rule.IpAddress);
            cmd.Parameters.AddWithValue("@action", rule.Action.ToString());
            cmd.Parameters.AddWithValue("@enabled", rule.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@desc", rule.Description ?? "");
            cmd.Parameters.AddWithValue("@dirId", rule.DirectoryId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@geoId", rule.GeoRoadmapId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<IpFilterRule> GetAllRules()
        {
            var rules = new List<IpFilterRule>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, ruleName, ipAddress, action, isEnabled, description, 
                       createdAt, directoryId, geoRoadmapId
                FROM IpFilterRules 
                ORDER BY createdAt DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rules.Add(new IpFilterRule
                {
                    Id = reader.GetInt32(0),
                    RuleName = reader.GetString(1),
                    IpAddress = reader.GetString(2),
                    Action = Enum.Parse<IpFilterAction>(reader.GetString(3)),
                    IsEnabled = reader.GetInt32(4) == 1,
                    Description = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    CreatedAt = DateTime.Parse(reader.GetString(6)),
                    DirectoryId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    GeoRoadmapId = reader.IsDBNull(8) ? null : reader.GetInt32(8)
                });
            }

            return rules;
        }

        public bool UpdateRule(IpFilterRule rule)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE IpFilterRules 
                SET ruleName = @name, ipAddress = @ip, action = @action, 
                    isEnabled = @enabled, description = @desc
                WHERE id = @id;";

            cmd.Parameters.AddWithValue("@id", rule.Id);
            cmd.Parameters.AddWithValue("@name", rule.RuleName);
            cmd.Parameters.AddWithValue("@ip", rule.IpAddress);
            cmd.Parameters.AddWithValue("@action", rule.Action.ToString());
            cmd.Parameters.AddWithValue("@enabled", rule.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@desc", rule.Description ?? "");

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteRule(int ruleId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM IpFilterRules WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", ruleId);

            return cmd.ExecuteNonQuery() > 0;
        }

        #endregion

        #region Access Check

        /// <summary>
        /// Перевірка доступу для IP адреси
        /// </summary>
        public bool CheckAccess(string ipAddress, int? directoryId = null, int? geoRoadmapId = null)
        {
            var rules = GetAllRules()
                .Where(r => r.IsEnabled)
                .OrderByDescending(r => GetRuleSpecificity(r, directoryId, geoRoadmapId))
                .ToList();

            foreach (var rule in rules)
            {
                // Перевіряємо чи правило застосовується до даного контексту
                if (rule.DirectoryId.HasValue && rule.DirectoryId != directoryId)
                    continue;

                if (rule.GeoRoadmapId.HasValue && rule.GeoRoadmapId != geoRoadmapId)
                    continue;

                // Перевіряємо чи IP підходить під правило
                if (IpMatches(ipAddress, rule.IpAddress))
                {
                    return rule.Action == IpFilterAction.Allow;
                }
            }

            // За замовчуванням дозволяємо доступ
            return true;
        }

        /// <summary>
        /// Визначає специфічність правила (більш специфічні мають вищий пріоритет)
        /// </summary>
        private int GetRuleSpecificity(IpFilterRule rule, int? directoryId, int? geoRoadmapId)
        {
            int specificity = 0;

            // Правила для конкретної геокарти найбільш специфічні
            if (rule.GeoRoadmapId.HasValue && rule.GeoRoadmapId == geoRoadmapId)
                specificity += 100;

            // Правила для конкретної директорії середньої специфічності
            if (rule.DirectoryId.HasValue && rule.DirectoryId == directoryId)
                specificity += 50;

            // Правила з конкретною IP адресою більш специфічні ніж CIDR
            if (!rule.IpAddress.Contains("/"))
                specificity += 10;

            return specificity;
        }

        /// <summary>
        /// Перевірка чи IP адреса відповідає шаблону (підтримує CIDR)
        /// </summary>
        private bool IpMatches(string ipAddress, string pattern)
        {
            try
            {
                // Якщо pattern містить CIDR нотацію (192.168.1.0/24)
                if (pattern.Contains("/"))
                {
                    return IpMatchesCidr(ipAddress, pattern);
                }

                // Пряме співставлення
                if (pattern == ipAddress)
                    return true;

                // Wildcard підтримка (192.168.1.*)
                if (pattern.Contains("*"))
                {
                    var regexPattern = "^" + pattern.Replace(".", "\\.").Replace("*", ".*") + "$";
                    return System.Text.RegularExpressions.Regex.IsMatch(ipAddress, regexPattern);
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка перевірки IP: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Перевірка чи IP входить в CIDR блок
        /// </summary>
        private bool IpMatchesCidr(string ipAddress, string cidr)
        {
            try
            {
                var parts = cidr.Split('/');
                if (parts.Length != 2)
                    return false;

                var networkAddress = IPAddress.Parse(parts[0]);
                var maskBits = int.Parse(parts[1]);
                var testAddress = IPAddress.Parse(ipAddress);

                // Перетворюємо в uint для побітових операцій
                var networkBytes = networkAddress.GetAddressBytes();
                var testBytes = testAddress.GetAddressBytes();

                if (networkBytes.Length != testBytes.Length)
                    return false;

                // Створюємо маску
                uint mask = maskBits == 0 ? 0 : ~(uint.MaxValue >> maskBits);

                // IPv4
                if (networkBytes.Length == 4)
                {
                    uint networkInt = BitConverter.ToUInt32(networkBytes.Reverse().ToArray(), 0);
                    uint testInt = BitConverter.ToUInt32(testBytes.Reverse().ToArray(), 0);

                    return (networkInt & mask) == (testInt & mask);
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка CIDR перевірки: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Bulk Operations

        /// <summary>
        /// Додавання правил для списку IP
        /// </summary>
        public void AddBulkRules(List<string> ipAddresses, IpFilterAction action, string description)
        {
            foreach (var ip in ipAddresses)
            {
                var rule = new IpFilterRule
                {
                    RuleName = $"Bulk rule for {ip}",
                    IpAddress = ip,
                    Action = action,
                    IsEnabled = true,
                    Description = description,
                    CreatedAt = DateTime.Now
                };

                AddRule(rule);
            }
        }

        /// <summary>
        /// Блокування IP адреси
        /// </summary>
        public int BlockIpAddress(string ipAddress, string reason = "Заблоковано адміністратором")
        {
            var rule = new IpFilterRule
            {
                RuleName = $"Block {ipAddress}",
                IpAddress = ipAddress,
                Action = IpFilterAction.Deny,
                IsEnabled = true,
                Description = reason,
                CreatedAt = DateTime.Now
            };

            return AddRule(rule);
        }

        /// <summary>
        /// Дозвіл IP адреси
        /// </summary>
        public int AllowIpAddress(string ipAddress, string reason = "Дозволено адміністратором")
        {
            var rule = new IpFilterRule
            {
                RuleName = $"Allow {ipAddress}",
                IpAddress = ipAddress,
                Action = IpFilterAction.Allow,
                IsEnabled = true,
                Description = reason,
                CreatedAt = DateTime.Now
            };

            return AddRule(rule);
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Отримання статистики по правилам
        /// </summary>
        public Dictionary<string, int> GetRuleStatistics()
        {
            var rules = GetAllRules();

            return new Dictionary<string, int>
            {
                ["Total"] = rules.Count,
                ["Enabled"] = rules.Count(r => r.IsEnabled),
                ["Disabled"] = rules.Count(r => !r.IsEnabled),
                ["Allow"] = rules.Count(r => r.Action == IpFilterAction.Allow),
                ["Deny"] = rules.Count(r => r.Action == IpFilterAction.Deny),
                ["Global"] = rules.Count(r => !r.DirectoryId.HasValue && !r.GeoRoadmapId.HasValue),
                ["DirectorySpecific"] = rules.Count(r => r.DirectoryId.HasValue),
                ["GeoRoadmapSpecific"] = rules.Count(r => r.GeoRoadmapId.HasValue)
            };
        }

        #endregion
    }
}