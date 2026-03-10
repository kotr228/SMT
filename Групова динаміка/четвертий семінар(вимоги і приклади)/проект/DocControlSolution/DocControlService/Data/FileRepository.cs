// File: Data/FileRepository.cs
using DocControlService.Models;
using Microsoft.Data.Sqlite;
using System;

namespace DocControlService.Data
{
    public class FileRepository
    {
        private readonly DatabaseManager _db;
        public FileRepository(DatabaseManager db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public int GetByBrowseAndFolderId(string browsePath, int folderId)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM Files WHERE inBrowse = @browse AND idFolder = @folderId LIMIT 1;";
            cmd.Parameters.AddWithValue("@browse", browsePath);
            cmd.Parameters.AddWithValue("@folderId", folderId);
            var r = cmd.ExecuteScalar();
            return r == null ? 0 : Convert.ToInt32(r);
        }

        public int AddFile(string nameFile, string browse, int idTypeFile, int idFolder, int idObject, int idDirectory)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"INSERT INTO Files 
                (NameFile, inBrowse, idTypeFile, idFolder, idObject, idDirectory) 
                VALUES (@name, @browse, @typeId, @folderId, @objectId, @dirId);";
            cmd.Parameters.AddWithValue("@name", nameFile);
            cmd.Parameters.AddWithValue("@browse", browse);
            cmd.Parameters.AddWithValue("@typeId", idTypeFile);
            cmd.Parameters.AddWithValue("@folderId", idFolder == 0 ? (object)DBNull.Value : idFolder);
            cmd.Parameters.AddWithValue("@objectId", idObject == 0 ? (object)DBNull.Value : idObject);
            cmd.Parameters.AddWithValue("@dirId", idDirectory);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT last_insert_rowid();";
            long id = (long)cmd.ExecuteScalar();
            txn.Commit();
            return (int)id;
        }
    }
}
