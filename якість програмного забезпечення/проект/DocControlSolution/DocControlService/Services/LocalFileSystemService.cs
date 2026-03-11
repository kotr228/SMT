using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DocControlService.Services
{
    /// <summary>
    /// Локальна файлова система (обгортка для роботи з локальними директоріями через DocControlService)
    /// </summary>
    public class LocalFileSystemService : IFileSystemService
    {
        private readonly DirectoryRepository _dirRepo;
        private readonly DatabaseManager _dbManager;

        public FileSystemType Type => FileSystemType.Local;
        public string SystemId => "local";
        public string DisplayName => $"Цей комп'ютер ({Environment.MachineName})";
        public bool IsAvailable => true;

        public LocalFileSystemService(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            _dirRepo = new DirectoryRepository(dbManager);
        }

        /// <summary>
        /// Отримати список файлів та папок
        /// </summary>
        public async Task<FileSystemItemList> GetFileListAsync(string path, string filter = "*.*", bool includeSubdirectories = false)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Перевірка чи шлях існує
                    if (!Directory.Exists(path))
                    {
                        return new FileSystemItemList
                        {
                            Success = false,
                            ErrorMessage = "Директорія не знайдена"
                        };
                    }

                    var items = new List<FileSystemItem>();
                    var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    // Отримати директорії
                    var directories = Directory.GetDirectories(path, "*", searchOption);
                    items.AddRange(directories.Select(d =>
                    {
                        var di = new DirectoryInfo(d);
                        return new FileSystemItem
                        {
                            Name = di.Name,
                            FullPath = di.FullName,
                            Size = 0,
                            CreatedDate = di.CreationTime,
                            ModifiedDate = di.LastWriteTime,
                            IsDirectory = true,
                            Extension = ""
                        };
                    }));

                    // Отримати файли
                    var files = Directory.GetFiles(path, filter, searchOption);
                    items.AddRange(files.Select(f =>
                    {
                        var fi = new FileInfo(f);
                        return new FileSystemItem
                        {
                            Name = fi.Name,
                            FullPath = fi.FullName,
                            Size = fi.Length,
                            CreatedDate = fi.CreationTime,
                            ModifiedDate = fi.LastWriteTime,
                            IsDirectory = false,
                            Extension = fi.Extension
                        };
                    }));

                    return new FileSystemItemList
                    {
                        Items = items,
                        Success = true
                    };
                }
                catch (UnauthorizedAccessException)
                {
                    return new FileSystemItemList
                    {
                        Success = false,
                        ErrorMessage = "Доступ заборонено"
                    };
                }
                catch (Exception ex)
                {
                    return new FileSystemItemList
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                }
            });
        }

        /// <summary>
        /// Отримати метадані файлу
        /// </summary>
        public async Task<FileSystemItem?> GetFileMetadataAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var fi = new FileInfo(filePath);
                        return new FileSystemItem
                        {
                            Name = fi.Name,
                            FullPath = fi.FullName,
                            Size = fi.Length,
                            CreatedDate = fi.CreationTime,
                            ModifiedDate = fi.LastWriteTime,
                            IsDirectory = false,
                            Extension = fi.Extension
                        };
                    }
                    else if (Directory.Exists(filePath))
                    {
                        var di = new DirectoryInfo(filePath);
                        return new FileSystemItem
                        {
                            Name = di.Name,
                            FullPath = di.FullName,
                            Size = 0,
                            CreatedDate = di.CreationTime,
                            ModifiedDate = di.LastWriteTime,
                            IsDirectory = true,
                            Extension = ""
                        };
                    }

                    return null;
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// "Завантажити" файл (для локальної системи це просто копіювання)
        /// </summary>
        public async Task<bool> DownloadFileAsync(string sourcePath, string destinationPath, IProgress<TransferProgress>? progress = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(sourcePath))
                        return false;

                    var fileInfo = new FileInfo(sourcePath);
                    long totalBytes = fileInfo.Length;
                    long bytesTransferred = 0;

                    // Створити директорію якщо не існує
                    var destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Копіювання з прогресом
                    const int bufferSize = 81920; // 80KB
                    using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
                    using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[bufferSize];
                        int bytesRead;

                        while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            destStream.Write(buffer, 0, bytesRead);
                            bytesTransferred += bytesRead;

                            progress?.Report(new TransferProgress
                            {
                                FileName = Path.GetFileName(sourcePath),
                                BytesTransferred = bytesTransferred,
                                TotalBytes = totalBytes
                            });
                        }
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Перевірка доступності (завжди доступна для локальної системи)
        /// </summary>
        public async Task<bool> PingAsync()
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// Отримати список зареєстрованих директорій з БД
        /// </summary>
        public List<DirectoryModel> GetRegisteredDirectories()
        {
            return _dirRepo.GetAllDirectories();
        }
    }
}
