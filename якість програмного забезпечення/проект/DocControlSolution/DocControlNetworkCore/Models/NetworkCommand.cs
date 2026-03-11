using System;
using System.Collections.Generic;

namespace DocControlNetworkCore.Models
{
    /// <summary>
    /// Типи команд для мережевого обміну
    /// </summary>
    public enum CommandType
    {
        /// <summary>
        /// Запит списку файлів у директорії
        /// </summary>
        GetFileList,

        /// <summary>
        /// Запит метаданих файлу
        /// </summary>
        GetFileMeta,

        /// <summary>
        /// Запит на завантаження файлу
        /// </summary>
        DownloadFile,

        /// <summary>
        /// Heartbeat (сигнал активності)
        /// </summary>
        Heartbeat,

        /// <summary>
        /// Ping (перевірка доступності)
        /// </summary>
        Ping,

        /// <summary>
        /// Запит списку директорій, які цей вузол відкриває для доступу
        /// </summary>
        GetSharedDirectories,

        /// <summary>
        /// Запит статистики директорії
        /// </summary>
        GetDirectoryStatistics,

        /// <summary>
        /// Запит списку файлів/папок у директорії
        /// </summary>
        GetDirectoryFileList,

        /// <summary>
        /// Створення папки
        /// </summary>
        CreateFolder,

        /// <summary>
        /// Створення файлу
        /// </summary>
        CreateFile,

        /// <summary>
        /// Перейменування файлу/папки
        /// </summary>
        RenameFileOrFolder,

        /// <summary>
        /// Видалення файлу/папки
        /// </summary>
        DeleteFileOrFolder,

        /// <summary>
        /// Сканування директорії
        /// </summary>
        ScanDirectory,

        /// <summary>
        /// Git коміт
        /// </summary>
        GitCommit,

        /// <summary>
        /// Git історія
        /// </summary>
        GitHistory,

        /// <summary>
        /// Git відкат до версії
        /// </summary>
        GitRevert,

        /// <summary>
        /// Читання вмісту файлу
        /// </summary>
        ReadFileContent,

        /// <summary>
        /// Запис вмісту файлу
        /// </summary>
        WriteFileContent,

        /// <summary>
        /// Читання бінарного файлу (byte[])
        /// </summary>
        ReadFileBinary,

        /// <summary>
        /// Запис бінарного файлу (byte[])
        /// </summary>
        WriteFileBinary,

        /// <summary>
        /// Заблокувати файл для редагування
        /// </summary>
        LockFile,

        /// <summary>
        /// Розблокувати файл
        /// </summary>
        UnlockFile,

        /// <summary>
        /// Отримати інформацію про блокування файлу
        /// </summary>
        GetFileLockInfo,

        /// <summary>
        /// Оновити heartbeat для блокування
        /// </summary>
        UpdateFileLockHeartbeat,

        /// <summary>
        /// Відповідь на команду
        /// </summary>
        Response
    }

    /// <summary>
    /// Команда для мережевого обміну
    /// </summary>
    public class NetworkCommand
    {
        /// <summary>
        /// Тип команди
        /// </summary>
        public CommandType Type { get; set; }

        /// <summary>
        /// Ідентифікатор запиту (для зіставлення відповіді)
        /// </summary>
        public Guid RequestId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Дані команди (JSON серіалізовані)
        /// </summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>
        /// Мітка часу
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Відправник (для ідентифікації)
        /// </summary>
        public Guid SenderId { get; set; }
    }

    /// <summary>
    /// Відповідь на команду
    /// </summary>
    public class CommandResponse
    {
        /// <summary>
        /// Ідентифікатор запиту, на який це відповідь
        /// </summary>
        public Guid RequestId { get; set; }

        /// <summary>
        /// Чи успішна операція
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Повідомлення про помилку (якщо є)
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Дані відповіді (JSON серіалізовані)
        /// </summary>
        public string? Data { get; set; }

        /// <summary>
        /// Мітка часу
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Запит на отримання списку файлів
    /// </summary>
    public class GetFileListRequest
    {
        /// <summary>
        /// Шлях до директорії
        /// </summary>
        public string DirectoryPath { get; set; } = string.Empty;

        /// <summary>
        /// Маска фільтру (*.txt, *.*, тощо)
        /// </summary>
        public string Filter { get; set; } = "*.*";

        /// <summary>
        /// Включити піддиректорії
        /// </summary>
        public bool IncludeSubdirectories { get; set; } = false;
    }

    /// <summary>
    /// Метадані файлу
    /// </summary>
    public class FileMetadata
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public bool IsDirectory { get; set; }
        public string Extension { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запит на завантаження файлу
    /// </summary>
    public class DownloadFileRequest
    {
        /// <summary>
        /// Повний шлях до файлу
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Зміщення (для підтримки докачування)
        /// </summary>
        public long Offset { get; set; } = 0;

        /// <summary>
        /// Розмір буфера
        /// </summary>
        public int BufferSize { get; set; } = 8192;
    }

    /// <summary>
    /// Запит статистики директорії
    /// </summary>
    public class RemoteDirectoryStatisticsRequest
    {
        public int DirectoryId { get; set; }
    }

    /// <summary>
    /// Запит списку файлів/папок у директорії
    /// </summary>
    public class RemoteFileListRequest
    {
        public string DirectoryPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Створення папки
    /// </summary>
    public class RemoteCreateFolderRequest
    {
        public string ParentPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Створення файлу
    /// </summary>
    public class RemoteCreateFileRequest
    {
        public string ParentPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Перейменування файлу/папки
    /// </summary>
    public class RemoteRenameRequest
    {
        public string OldPath { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Видалення файлу/папки
    /// </summary>
    public class RemoteDeleteRequest
    {
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public bool Recursive { get; set; }
    }

    /// <summary>
    /// Сканування директорії
    /// </summary>
    public class RemoteScanDirectoryRequest
    {
        public int DirectoryId { get; set; }
    }

    /// <summary>
    /// Git коміт
    /// </summary>
    public class RemoteGitCommitRequest
    {
        public int DirectoryId { get; set; }
        public string CommitMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Git історія
    /// </summary>
    public class RemoteGitHistoryRequest
    {
        public int DirectoryId { get; set; }
        public int MaxCount { get; set; } = 100;
    }

    /// <summary>
    /// Git відкат до версії
    /// </summary>
    public class RemoteGitRevertRequest
    {
        public int DirectoryId { get; set; }
        public string CommitHash { get; set; } = string.Empty;
    }

    /// <summary>
    /// Читання вмісту файлу
    /// </summary>
    public class RemoteReadFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запис вмісту файлу
    /// </summary>
    public class RemoteWriteFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Читання бінарного файлу
    /// </summary>
    public class RemoteReadFileBinaryRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запис бінарного файлу
    /// </summary>
    public class RemoteWriteFileBinaryRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Запит на блокування файлу
    /// </summary>
    public class RemoteLockFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запит на розблокування файлу
    /// </summary>
    public class RemoteUnlockFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запит інформації про блокування файлу
    /// </summary>
    public class RemoteGetFileLockInfoRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Оновлення heartbeat блокування
    /// </summary>
    public class RemoteUpdateFileLockHeartbeatRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }
}
