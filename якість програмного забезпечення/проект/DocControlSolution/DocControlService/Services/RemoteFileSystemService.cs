using DocControlNetworkCore.Models;
using DocControlNetworkCore.Services;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NetworkCommandType = DocControlNetworkCore.Models.CommandType;
using PeerIdentity = DocControlService.Shared.PeerIdentity;

namespace DocControlService.Services
{
    /// <summary>
    /// Віддалена файлова система (обгортка для роботи через NetworkCore)
    /// </summary>
    public class RemoteFileSystemService : IFileSystemService
    {
        private readonly PeerIdentity _remotePeer;
        private readonly CommandLayerService _commandLayer;
        private readonly FileTransferService _fileTransfer;

        public FileSystemType Type => FileSystemType.Remote;
        public string SystemId => _remotePeer.InstanceId.ToString();
        public string DisplayName => $"{_remotePeer.UserName}@{_remotePeer.MachineName}";
        public bool IsAvailable { get; private set; } = true;

        public RemoteFileSystemService(
            PeerIdentity remotePeer,
            CommandLayerService commandLayer,
            FileTransferService fileTransfer)
        {
            _remotePeer = remotePeer;
            _commandLayer = commandLayer;
            _fileTransfer = fileTransfer;
        }

        /// <summary>
        /// Отримати список файлів та папок з віддаленого вузла
        /// </summary>
        public async Task<FileSystemItemList> GetFileListAsync(string path, string filter = "*.*", bool includeSubdirectories = false)
        {
            try
            {
                // Створити запит
                var request = new GetFileListRequest
                {
                    DirectoryPath = path,
                    Filter = filter,
                    IncludeSubdirectories = includeSubdirectories
                };

                var command = new NetworkCommand
                {
                    Type = NetworkCommandType.GetFileList,
                    Payload = JsonSerializer.Serialize(request),
                    SenderId = Guid.Empty // Буде заповнено в CommandLayer
                };

                // Відправити команду
                var response = await _commandLayer.SendCommandAsync(
                    _remotePeer.IpAddress,
                    _remotePeer.TcpPort,
                    command,
                    timeoutMs: 10000);

                if (response == null)
                {
                    IsAvailable = false;
                    return new FileSystemItemList
                    {
                        Success = false,
                        ErrorMessage = "Немає відповіді від віддаленого вузла"
                    };
                }

                if (!response.Success)
                {
                    return new FileSystemItemList
                    {
                        Success = false,
                        ErrorMessage = response.ErrorMessage ?? "Невідома помилка"
                    };
                }

                // Парсити відповідь
                var remoteItems = JsonSerializer.Deserialize<List<DocControlNetworkCore.Models.FileMetadata>>(response.Data!);
                if (remoteItems == null)
                {
                    return new FileSystemItemList
                    {
                        Success = false,
                        ErrorMessage = "Неможливо розпарсити відповідь"
                    };
                }

                // Конвертувати в FileSystemItem
                var items = remoteItems.Select(ri => new FileSystemItem
                {
                    Name = ri.FileName,
                    FullPath = ri.FullPath,
                    Size = ri.Size,
                    CreatedDate = ri.CreatedDate,
                    ModifiedDate = ri.ModifiedDate,
                    IsDirectory = ri.IsDirectory,
                    Extension = ri.Extension
                }).ToList();

                IsAvailable = true;
                return new FileSystemItemList
                {
                    Items = items,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                return new FileSystemItemList
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Отримати метадані файлу з віддаленого вузла
        /// </summary>
        public async Task<FileSystemItem?> GetFileMetadataAsync(string filePath)
        {
            try
            {
                var command = new NetworkCommand
                {
                    Type = NetworkCommandType.GetFileMeta,
                    Payload = filePath,
                    SenderId = Guid.Empty
                };

                var response = await _commandLayer.SendCommandAsync(
                    _remotePeer.IpAddress,
                    _remotePeer.TcpPort,
                    command,
                    timeoutMs: 5000);

                if (response == null || !response.Success || response.Data == null)
                {
                    IsAvailable = false;
                    return null;
                }

                var metadata = JsonSerializer.Deserialize<DocControlNetworkCore.Models.FileMetadata>(response.Data);
                if (metadata == null)
                    return null;

                IsAvailable = true;
                return new FileSystemItem
                {
                    Name = metadata.FileName,
                    FullPath = metadata.FullPath,
                    Size = metadata.Size,
                    CreatedDate = metadata.CreatedDate,
                    ModifiedDate = metadata.ModifiedDate,
                    IsDirectory = metadata.IsDirectory,
                    Extension = metadata.Extension
                };
            }
            catch
            {
                IsAvailable = false;
                return null;
            }
        }

        /// <summary>
        /// Завантажити файл з віддаленого вузла
        /// </summary>
        public async Task<bool> DownloadFileAsync(string remotePath, string localPath, IProgress<TransferProgress>? progress = null)
        {
            try
            {
                // Підписка на події прогресу
                void OnProgress(string fileName, long current, long total)
                {
                    progress?.Report(new TransferProgress
                    {
                        FileName = fileName,
                        BytesTransferred = current,
                        TotalBytes = total
                    });
                }

                if (_fileTransfer != null)
                {
                    _fileTransfer.DownloadProgress += OnProgress;
                }

                // Завантажити файл
                bool success = await _fileTransfer.DownloadFileAsync(
                    _remotePeer.IpAddress,
                    _remotePeer.TcpPort,
                    remotePath,
                    localPath);

                if (_fileTransfer != null)
                {
                    _fileTransfer.DownloadProgress -= OnProgress;
                }

                IsAvailable = success;
                return success;
            }
            catch
            {
                IsAvailable = false;
                return false;
            }
        }

        /// <summary>
        /// Перевірити доступність віддаленого вузла
        /// </summary>
        public async Task<bool> PingAsync()
        {
            try
            {
                var command = new NetworkCommand
                {
                    Type = NetworkCommandType.Ping,
                    Payload = "",
                    SenderId = Guid.Empty
                };

                var response = await _commandLayer.SendCommandAsync(
                    _remotePeer.IpAddress,
                    _remotePeer.TcpPort,
                    command,
                    timeoutMs: 3000);

                IsAvailable = response != null && response.Success;
                return IsAvailable;
            }
            catch
            {
                IsAvailable = false;
                return false;
            }
        }
    }
}
