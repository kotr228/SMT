// File: Data/TypeFileRepository.cs
using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;

namespace DocControlService.Data
{
    public class TypeFileRepository
    {
        private readonly DatabaseManager _db;
        public TypeFileRepository(DatabaseManager db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public int GetByExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return 0;
            ext = ext.TrimStart('.').ToLowerInvariant();

            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM TypeFiles WHERE LOWER(extention) = @ext LIMIT 1;";
            cmd.Parameters.AddWithValue("@ext", ext);
            var r = cmd.ExecuteScalar();
            return r == null ? 0 : Convert.ToInt32(r);
        }

        public int AddType(string ext, string typeName = null)
        {
            ext = ext?.TrimStart('.').ToLowerInvariant() ?? "";
            typeName = string.IsNullOrWhiteSpace(typeName) ? ext.ToUpperInvariant() : typeName;

            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "INSERT INTO TypeFiles (extention, TypeName) VALUES (@ext, @name);";
            cmd.Parameters.AddWithValue("@ext", ext);
            cmd.Parameters.AddWithValue("@name", typeName);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }
    }
}
