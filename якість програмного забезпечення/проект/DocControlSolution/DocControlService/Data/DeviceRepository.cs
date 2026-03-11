using DocControlService.Models;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocControlService.Data
{
    public class DeviceRepository
    {
        private readonly DatabaseManager _db;

        public DeviceRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public List<DeviceModel> GetAllDevices()
        {
            var result = new List<DeviceModel>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, Name, Acces FROM Devises ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DeviceModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Access = reader.GetBoolean(2),
                    IsOnline = false // За замовчуванням офлайн, буде оновлено пізніше
                });
            }
            Console.WriteLine($"[DeviceRepo] GetAllDevices: знайдено {result.Count} пристроїв в БД");
            return result;
        }

        public DeviceModel GetById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, Name, Acces FROM Devises WHERE id = @id LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new DeviceModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Access = reader.GetBoolean(2)
                };
            }
            return null;
        }

        public int AddDevice(string name, bool access = false)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "INSERT INTO Devises (Name, Acces) VALUES (@name, @access);";
            cmd.Parameters.AddWithValue("@name", name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@access", access);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }

        public bool UpdateDevice(int id, string name, bool access)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Devises SET Name = @name, Acces = @access WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@access", access);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }

        public bool DeleteDevice(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Devises WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }

        /// <summary>
        /// Знайти пристрій за іменем або створити новий
        /// </summary>
        public DeviceModel GetOrCreateDevice(string name, bool defaultAccess = false)
        {
            Console.WriteLine($"[DeviceRepo] GetOrCreateDevice: name='{name}', defaultAccess={defaultAccess}");

            using var conn = _db.GetConnection();
            conn.Open();

            // Спочатку шукаємо існуючий
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, Name, Acces FROM Devises WHERE Name = @name LIMIT 1;";
                cmd.Parameters.AddWithValue("@name", name);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var device = new DeviceModel
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Access = reader.GetBoolean(2)
                    };
                    Console.WriteLine($"[DeviceRepo] ✅ Знайдено існуючий пристрій: ID={device.Id}, Access={device.Access}");
                    return device;
                }
            }

            // Якщо не знайдено - створюємо новий
            Console.WriteLine($"[DeviceRepo] 🆕 Пристрій не знайдено, створюємо новий...");
            using (var txn = conn.BeginTransaction())
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txn;
                cmd.CommandText = "INSERT INTO Devises (Name, Acces) VALUES (@name, @access);";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@access", defaultAccess);
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT last_insert_rowid();";
                long id = (long)cmd.ExecuteScalar();
                txn.Commit();

                Console.WriteLine($"[DeviceRepo] ✅ Створено новий пристрій: ID={id}, Name='{name}', Access={defaultAccess}");

                return new DeviceModel
                {
                    Id = (int)id,
                    Name = name,
                    Access = defaultAccess
                };
            }
        }
    }
}
