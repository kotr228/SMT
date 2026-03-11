using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocControlService.Services
{
    /// <summary>
    /// Сервіс для створення дорожніх карт з аналізу файлів
    /// </summary>
    public class RoadmapService
    {
        public List<RoadmapEvent> AnalyzeDirectory(string directoryPath)
        {
            var events = new List<RoadmapEvent>();

            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"Директорія {directoryPath} не існує");
                return events;
            }

            try
            {
                // Рекурсивно збираємо всі файли
                var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);

                        // Подія створення файлу
                        events.Add(new RoadmapEvent
                        {
                            Title = $"Створено: {fileInfo.Name}",
                            Description = $"Файл створено в системі",
                            EventDate = fileInfo.CreationTime,
                            EventType = "file_created",
                            FilePath = file,
                            Category = DetermineCategory(fileInfo.Extension)
                        });

                        // Якщо файл модифікували пізніше
                        if (fileInfo.LastWriteTime != fileInfo.CreationTime)
                        {
                            events.Add(new RoadmapEvent
                            {
                                Title = $"Оновлено: {fileInfo.Name}",
                                Description = $"Файл модифіковано",
                                EventDate = fileInfo.LastWriteTime,
                                EventType = "file_modified",
                                FilePath = file,
                                Category = DetermineCategory(fileInfo.Extension)
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Помилка обробки файлу {file}: {ex.Message}");
                    }
                }

                // Сортуємо за датою
                events = events.OrderBy(e => e.EventDate).ToList();

                Console.WriteLine($"Проаналізовано {files.Length} файлів, створено {events.Count} подій");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка аналізу директорії: {ex.Message}");
            }

            return events;
        }

        private string DetermineCategory(string extension)
        {
            extension = extension.ToLower();

            if (extension == ".docx" || extension == ".doc" || extension == ".pdf")
                return "Документи";
            if (extension == ".xlsx" || extension == ".xls" || extension == ".csv")
                return "Таблиці";
            if (extension == ".jpg" || extension == ".png" || extension == ".gif")
                return "Зображення";
            if (extension == ".zip" || extension == ".rar" || extension == ".7z")
                return "Архіви";
            if (extension == ".txt" || extension == ".log")
                return "Текстові файли";

            return "Інше";
        }

        public string ExportToJson(Roadmap roadmap)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Serialize(roadmap, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка експорту в JSON: {ex.Message}");
                return null;
            }
        }

        public List<RoadmapEvent> FilterEventsByDateRange(List<RoadmapEvent> events, DateTime startDate, DateTime endDate)
        {
            return events.Where(e => e.EventDate >= startDate && e.EventDate <= endDate).ToList();
        }

        public Dictionary<string, int> GetStatsByCategory(List<RoadmapEvent> events)
        {
            return events
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}