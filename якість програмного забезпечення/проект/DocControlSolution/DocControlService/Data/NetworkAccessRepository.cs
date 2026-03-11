using DocControlService.Models;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocControlService.Data
{
    public class NetworkAccessRepository
    {
        private readonly DatabaseManager _db;

        public NetworkAccessRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public List<NetworkAccessModel> GetAllAccess()
        {
            var result = new List<NetworkAccessModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, idDyrectory, Status, idDevises FROM NetworkAccesDirectory ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new NetworkAccessModel
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    Status = reader.IsDBNull(2) ? false : reader.GetBoolean(2),
                    DeviceId = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
                });
            }
            return result;
        }

        public List<NetworkAccessModel> GetAccessByDirectory(int directoryId)
        {
            var result = new List<NetworkAccessModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            // JOIN з Devises щоб отримати назву пристрою
            cmd.CommandText = @"
                SELECT na.id, na.idDyrectory, na.Status, na.idDevises, d.Name
                FROM NetworkAccesDirectory na
                LEFT JOIN Devises d ON na.idDevises = d.id
                WHERE na.idDyrectory = @dirId;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new NetworkAccessModel
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    Status = reader.GetBoolean(2),
                    DeviceId = reader.GetInt32(3),
                    DeviceName = reader.IsDBNull(4) ? "Невідомий пристрій" : reader.GetString(4)
                });
            }
            return result;
        }

        public List<DeviceModel> GetAllowedDevicesForDirectory(int directoryId)
        {
            Console.WriteLine($"[NetworkAccessRepo] GetAllowedDevicesForDirectory: directoryId={directoryId}");
            var result = new List<DeviceModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT d.id, d.Name, d.Acces
                FROM Devises d
                INNER JOIN NetworkAccesDirectory na ON d.id = na.idDevises
                WHERE na.idDyrectory = @dirId AND na.Status = 1;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var device = new DeviceModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Access = reader.GetBoolean(2)
                };
                Console.WriteLine($"[NetworkAccessRepo]   - Знайдено пристрій: ID={device.Id}, Name='{device.Name}', Access={device.Access}");
                result.Add(device);
            }
            Console.WriteLine($"[NetworkAccessRepo] Всього пристроїв з доступом: {result.Count}");
            return result;
        }

        public int GrantAccess(int directoryId, int deviceId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // Перевіряємо чи існує запис
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = @"
                    SELECT id FROM NetworkAccesDirectory 
                    WHERE idDyrectory = @dirId AND idDevises = @devId LIMIT 1;";
                checkCmd.Parameters.AddWithValue("@dirId", directoryId);
                checkCmd.Parameters.AddWithValue("@devId", deviceId);
                var existing = checkCmd.ExecuteScalar();

                if (existing != null)
                {
                    // Оновлюємо статус
                    using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = @"
                        UPDATE NetworkAccesDirectory 
                        SET Status = 1 
                        WHERE idDyrectory = @dirId AND idDevises = @devId;";
                    updateCmd.Parameters.AddWithValue("@dirId", directoryId);
                    updateCmd.Parameters.AddWithValue("@devId", deviceId);
                    updateCmd.ExecuteNonQuery();
                    return Convert.ToInt32(existing);
                }
            }

            // Додаємо новий запис
            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO NetworkAccesDirectory (idDyrectory, Status, idDevises) 
                VALUES (@dirId, 1, @devId);";
            cmd.Parameters.AddWithValue("@dirId", directoryId);
            cmd.Parameters.AddWithValue("@devId", deviceId);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }

        public bool RevokeAccess(int directoryId, int deviceId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE NetworkAccesDirectory 
                SET Status = 0 
                WHERE idDyrectory = @dirId AND idDevises = @devId;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);
            cmd.Parameters.AddWithValue("@devId", deviceId);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }

        public bool SetDirectoryAccessStatus(int directoryId, bool status)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE NetworkAccesDirectory 
                SET Status = @status 
                WHERE idDyrectory = @dirId;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);
            cmd.Parameters.AddWithValue("@status", status);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }

        public bool IsDirectoryShared(int directoryId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM NetworkAccesDirectory 
                WHERE idDyrectory = @dirId AND Status = 1;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }
    }
}
