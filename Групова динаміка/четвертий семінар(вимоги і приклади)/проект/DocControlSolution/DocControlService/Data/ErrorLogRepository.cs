using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Data
{
    public class ErrorLogEntry
    {
        public int Id { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string UserFriendlyMessage { get; set; }
        public string StackTrace { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsResolved { get; set; }
    }

    public class ErrorLogRepository
    {
        private readonly DatabaseManager _db;

        public ErrorLogRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public void LogError(string errorType, string errorMessage, string userFriendlyMessage, string stackTrace = null)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ErrorLog (errorType, errorMessage, userFriendlyMessage, stackTrace, timestamp) 
                VALUES (@type, @msg, @userMsg, @stack, @time);";
            cmd.Parameters.AddWithValue("@type", errorType);
            cmd.Parameters.AddWithValue("@msg", errorMessage);
            cmd.Parameters.AddWithValue("@userMsg", userFriendlyMessage);
            cmd.Parameters.AddWithValue("@stack", stackTrace ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public List<ErrorLogEntry> GetRecentErrors(int limit = 100, bool onlyUnresolved = false)
        {
            var result = new List<ErrorLogEntry>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            string where = onlyUnresolved ? "WHERE isResolved = 0" : "";
            cmd.CommandText = $@"
                SELECT id, errorType, errorMessage, userFriendlyMessage, stackTrace, timestamp, isResolved 
                FROM ErrorLog 
                {where}
                ORDER BY timestamp DESC 
                LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ErrorLogEntry
                {
                    Id = reader.GetInt32(0),
                    ErrorType = reader.GetString(1),
                    ErrorMessage = reader.GetString(2),
                    UserFriendlyMessage = reader.GetString(3),
                    StackTrace = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Timestamp = DateTime.Parse(reader.GetString(5)),
                    IsResolved = reader.GetInt32(6) == 1
                });
            }
            return result;
        }

        public void MarkAsResolved(int errorId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ErrorLog SET isResolved = 1 WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", errorId);
            cmd.ExecuteNonQuery();
        }

        public void ClearResolvedErrors()
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM ErrorLog WHERE isResolved = 1;";
            cmd.ExecuteNonQuery();
        }

        public int GetUnresolvedCount()
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ErrorLog WHERE isResolved = 0;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
}