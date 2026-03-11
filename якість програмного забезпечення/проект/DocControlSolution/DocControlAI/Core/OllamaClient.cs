using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Abstractions;

namespace DocControlAI.Core
{
    /// <summary>Клієнт для локальної Llama 3 (.gguf) без Ollama</summary>
    public class OllamaClient
    {
        private readonly string _modelPath;
        private readonly string _modelName;

        private LLamaWeights _model;
        private LLamaContext _context;
        private InteractiveExecutor _executor;
        private bool _isModelLoaded = false;

        public OllamaClient(string modelPath = "Models/meta-llama-3-8b-instruct.Q4_K_M.gguf", string modelName = "llama3")
        {
            // Конвертуємо відносний шлях до абсолютного
            if (!Path.IsPathRooted(modelPath))
            {
                _modelPath = Path.Combine(AppContext.BaseDirectory, modelPath);
            }
            else
            {
                _modelPath = modelPath;
            }

            _modelName = modelName;
            Console.WriteLine($"📍 Шлях до AI моделі: {_modelPath}");
        }

        public async Task<bool> IsOllamaRunningAsync()
        {
            return await Task.Run(() => File.Exists(_modelPath));
        }

        public async Task EnsureModelLoadedAsync()
        {
            if (_isModelLoaded && _executor != null)
                return;

            if (!File.Exists(_modelPath))
                throw new Exception($"Модель не знайдена: {_modelPath}");

            await Task.Run(() =>
            {
                // ✅ 1. Створюємо параметри моделі (це реалізує IModelParams)
                var modelParams = new ModelParams(_modelPath)
                {
                    ContextSize = 2048,
                    Seed = 1337
                };

                // ✅ 2. Завантажуємо ваги, передаючи modelParams
                _model = LLamaWeights.LoadFromFile(modelParams);

                // ✅ 3. Створюємо контекст на основі цих параметрів
                _context = _model.CreateContext(modelParams);

                // ✅ 4. Ініціалізуємо виконавця
                _executor = new InteractiveExecutor(_context);

                _isModelLoaded = true;
                Console.WriteLine("✅ Модель успішно завантажена в пам'ять (LLamaSharp 0.8.1)");
            });
        }


        public async Task<string> SendPromptAsync(string prompt)
        {
            await EnsureModelLoadedAsync();
            if (!_isModelLoaded)
                throw new Exception("Модель не завантажена.");

            var sb = new StringBuilder();
            var inferParams = new InferenceParams
            {
                Temperature = 0.7f,
                MaxTokens = 512,
                AntiPrompts = new List<string> { "User:" }
            };

            await foreach (var text in _executor.InferAsync(prompt, inferParams))
            {
                sb.Append(text);
            }

            return sb.ToString();
        }

        public async Task<string> SendChatPromptAsync(List<Message> messages)
        {
            var combined = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
            return await SendPromptAsync(combined);
        }

        public async Task<string> GenerateJsonAsync(string prompt, string jsonSchema = null)
        {
            string fullPrompt = $@"{prompt}

IMPORTANT: Respond ONLY with valid JSON. Do not include any explanatory text before or after the JSON.
{(jsonSchema != null ? $"Use this JSON schema:\n{jsonSchema}" : "")}";

            string response = await SendPromptAsync(fullPrompt);

            int start = response.IndexOf('{');
            int end = response.LastIndexOf('}');
            if (start >= 0 && end > start)
                return response.Substring(start, end - start + 1);

            return response;
        }

        public async Task<(bool isRunning, string version, bool isModelLoaded)> GetStatusAsync()
        {
            Console.WriteLine("🔍 Перевірка статусу Ollama...");

            bool exists = await Task.Run(() => File.Exists(_modelPath));
            _isModelLoaded = exists;

            Console.WriteLine($"📦 Model file exists: {exists}");
            Console.WriteLine($"📦 Model path: {_modelPath}");
            Console.WriteLine($"📦 Is loaded: {_isModelLoaded}");

            return (exists, "LLamaSharp 0.8.1 (local, .NET6)", _isModelLoaded);
        }

        public void Dispose()
        {
            _context?.Dispose();
            _model?.Dispose();
        }
    }

    public class ConversationContext
    {
        public List<Message> Messages { get; set; } = new List<Message>();
        public string SystemPrompt { get; set; }

        public void AddMessage(string role, string content)
        {
            Messages.Add(new Message { Role = role, Content = content });
        }
    }

    public class Message
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }
}