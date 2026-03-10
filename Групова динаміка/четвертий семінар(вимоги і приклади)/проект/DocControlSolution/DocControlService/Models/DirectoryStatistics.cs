// File: Models/DirectoryStatistics.cs
namespace DocControlService.Models
{
    public class DirectoryStatistics
    {
        public int DirectoryId { get; set; }
        public int ObjectsCount { get; set; }
        public int FoldersCount { get; set; }
        public int FilesCount { get; set; }
        public int AllowedDevicesCount { get; set; }
        public bool IsShared { get; set; }
    }
}
