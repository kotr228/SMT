using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Data
{
    public class CommitStatusLog
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string DirectoryPath { get; set; }
        public string Status { get; set; } // success, denied, error
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CommitLogRepository
    {
        private readonly DatabaseManager _db;

        public CommitLogRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public void LogCommit(int directoryId, string directoryPath, string status, string message)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CommitStatusLog (directoryId, directoryPath, status, message, timestamp) 
                VALUES (@dirId, @path, @status, @msg, @time);";
            cmd.Parameters.AddWithValue("@dirId", directoryId);
            cmd.Parameters.AddWithValue("@path", directoryPath);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@msg", message ?? "");
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public List<CommitStatusLog> GetRecentLogs(int limit = 100)
        {
            var result = new List<CommitStatusLog>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, directoryId, directoryPath, status, message, timestamp 
                FROM CommitStatusLog 
                ORDER BY timestamp DESC 
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new CommitStatusLog
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    DirectoryPath = reader.GetString(2),
                    Status = reader.GetString(3),
                    Message = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Timestamp = DateTime.Parse(reader.GetString(5))
                });
            }
            return result;
        }

        public List<CommitStatusLog> GetLogsByDirectory(int directoryId, int limit = 50)
        {
            var result = new List<CommitStatusLog>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, directoryId, directoryPath, status, message, timestamp 
                FROM CommitStatusLog 
                WHERE directoryId = @dirId
                ORDER BY timestamp DESC 
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new CommitStatusLog
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    DirectoryPath = reader.GetString(2),
                    Status = reader.GetString(3),
                    Message = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Timestamp = DateTime.Parse(reader.GetString(5))
                });
            }
            return result;
        }

        public Dictionary<int, string> GetDirectoryCommitStatus()
        {
            var result = new Dictionary<int, string>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT directoryId, status 
                FROM CommitStatusLog 
                WHERE id IN (
                    SELECT MAX(id) 
                    FROM CommitStatusLog 
                    GROUP BY directoryId
                );";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetInt32(0)] = reader.GetString(1);
            }
            return result;
        }
    }
}