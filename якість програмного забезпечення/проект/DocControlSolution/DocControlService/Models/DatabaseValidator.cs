using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace DocControlService.Models
{
    public class DatabaseValidator
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseValidator(string dbPath)
        {
            _dbPath = dbPath;
            _connectionString = $"Data Source={_dbPath}";
        }

        public bool DatabaseExists()
        {
            bool exists = File.Exists(_dbPath);
            Console.WriteLine(exists ? "ℹ Файл БД знайдено." : "ℹ Файл БД відсутній.");
            return exists;
        }

        public bool ValidateStructure()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                var requiredTables = new List<string> { "directory", "Objects", "TypeFiles", "Folders", "Files", "DirectoryAccess" };

                foreach (var table in requiredTables)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';";
                    var result = cmd.ExecuteScalar();

                    if (result == null)
                    {
                        Console.WriteLine($"❌ Не знайдено таблицю: {table}");
                        return false;
                    }
                    else
                    {
                        Console.WriteLine($"✅ Таблиця {table} знайдена.");
                    }
                }

                return true; // все є
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка при перевірці структури: {ex.Message}");
                return false;
            }
        }

        public void DropDatabase()
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
                Console.WriteLine("⚠ Стара БД видалена.");
            }
        }
    }
}
