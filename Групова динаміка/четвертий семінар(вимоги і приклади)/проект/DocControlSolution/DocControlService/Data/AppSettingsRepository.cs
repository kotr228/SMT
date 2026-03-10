using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Data
{
    public class AppSettingsRepository
    {
        private readonly DatabaseManager _db;

        public AppSettingsRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public string GetSetting(string key, string defaultValue = null)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT settingValue FROM AppSettings WHERE settingKey = @key LIMIT 1;";
            cmd.Parameters.AddWithValue("@key", key);

            var result = cmd.ExecuteScalar();
            return result?.ToString() ?? defaultValue;
        }

        public void SetSetting(string key, string value, string description = null)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AppSettings (settingKey, settingValue, description, updatedAt) 
                VALUES (@key, @value, @desc, @time)
                ON CONFLICT(settingKey) DO UPDATE SET 
                    settingValue = @value, 
                    updatedAt = @time;";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@desc", description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public bool GetBoolSetting(string key, bool defaultValue = false)
        {
            var value = GetSetting(key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            return value.ToLower() == "true" || value == "1";
        }

        public void SetBoolSetting(string key, bool value, string description = null)
        {
            SetSetting(key, value ? "true" : "false", description);
        }

        public int GetIntSetting(string key, int defaultValue = 0)
        {
            var value = GetSetting(key);
            if (int.TryParse(value, out int result))
                return result;

            return defaultValue;
        }

        public void SetIntSetting(string key, int value, string description = null)
        {
            SetSetting(key, value.ToString(), description);
        }

        public Dictionary<string, string> GetAllSettings()
        {
            var result = new Dictionary<string, string>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT settingKey, settingValue FROM AppSettings;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = reader.GetString(1);
            }

            return result;
        }

        // Ініціалізація дефолтних налаштувань
        public void InitializeDefaults()
        {
            SetBoolSetting("AutoShareOnAdd", false, "Автоматично відкривати доступ при додаванні директорії");
            SetBoolSetting("EnableUpdateNotifications", true, "Показувати повідомлення про оновлення");
            SetIntSetting("CommitIntervalMinutes", 720, "Інтервал автокомітів (хвилини)");
        }
    }
}