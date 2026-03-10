// File: Data/ObjectRepository.cs
using Microsoft.Data.Sqlite;
using System;
using DocControlService.Models;

namespace DocControlService.Data
{
    public class ObjectRepository
    {
        private readonly DatabaseManager _db;
        public ObjectRepository(DatabaseManager db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        // Повертає id, якщо існує, або 0
        public int GetByBrowseAndDirectoryId(string browsePath, int directoryId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM Objects WHERE inBrowse = @browse AND idDirectory = @dir LIMIT 1;";
            cmd.Parameters.AddWithValue("@browse", browsePath);
            cmd.Parameters.AddWithValue("@dir", directoryId);
            var r = cmd.ExecuteScalar();
            return r == null ? 0 : Convert.ToInt32(r);
        }

        public int AddObject(string name, string browse, int directoryId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "INSERT INTO Objects (Name, inBrowse, idDirectory) VALUES (@name, @browse, @dir);";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@browse", browse);
            cmd.Parameters.AddWithValue("@dir", directoryId);
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }
    }
}
