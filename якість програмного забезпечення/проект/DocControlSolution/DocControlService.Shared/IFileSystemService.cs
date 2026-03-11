using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocControlService.Shared
{
    /// <summary>
    /// Загальний інтерфейс для роботи з файловою системою (локальною або віддаленою)
    /// </summary>
    public interface IFileSystemService
    {
        /// <summary>
        /// Тип файлової системи
        /// </summary>
        FileSystemType Type { get; }

        /// <summary>
        /// Ідентифікатор системи (для віддалених - IP або GUID, для локальної - "local")
        /// </summary>
        string SystemId { get; }

        /// <summary>
        /// Відображуване ім'я (для UI)
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Чи доступна файлова система
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Отримати список файлів та папок у директорії
        /// </summary>
        Task<FileSystemItemList> GetFileListAsync(string path, string filter = "*.*", bool includeSubdirectories = false);

        /// <summary>
        /// Отримати метадані файлу
        /// </summary>
        Task<FileSystemItem?> GetFileMetadataAsync(string filePath);

        /// <summary>
        /// Завантажити файл
        /// </summary>
        Task<bool> DownloadFileAsync(string remotePath, string localPath, IProgress<TransferProgress>? progress = null);

        /// <summary>
        /// Перевірити доступність
        /// </summary>
        Task<bool> PingAsync();
    }

    /// <summary>
    /// Тип файлової системи
    /// </summary>
    public enum FileSystemType
    {
        Local,
        Remote
    }

    /// <summary>
    /// Елемент файлової системи
    /// </summary>
    public class FileSystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public bool IsDirectory { get; set; }
        public string Extension { get; set; } = string.Empty;
    }

    /// <summary>
    /// Список елементів файлової системи
    /// </summary>
    public class FileSystemItemList
    {
        public List<FileSystemItem> Items { get; set; } = new List<FileSystemItem>();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Прогрес передачі файлу
    /// </summary>
    public class TransferProgress
    {
        public string FileName { get; set; } = string.Empty;
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double PercentComplete => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100 : 0;
    }

    /// <summary>
    /// Інформація про віддалений вузол (для UI)
    /// </summary>
    public class RemoteNode
    {
        public Guid InstanceId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int TcpPort { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }

        public string DisplayName => $"{UserName}@{MachineName}";
    }

    /// <summary>
    /// Ідентичність вузла в мережі (використовується для NetworkCore)
    /// </summary>
    public class PeerIdentity
    {
        public Guid InstanceId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public int TcpPort { get; set; }
        public int UdpPort { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public string ProtocolVersion { get; set; } = "1.0";

        /// <summary>
        /// Перевірка чи це цей же екземпляр (щоб не знаходити самого себе)
        /// </summary>
        public bool IsSelf(Guid localInstanceId)
        {
            return InstanceId == localInstanceId;
        }

        public override string ToString()
        {
            return $"{UserName}@{MachineName} ({IpAddress}:{TcpPort})";
        }
    }
}
