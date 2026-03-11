using DocControlNetworkCore.Models;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DocControlNetworkCore.Services
{
    /// <summary>
    /// Сервіс для передачі файлів між вузлами
    /// </summary>
    public class FileTransferService
    {
        private readonly string _allowedBasePath;
        private const int DefaultBufferSize = 8192; // 8KB

        /// <summary>
        /// Подія прогресу завантаження
        /// </summary>
        public event Action<string, long, long>? DownloadProgress;

        /// <summary>
        /// Подія прогресу відправки
        /// </summary>
        public event Action<string, long, long>? UploadProgress;

        public FileTransferService(string allowedBasePath)
        {
            _allowedBasePath = Path.GetFullPath(allowedBasePath);
        }

        /// <summary>
        /// Завантажити файл від іншого вузла
        /// </summary>
        public async Task<bool> DownloadFileAsync(
            string remoteIp,
            int remotePort,
            string remoteFilePath,
            string localSavePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Console.WriteLine($"[FileTransfer] Завантаження файлу від {remoteIp}:{remotePort}");
                Console.WriteLine($"[FileTransfer] Віддалений шлях: {remoteFilePath}");
                Console.WriteLine($"[FileTransfer] Локальний шлях: {localSavePath}");

                using var client = new TcpClient();
                await client.ConnectAsync(remoteIp, remotePort, cancellationToken);

                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

                // Відправити запит на завантаження
                var request = new DownloadFileRequest
                {
                    FilePath = remoteFilePath,
                    Offset = 0,
                    BufferSize = DefaultBufferSize
                };

                var command = new NetworkCommand
                {
                    Type = CommandType.DownloadFile,
                    Payload = JsonSerializer.Serialize(request)
                };

                var commandJson = JsonSerializer.Serialize(command);
                await writer.WriteLineAsync(commandJson);

                // Отримати відповідь з метаданими
                var responseJson = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(responseJson))
                {
                    Console.WriteLine("[FileTransfer] Порожня відповідь");
                    return false;
                }

                var response = JsonSerializer.Deserialize<CommandResponse>(responseJson);
                if (response == null || !response.Success)
                {
                    Console.WriteLine($"[FileTransfer] Помилка: {response?.ErrorMessage}");
                    return false;
                }

                // Отримати розмір файлу
                var metadata = JsonSerializer.Deserialize<FileMetadata>(response.Data!);
                if (metadata == null)
                {
                    Console.WriteLine("[FileTransfer] Неможливо отримати метадані файлу");
                    return false;
                }

                long fileSize = metadata.Size;
                Console.WriteLine($"[FileTransfer] Розмір файлу: {fileSize} байт");

                // Створити директорію якщо потрібно
                var directory = Path.GetDirectoryName(localSavePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Потокове завантаження файлу
                using var fileStream = new FileStream(localSavePath, FileMode.Create, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[DefaultBufferSize];
                long totalBytesRead = 0;

                while (totalBytesRead < fileSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int bytesToRead = (int)Math.Min(buffer.Length, fileSize - totalBytesRead);
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

                    if (bytesRead == 0)
                        break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalBytesRead += bytesRead;

                    // Виклик події прогресу
                    DownloadProgress?.Invoke(remoteFilePath, totalBytesRead, fileSize);

                    // Логування прогресу
                    if (totalBytesRead % (1024 * 1024) == 0 || totalBytesRead == fileSize)
                    {
                        double progress = (double)totalBytesRead / fileSize * 100;
                        Console.WriteLine($"[FileTransfer] Прогрес: {progress:F2}% ({totalBytesRead}/{fileSize})");
                    }
                }

                Console.WriteLine($"[FileTransfer] Файл успішно завантажено: {localSavePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileTransfer] Помилка завантаження файлу: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Обробити запит на відправку файлу (викликається з CommandLayer)
        /// </summary>
        public async Task HandleUploadRequestAsync(NetworkStream stream, DownloadFileRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                // Валідація шляху
                var fullPath = Path.GetFullPath(Path.Combine(_allowedBasePath, request.FilePath));
                if (!fullPath.StartsWith(_allowedBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Доступ заборонено: шлях поза дозволеною директорією");
                }

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("Файл не знайдено", fullPath);
                }

                var fileInfo = new FileInfo(fullPath);
                Console.WriteLine($"[FileTransfer] Відправка файлу: {fileInfo.Name} ({fileInfo.Length} байт)");

                // Відправити метадані
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var metadata = new FileMetadata
                {
                    FileName = fileInfo.Name,
                    FullPath = request.FilePath,
                    Size = fileInfo.Length,
                    CreatedDate = fileInfo.CreationTime,
                    ModifiedDate = fileInfo.LastWriteTime,
                    IsDirectory = false,
                    Extension = fileInfo.Extension
                };

                var response = new CommandResponse
                {
                    RequestId = Guid.NewGuid(),
                    Success = true,
                    Data = JsonSerializer.Serialize(metadata)
                };

                var responseJson = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson);

                // Потокова відправка файлу
                using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

                // Пропустити offset якщо вказаний (для підтримки докачування)
                if (request.Offset > 0)
                {
                    fileStream.Seek(request.Offset, SeekOrigin.Begin);
                }

                byte[] buffer = new byte[request.BufferSize > 0 ? request.BufferSize : DefaultBufferSize];
                long totalBytesSent = request.Offset;
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalBytesSent += bytesRead;

                    // Виклик події прогресу
                    UploadProgress?.Invoke(request.FilePath, totalBytesSent, fileInfo.Length);

                    // Логування прогресу
                    if (totalBytesSent % (1024 * 1024) == 0 || totalBytesSent == fileInfo.Length)
                    {
                        double progress = (double)totalBytesSent / fileInfo.Length * 100;
                        Console.WriteLine($"[FileTransfer] Відправка: {progress:F2}% ({totalBytesSent}/{fileInfo.Length})");
                    }
                }

                await stream.FlushAsync(cancellationToken);
                Console.WriteLine($"[FileTransfer] Файл успішно відправлено: {fileInfo.Name}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileTransfer] Помилка відправки файлу: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Отримати розмір файлу без завантаження
        /// </summary>
        public long GetFileSize(string filePath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(_allowedBasePath, filePath));
            if (!fullPath.StartsWith(_allowedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Доступ заборонено");
            }

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Файл не знайдено", fullPath);
            }

            return new FileInfo(fullPath).Length;
        }
    }
}
