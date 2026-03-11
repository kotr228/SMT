// File: Data/FolderRepository.cs
using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;

namespace DocControlService.Data
{
    public class FolderRepository
    {
        private readonly DatabaseManager _db;
        public FolderRepository(DatabaseManager db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public int GetByBrowseAndObjectId(string browsePath, int objectId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM Folders WHERE inBrowse = @browse AND idObject = @objectId LIMIT 1;";
            cmd.Parameters.AddWithValue("@browse", browsePath);
            cmd.Parameters.AddWithValue("@objectId", objectId);
            var r = cmd.ExecuteScalar();
            return r == null ? 0 : Convert.ToInt32(r);
        }

        public int AddFolder(string nameFolder, string browse, int objectId, int directoryId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "INSERT INTO Folders (NameFolder, inBrowse, idObject, idDirectory) VALUES (@name, @browse, @objectId, @dir);";
            cmd.Parameters.AddWithValue("@name", nameFolder);
            cmd.Parameters.AddWithValue("@browse", browse);
            cmd.Parameters.AddWithValue("@objectId", objectId);
            cmd.Parameters.AddWithValue("@dir", directoryId);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }
    }
}
