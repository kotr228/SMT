// File: Data/FileLockRepository.cs
using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Data
{
    /// <summary>
    /// Репозиторій для керування блокуваннями файлів (багатокористувацький режим)
    /// </summary>
    public class FileLockRepository
    {
        private readonly DatabaseManager _db;

        public FileLockRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            EnsureTableExists();
        }

        /// <summary>
        /// Створити таблицю FileLocks якщо не існує
        /// </summary>
        private void EnsureTableExists()
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS FileLocks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT NOT NULL,
                    DeviceName TEXT NOT NULL,
                    UserName TEXT,
                    LockTime DATETIME NOT NULL,
                    LastModified DATETIME NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    UNIQUE(FilePath, DeviceName)
                );
                CREATE INDEX IF NOT EXISTS idx_filelocks_filepath ON FileLocks(FilePath);
                CREATE INDEX IF NOT EXISTS idx_filelocks_active ON FileLocks(IsActive);
            ";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Спробувати заблокувати файл для редагування
        /// </summary>
        public FileLockModel TryLockFile(string filePath, string deviceName, string userName)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();

            try
            {
                // Перевірити чи файл вже заблокований іншим пристроєм
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = txn;
                    cmd.CommandText = @"
                        SELECT Id, DeviceName, UserName, LockTime, LastModified
                        FROM FileLocks
                        WHERE FilePath = @filePath AND IsActive = 1 AND DeviceName != @deviceName
                        LIMIT 1;
                    ";
                    cmd.Parameters.AddWithValue("@filePath", filePath);
                    cmd.Parameters.AddWithValue("@deviceName", deviceName);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        // Файл заблокований іншим пристроєм
                        return new FileLockModel
                        {
                            Id = reader.GetInt32(0),
                            FilePath = filePath,
                            DeviceName = reader.GetString(1),
                            UserName = reader.IsDBNull(2) ? null : reader.GetString(2),
                            LockTime = reader.GetDateTime(3),
                            LastModified = reader.GetDateTime(4),
                            IsActive = true,
                            IsOwnedByCurrentDevice = false
                        };
                    }
                }

                // Створити або оновити блокування для поточного пристрою
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = txn;
                    cmd.CommandText = @"
                        INSERT INTO FileLocks (FilePath, DeviceName, UserName, LockTime, LastModified, IsActive)
                        VALUES (@filePath, @deviceName, @userName, @lockTime, @lastModified, 1)
                        ON CONFLICT(FilePath, DeviceName) DO UPDATE SET
                            UserName = @userName,
                            LockTime = @lockTime,
                            LastModified = @lastModified,
                            IsActive = 1;
                        SELECT last_insert_rowid();
                    ";
                    var now = DateTime.UtcNow;
                    cmd.Parameters.AddWithValue("@filePath", filePath);
                    cmd.Parameters.AddWithValue("@deviceName", deviceName);
                    cmd.Parameters.AddWithValue("@userName", userName ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@lockTime", now);
                    cmd.Parameters.AddWithValue("@lastModified", now);

                    var id = (long)cmd.ExecuteScalar();

                    txn.Commit();

                    return new FileLockModel
                    {
                        Id = (int)id,
                        FilePath = filePath,
                        DeviceName = deviceName,
                        UserName = userName,
                        LockTime = now,
                        LastModified = now,
                        IsActive = true,
                        IsOwnedByCurrentDevice = true
                    };
                }
            }
            catch
            {
                txn.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Розблокувати файл
        /// </summary>
        public bool UnlockFile(string filePath, string deviceName)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE FileLocks
                SET IsActive = 0
                WHERE FilePath = @filePath AND DeviceName = @deviceName;
            ";
            cmd.Parameters.AddWithValue("@filePath", filePath);
            cmd.Parameters.AddWithValue("@deviceName", deviceName);
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Оновити час останньої модифікації (для heartbeat)
        /// </summary>
        public bool UpdateLastModified(string filePath, string deviceName)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE FileLocks
                SET LastModified = @now
                WHERE FilePath = @filePath AND DeviceName = @deviceName AND IsActive = 1;
            ";
            cmd.Parameters.AddWithValue("@filePath", filePath);
            cmd.Parameters.AddWithValue("@deviceName", deviceName);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>
        /// Отримати інформацію про блокування файлу
        /// </summary>
        public FileLockModel GetFileLock(string filePath)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FilePath, DeviceName, UserName, LockTime, LastModified, IsActive
                FROM FileLocks
                WHERE FilePath = @filePath AND IsActive = 1
                ORDER BY LockTime DESC
                LIMIT 1;
            ";
            cmd.Parameters.AddWithValue("@filePath", filePath);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new FileLockModel
                {
                    Id = reader.GetInt32(0),
                    FilePath = reader.GetString(1),
                    DeviceName = reader.GetString(2),
                    UserName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    LockTime = reader.GetDateTime(4),
                    LastModified = reader.GetDateTime(5),
                    IsActive = reader.GetInt32(6) == 1,
                    IsOwnedByCurrentDevice = false
                };
            }

            return null;
        }

        /// <summary>
        /// Отримати всі активні блокування
        /// </summary>
        public List<FileLockModel> GetAllActiveLocks()
        {
            var locks = new List<FileLockModel>();
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FilePath, DeviceName, UserName, LockTime, LastModified, IsActive
                FROM FileLocks
                WHERE IsActive = 1
                ORDER BY LockTime DESC;
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                locks.Add(new FileLockModel
                {
                    Id = reader.GetInt32(0),
                    FilePath = reader.GetString(1),
                    DeviceName = reader.GetString(2),
                    UserName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    LockTime = reader.GetDateTime(4),
                    LastModified = reader.GetDateTime(5),
                    IsActive = reader.GetInt32(6) == 1,
                    IsOwnedByCurrentDevice = false
                });
            }

            return locks;
        }

        /// <summary>
        /// Очистити застарілі блокування (старіші за 5 хвилин без оновлень)
        /// </summary>
        public int CleanupStaleLocks(int timeoutMinutes = 5)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE FileLocks
                SET IsActive = 0
                WHERE IsActive = 1
                AND datetime(LastModified, '+' || @timeout || ' minutes') < datetime('now');
            ";
            cmd.Parameters.AddWithValue("@timeout", timeoutMinutes);
            return cmd.ExecuteNonQuery();
        }
    }
}
