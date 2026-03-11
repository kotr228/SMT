using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace DocControlService.Models
{
    public class DatabaseManager
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly DatabaseValidator _validator;
        private readonly DatabaseMigrator _migrator;

        public DatabaseManager(string dbFileName = "DocControl.db")
        {
            _dbPath = Path.Combine(AppContext.BaseDirectory, dbFileName);
            _connectionString = $"Data Source={_dbPath}";
            _validator = new DatabaseValidator(_dbPath);
            _migrator = new DatabaseMigrator(_dbPath);

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            Console.WriteLine("=== ІНІЦІАЛІЗАЦІЯ БАЗИ ДАНИХ ===");

            try
            {
                bool exists = _validator.DatabaseExists();
                Console.WriteLine($"📂 Перевірка існування БД → {(exists ? "існує" : "не існує")}");

                if (!exists)
                {
                    Console.WriteLine("➡ Створюємо нову БД...");
                    CreateSchema();
                }
                else
                {
                    Console.WriteLine("➡ БД існує. Виконуємо міграції...");

                    // НОВА ЛОГІКА: Замість видалення - виконуємо міграції
                    try
                    {
                        _migrator.RunMigrations();
                        Console.WriteLine("✅ База даних оновлена через міграції.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Помилка виконання міграцій: {ex.Message}");
                        Console.WriteLine("⚠️ Спроба пересоздати БД...");

                        // Тільки якщо міграції не вдалися - видаляємо та створюємо заново
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        try
                        {
                            _validator.DropDatabase();
                            Console.WriteLine("🗑️ Існуюча БД видалена.");
                            CreateSchema();
                        }
                        catch (Exception dropEx)
                        {
                            Console.WriteLine($"❌ Критична помилка: {dropEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Критична помилка при ініціалізації БД: {ex.Message}");
            }

            Console.WriteLine("=== ІНІЦІАЛІЗАЦІЯ ЗАВЕРШЕНА ===\n");
        }


        private void CreateSchema()
        {
            Console.WriteLine("➡ Створення структури таблиць...");

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            foreach (var sql in DatabaseSchema.CreateTables)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();

                // маленький лайфхак: вичищаю перший рядок до CREATE TABLE щоб в логах було видно назву
                var firstLine = cmd.CommandText.Split('(')[0].Trim();
                Console.WriteLine($"   + {firstLine} ✓");
            }

            // Вставка дефолтних шаблонів геокарт
            Console.WriteLine("➡ Вставка вбудованих шаблонів...");
            foreach (var sql in DatabaseSchema.InsertDefaultTemplates)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    // Ігноруємо помилки вставки (шаблон вже існує)
                }
            }

            Console.WriteLine("✅ Усі таблиці та дані створено.");
        }

        // 🔹 метод перевірки запитів
        public bool ExecuteTestQuery()
        {
            Console.WriteLine("➡ Виконую тестовий запит...");

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM directory;";
                var count = Convert.ToInt32(cmd.ExecuteScalar());

                Console.WriteLine($"✅ Перевірка запиту успішна. В таблиці directory {count} записів.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка при перевірці БД: {ex.Message}");
                return false;
            }
        }

        public SqliteConnection GetConnection()
        {
            return new SqliteConnection(_connectionString);
        }

    }
}
