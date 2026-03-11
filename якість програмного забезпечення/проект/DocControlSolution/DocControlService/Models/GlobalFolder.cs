using System.Collections.Generic;

namespace DocControlService.Models
{
    public class GlobalFolder
    {
        public FolderObject FolderInfo { get; private set; }
        private List<FolderObject> _objects;

        public GlobalFolder(FolderObject folderInfo)
        {
            FolderInfo = folderInfo;
            _objects = new List<FolderObject>();
        }

        public void AddObject(FolderObject obj)
        {
            if (obj != null)
                _objects.Add(obj);
        }

        public IReadOnlyList<FolderObject> GetObjects() => _objects.AsReadOnly();
    }
}
