namespace DocControlService.Models
{
    public class FolderObject
    {
        public int Id { get; set; }              // ID в БД
        public string Path { get; set; }         // Повний шлях
        public string Name { get; set; }         // Назва файлу або папки
        public bool IsFile { get; set; }         // true = файл, false = папка
        public int? ParentId { get; set; }       // Для вкладених об’єктів

        public FolderObject(string path, bool isFile, int? parentId = null)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            IsFile = isFile;
            ParentId = parentId;
        }
    }
}
