using DocControlService.Models;
using DocControlService.Shared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Data
{
    public class ExternalServiceRepository
    {
        private readonly DatabaseManager _db;

        public ExternalServiceRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public int AddService(string name, string serviceType, string url, string apiKey, bool isActive)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ExternalServices (name, serviceType, url, apiKey, isActive, createdAt) 
                VALUES (@name, @type, @url, @key, @active, @time);";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@type", serviceType);
            cmd.Parameters.AddWithValue("@url", url);
            cmd.Parameters.AddWithValue("@key", apiKey ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<ExternalService> GetAllServices()
        {
            var services = new List<ExternalService>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, serviceType, url, apiKey, isActive, lastUsed FROM ExternalServices ORDER BY name;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                services.Add(new ExternalService
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ServiceType = reader.GetString(2),
                    Url = reader.GetString(3),
                    ApiKey = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsActive = reader.GetInt32(5) == 1,
                    LastUsed = reader.IsDBNull(6) ? DateTime.MinValue : DateTime.Parse(reader.GetString(6))
                });
            }

            return services;
        }

        public ExternalService GetServiceById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, serviceType, url, apiKey, isActive, lastUsed FROM ExternalServices WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new ExternalService
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ServiceType = reader.GetString(2),
                    Url = reader.GetString(3),
                    ApiKey = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsActive = reader.GetInt32(5) == 1,
                    LastUsed = reader.IsDBNull(6) ? DateTime.MinValue : DateTime.Parse(reader.GetString(6))
                };
            }

            return null;
        }

        public bool UpdateService(int id, string name, string serviceType, string url, string apiKey, bool isActive)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE ExternalServices 
                SET name = @name, serviceType = @type, url = @url, apiKey = @key, isActive = @active 
                WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@type", serviceType);
            cmd.Parameters.AddWithValue("@url", url);
            cmd.Parameters.AddWithValue("@key", apiKey ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@active", isActive ? 1 : 0);

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteService(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ExternalServices WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            return cmd.ExecuteNonQuery() > 0;
        }

        public void UpdateLastUsed(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ExternalServices SET lastUsed = @time WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }
    }
}