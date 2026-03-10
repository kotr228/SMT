// File: Services/DirectoryScanner.cs
using System;
using System.IO;
using System.Linq;
using DocControlService.Models;
using DocControlService.Data;

namespace DocControlService.Services
{
    public class DirectoryScanner
    {
        private readonly DatabaseManager _db;
        private readonly DirectoryRepository _dirRepo;
        private readonly ObjectRepository _objRepo;
        private readonly FolderRepository _folderRepo;
        private readonly TypeFileRepository _typeRepo;
        private readonly FileRepository _fileRepo;

        public DirectoryScanner(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _dirRepo = new DirectoryRepository(_db);
            _objRepo = new ObjectRepository(_db);
            _folderRepo = new FolderRepository(_db);
            _typeRepo = new TypeFileRepository(_db);
            _fileRepo = new FileRepository(_db);
        }

        public void ScanDirectoryById(int directoryId)
        {
            var dir = _dirRepo.GetById(directoryId);
            if (dir == null)
            {
                Console.WriteLine($"❌ Directory id={directoryId} not found in DB.");
                return;
            }

            Console.WriteLine($"🔎 Scanning directory '{dir.Name}' -> '{dir.Browse}' (id={dir.Id})");

            if (!Directory.Exists(dir.Browse))
            {
                Console.WriteLine($"⚠️ Filesystem path does not exist: {dir.Browse}");
                return;
            }

            // 1) enumerate immediate child directories (objects)
            var childObjects = Directory.EnumerateDirectories(dir.Browse);
            foreach (var objPath in childObjects)
            {
                string objName = Path.GetFileName(objPath);
                Console.WriteLine($"  • Found object folder: {objName} ({objPath})");

                int objId = _objRepo.GetByBrowseAndDirectoryId(objPath, dir.Id);
                if (objId == 0)
                {
                    objId = _objRepo.AddObject(objName, objPath, dir.Id);
                    Console.WriteLine($"    + Added Objects.id={objId}");
                }
                else
                {
                    Console.WriteLine($"    = Object already exists id={objId}");
                }

                // 2) enumerate subfolders inside this object -> Folders table
                var subFolders = Directory.EnumerateDirectories(objPath);
                foreach (var folderPath in subFolders)
                {
                    string folderName = Path.GetFileName(folderPath);
                    int folderId = _folderRepo.GetByBrowseAndObjectId(folderPath, objId);
                    if (folderId == 0)
                    {
                        folderId = _folderRepo.AddFolder(folderName, folderPath, objId, dir.Id);
                        Console.WriteLine($"      + Added Folder.id={folderId}");
                    }
                    else
                    {
                        Console.WriteLine($"      = Folder exists id={folderId}");
                    }

                    // 3) enumerate files inside this folder (non-recursive)
                    var files = Directory.EnumerateFiles(folderPath);
                    foreach (var filePath in files)
                    {
                        string fileName = Path.GetFileName(filePath);
                        // тип файлу
                        string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
                        int typeId = _typeRepo.GetByExtension(ext);
                        if (typeId == 0)
                        {
                            typeId = _typeRepo.AddType(ext, ext.ToUpperInvariant());
                            Console.WriteLine($"        + TypeFiles added id={typeId} (.{ext})");
                        }

                        int fileId = _fileRepo.GetByBrowseAndFolderId(filePath, folderId);
                        if (fileId == 0)
                        {
                            fileId = _fileRepo.AddFile(fileName, filePath, typeId, folderId, objId, dir.Id);
                            Console.WriteLine($"        + File added id={fileId} -> {fileName}");
                        }
                        else
                        {
                            Console.WriteLine($"        = File exists id={fileId} -> {fileName}");
                        }
                    } // files
                } // subFolders
            } // childObjects

            Console.WriteLine("🔎 Scan completed.");
        }
    }
}
