using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Models
{
    /// <summary>
    /// Система міграцій БД - автоматично додає недостаючі таблиці та поля
    /// </summary>
    public class DatabaseMigrator
    {
        private readonly string _connectionString;

        public DatabaseMigrator(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        /// <summary>
        /// Перевіряє та створює відсутні таблиці
        /// </summary>
        public void EnsureTablesExist()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            foreach (var sql in DatabaseSchema.CreateTables)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();

                    // Виводимо ім'я таблиці
                    var firstLine = sql.Split('(')[0].Trim();
                    if (firstLine.Contains("CREATE TABLE"))
                    {
                        Console.WriteLine($"   ✓ {firstLine}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠ Помилка при створенні таблиці: {ex.Message}");
                }
            }

            // Додати індекси якщо їх немає
            foreach (var sql in DatabaseSchema.CreateTables)
            {
                if (sql.Contains("CREATE INDEX"))
                {
                    try
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                    catch
                    {
                        // Індекс вже існує - ігноруємо
                    }
                }
            }
        }

        /// <summary>
        /// Перевіряє наявність колонок в таблиці
        /// </summary>
        public bool ColumnExists(string tableName, string columnName)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                string colName = reader.GetString(1); // name column
                if (colName.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Додає колонку до таблиці якщо її немає
        /// </summary>
        public void AddColumnIfNotExists(string tableName, string columnName, string columnType, string defaultValue = null)
        {
            if (ColumnExists(tableName, columnName))
            {
                Console.WriteLine($"   ℹ Колонка {tableName}.{columnName} вже існує");
                return;
            }

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            string alterSql = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
            if (!string.IsNullOrEmpty(defaultValue))
            {
                alterSql += $" DEFAULT {defaultValue}";
            }

            cmd.CommandText = alterSql + ";";

            try
            {
                cmd.ExecuteNonQuery();
                Console.WriteLine($"   ✓ Додано колонку {tableName}.{columnName} ({columnType})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠ Помилка додавання колонки {tableName}.{columnName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Перевіряє наявність таблиці
        /// </summary>
        public bool TableExists(string tableName)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}';";
            var result = cmd.ExecuteScalar();

            return result != null;
        }

        /// <summary>
        /// Виконує міграції для забезпечення сумісності
        /// </summary>
        public void RunMigrations()
        {
            Console.WriteLine("=== ВИКОНАННЯ МІГРАЦІЙ БД ===");

            // Спочатку переконуємось що всі таблиці існують
            EnsureTablesExist();

            // Міграція 1: Перевірка критичних таблиць
            var criticalTables = new[] { "directory", "Devises", "NetworkAccesDirectory", "DirectoryAccess" };
            foreach (var table in criticalTables)
            {
                if (!TableExists(table))
                {
                    Console.WriteLine($"   ⚠ Критична таблиця {table} відсутня - буде створена");
                }
            }

            // Міграція 2: Додаткові поля для сумісності
            // Тут можна додавати нові поля без видалення БД

            Console.WriteLine("=== МІГРАЦІЇ ЗАВЕРШЕНО ===\n");
        }
    }
}
