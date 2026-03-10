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
    /// Аналізатор структури директорій за допомогою AI
    /// Перевіряє дотримання: Директорія -> Об'єкт -> Папка -> Файли
    /// </summary>
    public class DirectoryStructureAnalyzer
    {
        private readonly OllamaClient _ollama;
        private readonly string _expectedStructure = "Директорія → Об'єкт → Папка → Файли";

        public DirectoryStructureAnalyzer(OllamaClient ollama)
        {
            _ollama = ollama ?? throw new ArgumentNullException(nameof(ollama));
        }

        /// <summary>
        /// Аналіз структури директорії
        /// </summary>
        public async Task<AIAnalysisResult> AnalyzeStructureAsync(string directoryPath, int directoryId)
        {
            Console.WriteLine($"🔍 Аналіз структури: {directoryPath}");

            var result = new AIAnalysisResult
            {
                DirectoryId = directoryId,
                DirectoryPath = directoryPath,
                AnalysisDate = DateTime.Now,
                Type = AIAnalysisType.StructureValidation
            };

            try
            {
                // 1. Сканування файлової системи
                var structure = ScanDirectory(directoryPath);

                // 2. Виявлення порушень
                var violations = DetectViolations(structure, directoryPath);
                result.Violations = violations;

                // 3. AI аналіз та рекомендації
                if (violations.Count > 0)
                {
                    var aiRecommendations = await GenerateAIRecommendationsAsync(structure, violations);
                    result.Recommendations = aiRecommendations;
                    result.RawAIResponse = "AI analysis completed";
                }

                result.Summary = GenerateSummary(violations);
                result.IsProcessed = true;

                Console.WriteLine($"✅ Знайдено порушень: {violations.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка аналізу: {ex.Message}");
                result.Summary = $"Помилка: {ex.Message}";
                result.IsProcessed = false;
            }

            return result;
        }

        /// <summary>
        /// Сканування структури директорії
        /// </summary>
        private DirectoryStructure ScanDirectory(string path)
        {
            var structure = new DirectoryStructure
            {
                RootPath = path,
                Objects = new List<ObjectFolder>()
            };

            if (!Directory.Exists(path))
            {
                Console.WriteLine($"⚠️ Директорія не існує: {path}");
                return structure;
            }

            // Файли в корені (потенційні порушення)
            structure.FilesInRoot = Directory.GetFiles(path).ToList();

            // Об'єкти (піддиректорії рівня 1)
            var objectDirs = Directory.GetDirectories(path);

            foreach (var objDir in objectDirs)
            {
                var objFolder = new ObjectFolder
                {
                    Path = objDir,
                    Name = Path.GetFileName(objDir),
                    SubFolders = new List<SubFolder>()
                };

                // Файли на рівні об'єкта (потенційні порушення)
                objFolder.FilesInObject = Directory.GetFiles(objDir).ToList();

                // Папки (піддиректорії рівня 2)
                var subDirs = Directory.GetDirectories(objDir);

                foreach (var subDir in subDirs)
                {
                    var subFolder = new SubFolder
                    {
                        Path = subDir,
                        Name = Path.GetFileName(subDir),
                        Files = Directory.GetFiles(subDir, "*", SearchOption.AllDirectories).ToList()
                    };

                    objFolder.SubFolders.Add(subFolder);
                }

                structure.Objects.Add(objFolder);
            }

            return structure;
        }

        /// <summary>
        /// Виявлення порушень структури
        /// </summary>
        private List<StructureViolation> DetectViolations(DirectoryStructure structure, string rootPath)
        {
            var violations = new List<StructureViolation>();
            int violationId = 1;

            // 1. Файли в корені директорії
            foreach (var file in structure.FilesInRoot)
            {
                var fileName = Path.GetFileName(file);
                violations.Add(new StructureViolation
                {
                    Id = violationId++,
                    FilePath = file,
                    Type = ViolationType.FileInRootDirectory,
                    Description = $"Файл '{fileName}' знаходиться в корені замість в структурі Об'єкт→Папка",
                    SuggestedPath = SuggestPath(file, rootPath, structure),
                    IsResolved = false
                });
            }

            // 2. Файли на рівні об'єкта
            foreach (var obj in structure.Objects)
            {
                foreach (var file in obj.FilesInObject)
                {
                    var fileName = Path.GetFileName(file);
                    violations.Add(new StructureViolation
                    {
                        Id = violationId++,
                        FilePath = file,
                        Type = ViolationType.MissingObjectFolder,
                        Description = $"Файл '{fileName}' в об'єкті '{obj.Name}' повинен бути в папці",
                        SuggestedPath = Path.Combine(obj.Path, "Документи", fileName),
                        IsResolved = false
                    });
                }

                // 3. Папки без файлів (не є порушенням, але можна повідомити)
                var emptyFolders = obj.SubFolders.Where(f => f.Files.Count == 0).ToList();
                // Опціонально: додати повідомлення про порожні папки
            }

            return violations;
        }

        /// <summary>
        /// Пропонований шлях для файлу
        /// </summary>
        private string SuggestPath(string filePath, string rootPath, DirectoryStructure structure)
        {
            var fileName = Path.GetFileName(filePath);
            var extension = Path.GetExtension(fileName).ToLower();

            // Визначення типу файлу
            string category = DetermineFileCategory(extension);

            // Якщо є об'єкти, пропонуємо перший
            if (structure.Objects.Count > 0)
            {
                var firstObject = structure.Objects[0];
                return Path.Combine(firstObject.Path, category, fileName);
            }

            // Якщо немає об'єктів, створюємо структуру
            return Path.Combine(rootPath, "Загальні", category, fileName);
        }

        /// <summary>
        /// Визначення категорії файлу
        /// </summary>
        private string DetermineFileCategory(string extension)
        {
            return extension switch
            {
                ".pdf" or ".doc" or ".docx" => "Документи",
                ".xlsx" or ".xls" or ".csv" => "Таблиці",
                ".jpg" or ".jpeg" or ".png" or ".gif" => "Зображення",
                ".zip" or ".rar" or ".7z" => "Архіви",
                ".txt" or ".log" => "Текстові файли",
                _ => "Інше"
            };
        }

        /// <summary>
        /// Генерація AI рекомендацій
        /// </summary>
        private async Task<List<AIRecommendation>> GenerateAIRecommendationsAsync(
            DirectoryStructure structure,
            List<StructureViolation> violations)
        {
            var recommendations = new List<AIRecommendation>();

            try
            {
                // Підготовка промпту для AI
                string prompt = BuildAnalysisPrompt(structure, violations);

                // Запит до AI
                string aiResponse = await _ollama.GenerateJsonAsync(prompt, GetRecommendationSchema());

                // Парсинг AI відповіді
                var aiRecommendations = ParseAIRecommendations(aiResponse, violations);
                recommendations.AddRange(aiRecommendations);

                Console.WriteLine($"🤖 AI згенерував {recommendations.Count} рекомендацій");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Помилка AI аналізу: {ex.Message}");
                // Fallback - базові рекомендації без AI
                recommendations.AddRange(GenerateFallbackRecommendations(violations));
            }

            return recommendations;
        }

        /// <summary>
        /// Побудова промпту для AI
        /// </summary>
        private string BuildAnalysisPrompt(DirectoryStructure structure, List<StructureViolation> violations)
        {
            return $@"You are a file system organization expert. Analyze the following directory structure violations and provide recommendations.

Expected structure: {_expectedStructure}

Current violations:
{string.Join("\n", violations.Select(v => $"- {v.Type}: {v.Description}"))}

Directory statistics:
- Files in root: {structure.FilesInRoot.Count}
- Objects (level 1): {structure.Objects.Count}
- Total violations: {violations.Count}

Provide recommendations as a JSON array with this structure:
[
  {{
    ""title"": ""Action title"",
    ""description"": ""Detailed description"",
    ""type"": ""CreateFolder"" or ""MoveFile"" or ""RenameFile"",
    ""priority"": ""Low"" or ""Medium"" or ""High"" or ""Critical"",
    ""actionJson"": ""{{action details as JSON string}}""
  }}
]

Focus on:
1. Creating proper folder structure
2. Moving files to correct locations
3. Grouping similar files together
4. Maintaining the expected hierarchy

Return ONLY the JSON array, no other text.";
        }

        /// <summary>
        /// JSON схема для рекомендацій
        /// </summary>
        private string GetRecommendationSchema()
        {
            return @"{
  ""type"": ""array"",
  ""items"": {
    ""type"": ""object"",
    ""properties"": {
      ""title"": { ""type"": ""string"" },
      ""description"": { ""type"": ""string"" },
      ""type"": { ""type"": ""string"", ""enum"": [""CreateFolder"", ""MoveFile"", ""RenameFile"", ""DeleteDuplicate"", ""StructureOptimization""] },
      ""priority"": { ""type"": ""string"", ""enum"": [""Low"", ""Medium"", ""High"", ""Critical""] },
      ""actionJson"": { ""type"": ""string"" }
    },
    ""required"": [""title"", ""description"", ""type"", ""priority""]
  }
}";
        }

        /// <summary>
        /// Парсинг AI рекомендацій
        /// </summary>
        private List<AIRecommendation> ParseAIRecommendations(string aiResponse, List<StructureViolation> violations)
        {
            var recommendations = new List<AIRecommendation>();

            try
            {
                var aiRecs = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(aiResponse);

                int recId = 1;
                foreach (var rec in aiRecs)
                {
                    recommendations.Add(new AIRecommendation
                    {
                        Id = recId++,
                        Title = rec.GetValueOrDefault("title", "AI Recommendation"),
                        Description = rec.GetValueOrDefault("description", ""),
                        Type = ParseRecommendationType(rec.GetValueOrDefault("type", "StructureOptimization")),
                        Priority = ParsePriority(rec.GetValueOrDefault("priority", "Medium")),
                        ActionJson = rec.GetValueOrDefault("actionJson", "{}"),
                        IsApplied = false
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Помилка парсингу AI рекомендацій: {ex.Message}");
            }

            return recommendations;
        }

        /// <summary>
        /// Fallback рекомендації без AI
        /// </summary>
        private List<AIRecommendation> GenerateFallbackRecommendations(List<StructureViolation> violations)
        {
            var recommendations = new List<AIRecommendation>();
            int recId = 1;

            // Групуємо порушення за типом
            var violationGroups = violations.GroupBy(v => v.Type);

            foreach (var group in violationGroups)
            {
                switch (group.Key)
                {
                    case ViolationType.FileInRootDirectory:
                        recommendations.Add(new AIRecommendation
                        {
                            Id = recId++,
                            Title = $"Перемістити {group.Count()} файлів з кореня",
                            Description = "Файли в корені директорії порушують структуру. Рекомендується створити папки та перемістити файли.",
                            Type = RecommendationType.CreateFolder,
                            Priority = RecommendationPriority.High,
                            ActionJson = JsonSerializer.Serialize(group.Select(v => new
                            {
                                source = v.FilePath,
                                destination = v.SuggestedPath
                            })),
                            IsApplied = false
                        });
                        break;

                    case ViolationType.MissingObjectFolder:
                        recommendations.Add(new AIRecommendation
                        {
                            Id = recId++,
                            Title = $"Створити папки для {group.Count()} файлів",
                            Description = "Файли на рівні об'єкта потребують структурованих папок.",
                            Type = RecommendationType.CreateFolder,
                            Priority = RecommendationPriority.Medium,
                            ActionJson = JsonSerializer.Serialize(group.Select(v => new
                            {
                                source = v.FilePath,
                                destination = v.SuggestedPath
                            })),
                            IsApplied = false
                        });
                        break;
                }
            }

            return recommendations;
        }

        /// <summary>
        /// Генерація підсумку
        /// </summary>
        private string GenerateSummary(List<StructureViolation> violations)
        {
            if (violations.Count == 0)
                return "✅ Структура директорії відповідає очікуваній схемі.";

            var summary = $"⚠️ Знайдено {violations.Count} порушень структури:\n";

            var byType = violations.GroupBy(v => v.Type);
            foreach (var group in byType)
            {
                summary += $"- {group.Key}: {group.Count()} файлів\n";
            }

            return summary;
        }

        private RecommendationType ParseRecommendationType(string type)
        {
            return type switch
            {
                "CreateFolder" => RecommendationType.CreateFolder,
                "MoveFile" => RecommendationType.MoveFile,
                "RenameFile" => RecommendationType.RenameFile,
                "DeleteDuplicate" => RecommendationType.DeleteDuplicate,
                "StructureOptimization" => RecommendationType.StructureOptimization,
                _ => RecommendationType.StructureOptimization
            };
        }

        private RecommendationPriority ParsePriority(string priority)
        {
            return priority switch
            {
                "Low" => RecommendationPriority.Low,
                "Medium" => RecommendationPriority.Medium,
                "High" => RecommendationPriority.High,
                "Critical" => RecommendationPriority.Critical,
                _ => RecommendationPriority.Medium
            };
        }
    }

    #region Helper Classes

    /// <summary>
    /// Структура директорії
    /// </summary>
    public class DirectoryStructure
    {
        public string RootPath { get; set; }
        public List<string> FilesInRoot { get; set; } = new List<string>();
        public List<ObjectFolder> Objects { get; set; } = new List<ObjectFolder>();
    }

    /// <summary>
    /// Папка об'єкта (рівень 1)
    /// </summary>
    public class ObjectFolder
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public List<string> FilesInObject { get; set; } = new List<string>();
        public List<SubFolder> SubFolders { get; set; } = new List<SubFolder>();
    }

    /// <summary>
    /// Підпапка (рівень 2)
    /// </summary>
    public class SubFolder
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public List<string> Files { get; set; } = new List<string>();
    }

    #endregion
}