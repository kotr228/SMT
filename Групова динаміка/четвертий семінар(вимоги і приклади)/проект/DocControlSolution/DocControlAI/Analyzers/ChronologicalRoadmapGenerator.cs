using DocControlService.Shared;
using DocControlAI.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocControlAI.Analyzers
{
    /// <summary>
    /// Генератор хронологічних дорожніх карт за допомогою AI
    /// </summary>
    public class ChronologicalRoadmapGenerator
    {
        private readonly OllamaClient _ollama;

        public ChronologicalRoadmapGenerator(OllamaClient ollama)
        {
            _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
        }

        /// <summary>
        /// Генерація хронологічної карти для директорії
        /// </summary>
        public async Task<AIChronologicalRoadmap> GenerateRoadmapAsync(string directoryPath, int directoryId, string projectName = null)
        {
            Console.WriteLine($"📅 Генерація хронологічної карти для: {directoryPath}");

            var roadmap = new AIChronologicalRoadmap
            {
                DirectoryId = directoryId,
                Name = projectName ?? $"Проект {Path.GetFileName(directoryPath)}",
                Description = "AI-згенерована хронологічна карта подій проекту",
                GeneratedAt = DateTime.Now
            };

            try
            {
                // 1. Збір інформації про файли
                var fileTimeline = CollectFileTimeline(directoryPath);

                // 2. AI аналіз та створення подій
                var events = await GenerateEventsWithAIAsync(fileTimeline, directoryPath);
                roadmap.Events = events;

                // 3. AI генерація insights
                roadmap.AIInsights = await GenerateInsightsAsync(events, directoryPath);

                Console.WriteLine($"✅ Створено {events.Count} хронологічних подій");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка генерації карти: {ex.Message}");
                roadmap.AIInsights = $"Помилка: {ex.Message}";
            }

            return roadmap;
        }

        /// <summary>
        /// Збір хронології файлів
        /// </summary>
        private List<FileTimelineEntry> CollectFileTimeline(string directoryPath)
        {
            var timeline = new List<FileTimelineEntry>();

            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"⚠️ Директорія не існує: {directoryPath}");
                return timeline;
            }

            try
            {
                var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

                foreach (var filePath in files)
                {
                    var fileInfo = new FileInfo(filePath);

                    timeline.Add(new FileTimelineEntry
                    {
                        FilePath = filePath,
                        FileName = fileInfo.Name,
                        Extension = fileInfo.Extension,
                        CreatedDate = fileInfo.CreationTime,
                        ModifiedDate = fileInfo.LastWriteTime,
                        Size = fileInfo.Length,
                        RelativePath = Path.GetRelativePath(directoryPath, filePath)
                    });
                }

                // Сортуємо за датою створення
                timeline = timeline.OrderBy(t => t.CreatedDate).ToList();

                Console.WriteLine($"📊 Знайдено {timeline.Count} файлів для аналізу");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Помилка збору даних: {ex.Message}");
            }

            return timeline;
        }

        /// <summary>
        /// Генерація подій за допомогою AI
        /// </summary>
        private async Task<List<ChronologicalEvent>> GenerateEventsWithAIAsync(
            List<FileTimelineEntry> timeline,
            string directoryPath)
        {
            var events = new List<ChronologicalEvent>();

            if (timeline.Count == 0)
                return events;

            try
            {
                // Групуємо файли за датами (по дням)
                var groupedByDate = timeline.GroupBy(t => t.CreatedDate.Date);

                int eventId = 1;

                foreach (var dateGroup in groupedByDate)
                {
                    // Для кожного дня створюємо подію з AI описом
                    var filesInDay = dateGroup.ToList();
                    var aiEvent = await CreateEventForDayAsync(dateGroup.Key, filesInDay, eventId++);

                    if (aiEvent != null)
                        events.Add(aiEvent);
                }

                // Виявлення ключових віх (milestones)
                var milestones = DetectMilestones(timeline);
                foreach (var milestone in milestones)
                {
                    milestone.Id = eventId++;
                    events.Add(milestone);
                }

                // Сортуємо за датою
                events = events.OrderBy(e => e.EventDate).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Помилка AI генерації подій: {ex.Message}");
            }

            return events;
        }

        /// <summary>
        /// Створення події для конкретного дня з AI
        /// </summary>
        private async Task<ChronologicalEvent> CreateEventForDayAsync(
            DateTime date,
            List<FileTimelineEntry> files,
            int eventId)
        {
            try
            {
                // Підготовка промпту для AI
                string prompt = BuildEventPrompt(date, files);

                // Запит до AI
                string aiResponse = await _ollama.GenerateJsonAsync(prompt, GetEventSchema());

                // Парсинг відповіді
                var eventData = JsonSerializer.Deserialize<Dictionary<string, string>>(aiResponse);

                return new ChronologicalEvent
                {
                    Id = eventId,
                    EventDate = date,
                    Title = eventData.GetValueOrDefault("title", $"Робота над проектом"),
                    Description = eventData.GetValueOrDefault("description", ""),
                    Category = DetermineCategory(files),
                    RelatedFiles = files.Select(f => f.FilePath).ToList(),
                    AIGeneratedContext = eventData.GetValueOrDefault("context", "")
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Помилка створення події: {ex.Message}");

                // Fallback без AI
                return new ChronologicalEvent
                {
                    Id = eventId,
                    EventDate = date,
                    Title = $"Робота з файлами ({files.Count})",
                    Description = $"Створено/оновлено {files.Count} файлів",
                    Category = DetermineCategory(files),
                    RelatedFiles = files.Select(f => f.FilePath).ToList(),
                    AIGeneratedContext = "Fallback event"
                };
            }
        }

        /// <summary>
        /// Побудова промпту для події
        /// </summary>
        private string BuildEventPrompt(DateTime date, List<FileTimelineEntry> files)
        {
            var filesList = string.Join("\n", files.Select(f =>
                $"- {f.FileName} ({f.Extension}, {FormatFileSize(f.Size)})"));

            return $@"You are analyzing a project timeline. Based on the following files created/modified on {date:yyyy-MM-dd}, generate a meaningful event description.

Files:
{filesList}

Total files: {files.Count}
Date: {date:yyyy-MM-dd}

Generate a JSON response with:
{{
  ""title"": ""Short event title (max 50 chars)"",
  ""description"": ""Detailed description of what happened this day based on file types and names"",
  ""context"": ""Additional context or insights about this work session""
}}

Make it professional, concise, and insightful. Infer the type of work based on file extensions.
Return ONLY the JSON, no other text.";
        }

        /// <summary>
        /// JSON схема події
        /// </summary>
        private string GetEventSchema()
        {
            return @"{
  ""type"": ""object"",
  ""properties"": {
    ""title"": { ""type"": ""string"", ""maxLength"": 50 },
    ""description"": { ""type"": ""string"" },
    ""context"": { ""type"": ""string"" }
  },
  ""required"": [""title"", ""description""]
}";
        }

        /// <summary>
        /// Виявлення ключових віх (milestones)
        /// </summary>
        private List<ChronologicalEvent> DetectMilestones(List<FileTimelineEntry> timeline)
        {
            var milestones = new List<ChronologicalEvent>();

            if (timeline.Count == 0)
                return milestones;

            // Перша подія проекту
            var firstFile = timeline.First();
            milestones.Add(new ChronologicalEvent
            {
                EventDate = firstFile.CreatedDate,
                Title = "🎯 Початок проекту",
                Description = $"Проект розпочато з файлу: {firstFile.FileName}",
                Category = "Milestone",
                RelatedFiles = new List<string> { firstFile.FilePath },
                AIGeneratedContext = "Project initiation"
            });

            // Значні зміни (багато файлів в один день)
            var filesByDate = timeline.GroupBy(t => t.CreatedDate.Date)
                .Where(g => g.Count() >= 5)
                .OrderByDescending(g => g.Count())
                .Take(3);

            foreach (var group in filesByDate)
            {
                milestones.Add(new ChronologicalEvent
                {
                    EventDate = group.Key,
                    Title = "⚡ Активна фаза розробки",
                    Description = $"Інтенсивна робота: {group.Count()} файлів за день",
                    Category = "Milestone",
                    RelatedFiles = group.Select(f => f.FilePath).ToList(),
                    AIGeneratedContext = "High activity period"
                });
            }

            // Остання подія
            var lastFile = timeline.Last();
            if ((DateTime.Now - lastFile.ModifiedDate).TotalDays > 7)
            {
                milestones.Add(new ChronologicalEvent
                {
                    EventDate = lastFile.ModifiedDate,
                    Title = "📌 Останнє оновлення",
                    Description = $"Останні зміни: {lastFile.FileName}",
                    Category = "Milestone",
                    RelatedFiles = new List<string> { lastFile.FilePath },
                    AIGeneratedContext = "Last modification"
                });
            }

            return milestones;
        }

        /// <summary>
        /// Генерація insights за допомогою AI
        /// </summary>
        private async Task<string> GenerateInsightsAsync(List<ChronologicalEvent> events, string directoryPath)
        {
            try
            {
                string prompt = $@"Analyze this project timeline and provide key insights:

Total events: {events.Count}
Project path: {directoryPath}
Timeline span: {events.First().EventDate:yyyy-MM-dd} to {events.Last().EventDate:yyyy-MM-dd}

Events summary:
{string.Join("\n", events.Take(10).Select(e => $"- {e.EventDate:yyyy-MM-dd}: {e.Title}"))}

Provide insights about:
1. Project activity pattern
2. Key milestones
3. Work intensity
4. Recommendations

Keep it concise (max 200 words).";

                return await _ollama.SendPromptAsync(prompt);
            }
            catch
            {
                return "Insights не згенеровані (AI unavailable)";
            }
        }

        /// <summary>
        /// Визначення категорії на основі файлів
        /// </summary>
        private string DetermineCategory(List<FileTimelineEntry> files)
        {
            var extensions = files.Select(f => f.Extension.ToLower()).Distinct().ToList();

            if (extensions.Any(e => e == ".doc" || e == ".docx" || e == ".pdf"))
                return "Документація";
            if (extensions.Any(e => e == ".xlsx" || e == ".xls" || e == ".csv"))
                return "Звітність";
            if (extensions.Any(e => e == ".jpg" || e == ".png" || e == ".gif"))
                return "Дизайн";
            if (extensions.Any(e => e == ".cs" || e == ".js" || e == ".py"))
                return "Розробка";

            return "Загальна робота";
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    #region Helper Classes

    /// <summary>
    /// Запис в хронології файлів
    /// </summary>
    public class FileTimelineEntry
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Extension { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public long Size { get; set; }
        public string RelativePath { get; set; }
    }

    #endregion
}