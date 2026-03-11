using DocControlService.Shared;
using System;
using System.IO;
using System.Text.Json;

namespace DocControlAI.Services
{
    /// <summary>
    /// Сервіс для експорту AI даних в JSON
    /// </summary>
    public class DataExportService
    {
        public string ExportChronologicalRoadmap(AIChronologicalRoadmap roadmap)
        {
            if (roadmap == null)
            {
                throw new ArgumentNullException(nameof(roadmap), "Хронологічна карта не може бути null");
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                string json = JsonSerializer.Serialize(roadmap, options);

                // Перевірка що JSON не порожній
                if (string.IsNullOrWhiteSpace(json))
                {
                    throw new InvalidOperationException("Згенерований JSON порожній");
                }

                Console.WriteLine($"✅ JSON експортовано успішно ({json.Length} символів)");
                return json;
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"❌ Помилка серіалізації JSON: {jsonEx.Message}");
                throw new InvalidOperationException($"Не вдалося серіалізувати карту в JSON: {jsonEx.Message}", jsonEx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка експорту: {ex.Message}");
                throw new InvalidOperationException($"Помилка експорту хронологічної карти: {ex.Message}", ex);
            }
        }

        public bool SaveToFile(string json, string filePath)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON не може бути порожнім", nameof(json));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Шлях до файлу не може бути порожнім", nameof(filePath));
            }

            try
            {
                // Створюємо директорію якщо не існує
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"📁 Створено директорію: {directory}");
                }

                // Перевірка прав доступу
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.IsReadOnly)
                    {
                        throw new UnauthorizedAccessException($"Файл '{filePath}' має атрибут 'тільки для читання'");
                    }
                }

                // Збереження з UTF-8 encoding для підтримки кирилиці
                File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);

                var savedFileInfo = new FileInfo(filePath);
                Console.WriteLine($"✅ Збережено: {filePath} ({savedFileInfo.Length / 1024.0:F2} KB)");
                return true;
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Console.WriteLine($"❌ Немає прав доступу: {uaEx.Message}");
                throw new UnauthorizedAccessException($"Немає доступу для запису файлу '{filePath}': {uaEx.Message}", uaEx);
            }
            catch (DirectoryNotFoundException dnfEx)
            {
                Console.WriteLine($"❌ Директорія не знайдена: {dnfEx.Message}");
                throw new DirectoryNotFoundException($"Директорія для '{filePath}' не існує: {dnfEx.Message}", dnfEx);
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"❌ Помилка вводу-виводу: {ioEx.Message}");
                throw new IOException($"Помилка запису файлу '{filePath}': {ioEx.Message}", ioEx);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка збереження: {ex.Message}");
                throw new InvalidOperationException($"Не вдалося зберегти файл '{filePath}': {ex.Message}", ex);
            }
        }
    }
}