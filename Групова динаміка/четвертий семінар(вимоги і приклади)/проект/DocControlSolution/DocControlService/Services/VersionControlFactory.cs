using DocControlService.Data;
using System.Collections.Generic;

namespace DocControlService.Services
{
    public class VersionControlFactory
    {
        private readonly DirectoryRepository _dirRepo;
        private readonly Dictionary<int, VersionControlService> _services = new();

        public VersionControlFactory(DirectoryRepository dirRepo)
        {
            _dirRepo = dirRepo;
            InitializeServices();
        }

        private void InitializeServices()
        {
            var dirs = _dirRepo.GetAllDirectories();
            foreach (var d in dirs)
            {
                if (!_services.ContainsKey(d.Id))
                {
                    _services[d.Id] = new VersionControlService(d.Browse);
                }
            }
        }

        public VersionControlService GetServiceFor(int directoryId)
        {
            if (_services.TryGetValue(directoryId, out var service))
                return service;

            var dir = _dirRepo.GetById(directoryId);
            if (dir == null) return null;

            var newService = new VersionControlService(dir.Browse);
            _services[directoryId] = newService;
            return newService;
        }

        public IEnumerable<VersionControlService> GetAllServices()
        {
            return _services.Values;
        }
    }
}
