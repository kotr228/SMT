using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DocControlService.Shared
{
    // =============== ІСНУЮЧІ МОДЕЛІ (v0.1-0.2) ===============

    [Serializable]
    public class DirectoryModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Browse { get; set; }
    }

    [Serializable]
    public class DeviceModel : INotifyPropertyChanged
    {
        private int _id;
        private string _name;
        private bool _access;
        private bool _isOnline;
        private int _accessDirectoriesCount;

        public int Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool Access
        {
            get => _access;
            set { _access = value; OnPropertyChanged(); }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set { _isOnline = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Кількість директорій до яких пристрій має доступ
        /// </summary>
        public int AccessDirectoriesCount
        {
            get => _accessDirectoriesCount;
            set { _accessDirectoriesCount = value; OnPropertyChanged(); }
        }

        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Serializable]
    public class NetworkAccessModel
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public bool Status { get; set; }
        public int DeviceId { get; set; }
        public string DeviceName { get; set; }  // Назва пристрою для відображення в UI
    }

    [Serializable]
    public class DirectoryWithAccessModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Browse { get; set; }
        public bool IsShared { get; set; }
        public string GitStatus { get; set; }
        public List<DeviceModel> AllowedDevices { get; set; } = new List<DeviceModel>();

        // Додаткові властивості для UI
        public string SharedStatusText { get; set; }
    }

    [Serializable]
    public class RoadmapEvent
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime EventDate { get; set; }
        public string EventType { get; set; }
        public string FilePath { get; set; }
        public string Category { get; set; }
    }

    [Serializable]
    public class Roadmap
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<RoadmapEvent> Events { get; set; } = new List<RoadmapEvent>();
    }

    // =============== НОВІ МОДЕЛІ v0.3 - ГЕОДОРОЖНІ КАРТИ ===============

    /// <summary>
    /// Географічна точка на карті
    /// </summary>
    [Serializable]
    public class GeoPoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Address { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Геодорожня карта проекту
    /// </summary>
    [Serializable]
    public class GeoRoadmap
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string CreatedBy { get; set; }

        // Налаштування карти
        public MapProvider MapProvider { get; set; } = MapProvider.OpenStreetMap;
        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public int ZoomLevel { get; set; } = 10;

        // Елементи карти
        public List<GeoRoadmapNode> Nodes { get; set; } = new List<GeoRoadmapNode>();
        public List<GeoRoadmapRoute> Routes { get; set; } = new List<GeoRoadmapRoute>();
        public List<GeoRoadmapArea> Areas { get; set; } = new List<GeoRoadmapArea>();
    }

    /// <summary>
    /// Вузол на геокарті (точка)
    /// </summary>
    [Serializable]
    public class GeoRoadmapNode
    {
        public int Id { get; set; }
        public int GeoRoadmapId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Address { get; set; }
        public NodeType Type { get; set; }
        public string IconName { get; set; }
        public string Color { get; set; }
        public DateTime? EventDate { get; set; }
        public string RelatedFiles { get; set; } // JSON масив шляхів до файлів
        public int OrderIndex { get; set; }
    }

    /// <summary>
    /// Маршрут між точками
    /// </summary>
    [Serializable]
    public class GeoRoadmapRoute
    {
        public int Id { get; set; }
        public int GeoRoadmapId { get; set; }
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public string Label { get; set; }
        public string Color { get; set; }
        public RouteStyle Style { get; set; }
        public int StrokeWidth { get; set; } = 2;
    }

    /// <summary>
    /// Область на карті (полігон)
    /// </summary>
    [Serializable]
    public class GeoRoadmapArea
    {
        public int Id { get; set; }
        public int GeoRoadmapId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string PolygonCoordinates { get; set; } // JSON масив координат
        public string FillColor { get; set; }
        public string StrokeColor { get; set; }
        public double Opacity { get; set; } = 0.3;
    }

    /// <summary>
    /// Шаблон геокарти
    /// </summary>
    [Serializable]
    public class GeoRoadmapTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string TemplateJson { get; set; } // JSON структура шаблону
        public bool IsBuiltIn { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Правило IP фільтрації
    /// </summary>
    [Serializable]
    public class IpFilterRule
    {
        public int Id { get; set; }
        public string RuleName { get; set; }
        public string IpAddress { get; set; } // Може бути IP або CIDR (192.168.1.0/24)
        public IpFilterAction Action { get; set; }
        public bool IsEnabled { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? DirectoryId { get; set; } // null = глобальне правило
        public int? GeoRoadmapId { get; set; } // null = для всіх карт
    }

    // =============== ЕНУМИ v0.3 ===============

    public enum MapProvider
    {
        OpenStreetMap,
        GoogleMaps,
        BingMaps
    }

    public enum NodeType
    {
        Milestone,      // Віха проекту
        Location,       // Локація
        Office,         // Офіс
        Site,           // Об'єкт
        Meeting,        // Зустріч
        Checkpoint,     // Контрольна точка
        Custom          // Користувацька
    }

    public enum RouteStyle
    {
        Solid,
        Dashed,
        Dotted,
        Arrow
    }

    public enum IpFilterAction
    {
        Allow,
        Deny
    }

    // =============== ОНОВЛЕНІ КОМАНДИ v0.3 ===============

    public enum CommandType
    {
        // Directory operations
        GetDirectories,
        AddDirectory,
        RemoveDirectory,
        UpdateDirectoryName,
        UpdateDirectory,
        ScanDirectory,
        SearchDirectories,
        GetDirectoryStatistics,

        // Device operations
        GetDevices,
        AddDevice,
        RemoveDevice,
        UpdateDevice,

        // Access control
        GrantAccess,
        RevokeAccess,
        GetNetworkAccess,

        // Service status
        GetStatus,
        GetServiceLogs,

        // Version control
        ForceCommit,
        CommitDirectory,
        GetCommitLog,
        SetCommitInterval,
        GetGitHistory,
        RevertToCommit,
        GetDirectoryGitStatus,

        // Error logging
        GetErrorLog,
        MarkErrorResolved,
        ClearResolvedErrors,
        GetUnresolvedErrorCount,

        // Settings
        GetSettings,
        SaveSettings,
        GetSetting,
        SetSetting,

        // Roadmap (v0.2)
        CreateRoadmap,
        GetRoadmaps,
        GetRoadmapById,
        DeleteRoadmap,
        AnalyzeDirectoryForRoadmap,
        ExportRoadmapAsJson,
        ExportRoadmapAsImage,

        // Network Discovery
        ScanNetwork,
        GetNetworkInterfaces,
        GetNetworkDevices,

        // External Services
        GetExternalServices,
        AddExternalService,
        UpdateExternalService,
        DeleteExternalService,
        TestExternalService,

        // ===== НОВІ КОМАНДИ v0.3 - GEO ROADMAPS =====

        // Geo Roadmap CRUD
        CreateGeoRoadmap,
        GetGeoRoadmaps,
        GetGeoRoadmapById,
        UpdateGeoRoadmap,
        DeleteGeoRoadmap,

        // Geo Nodes
        AddGeoNode,
        UpdateGeoNode,
        DeleteGeoNode,
        GetGeoNodesByRoadmap,

        // Geo Routes
        AddGeoRoute,
        UpdateGeoRoute,
        DeleteGeoRoute,

        // Geo Areas
        AddGeoArea,
        UpdateGeoArea,
        DeleteGeoArea,

        // Templates
        GetGeoRoadmapTemplates,
        CreateFromTemplate,
        SaveAsTemplate,

        // Map operations
        GeocodeAddress,        // Адреса -> координати
        ReverseGeocode,        // Координати -> адреса
        CalculateRoute,        // Розрахунок маршруту

        // IP Filtering
        GetIpFilterRules,
        AddIpFilterRule,
        UpdateIpFilterRule,
        DeleteIpFilterRule,
        TestIpAccess,

        // Network Core (v0.5 - NetworkCore Integration)
        GetNetworkCoreStatus,
        GetRemoteNodes,
        GetRemoteFileList,
        GetRemoteFileMetadata,
        DownloadRemoteFile,
        PingRemoteNode,
        GetRemoteDirectories,

        // Remote Operations (v0.6 - Full Remote Management)
        // Note: GetDirectoryStatistics, ScanDirectory, CommitDirectory, RevertToCommit, GetGitHistory вже існують вище
        GetDirectoryFileList,
        CreateFolder,
        CreateFile,
        RenameFileOrFolder,
        DeleteFileOrFolder,
        ReadFileContent,
        WriteFileContent,
        ReadFileBinary,
        WriteFileBinary,

        // File Locking (v0.10 - Multi-user support)
        LockFile,
        UnlockFile,
        GetFileLockInfo,
        UpdateFileLockHeartbeat

    }

    [Serializable]
    public class ServiceCommand
    {
        public CommandType Type { get; set; }
        public string Data { get; set; }
    }

    [Serializable]
    public class ServiceResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Data { get; set; }
    }

    // =============== NETWORK CORE REQUESTS (v0.5) ===============

    [Serializable]
    public class RemoteFileListRequest
    {
        public Guid PeerId { get; set; }
        public string Path { get; set; } = "/";
        public string Filter { get; set; } = "*.*";
        public bool IncludeSubdirectories { get; set; } = false;
    }

    [Serializable]
    public class RemoteFileRequest
    {
        public Guid PeerId { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }

    [Serializable]
    public class RemoteDownloadRequest
    {
        public Guid PeerId { get; set; }
        public string RemotePath { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
    }

    [Serializable]
    public class RemoteDirectoriesRequest
    {
        public string DeviceName { get; set; } = string.Empty;
    }

    // =============== REMOTE OPERATIONS MODELS (v0.6) ===============

    /// <summary>
    /// Елемент файлової системи (файл або папка) для відображення в remote browser
    /// </summary>
    [Serializable]
    public class FileSystemItemModel
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public string Extension { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запит статистики віддаленої директорії
    /// </summary>
    [Serializable]
    public class RemoteDirectoryStatisticsRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public int DirectoryId { get; set; }
    }

    /// <summary>
    /// Запит списку файлів/папок у віддаленій директорії
    /// </summary>
    [Serializable]
    public class RemoteDirectoryFileListRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Створення папки на віддаленому пристрої
    /// </summary>
    [Serializable]
    public class RemoteCreateFolderRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Створення файлу на віддаленому пристрої
    /// </summary>
    [Serializable]
    public class RemoteCreateFileRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Перейменування файлу/папки на віддаленому пристрої
    /// </summary>
    [Serializable]
    public class RemoteRenameRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string OldPath { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Видалення файлу/папки на віддаленому пристрої
    /// </summary>
    [Serializable]
    public class RemoteDeleteRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public bool Recursive { get; set; }
    }

    /// <summary>
    /// Читання вмісту віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteReadFileRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запис вмісту віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteWriteFileRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Читання бінарного віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteReadFileBinaryRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Запис бінарного віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteWriteFileBinaryRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Блокування віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteLockFileRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Розблокування віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteUnlockFileRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Отримання інформації про блокування віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteGetFileLockInfoRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Оновлення heartbeat блокування віддаленого файлу
    /// </summary>
    [Serializable]
    public class RemoteUpdateFileLockHeartbeatRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Сканування віддаленої директорії
    /// </summary>
    [Serializable]
    public class RemoteScanDirectoryRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public int DirectoryId { get; set; }
    }

    /// <summary>
    /// Git коміт на віддаленому пристрої
    /// </summary>
    [Serializable]
    public class RemoteGitCommitRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public int DirectoryId { get; set; }
        public string CommitMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Git історія на віддаленому пристрої
    /// </summary>
    [Serializable]
    public class RemoteGitHistoryRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public int DirectoryId { get; set; }
        public int MaxCount { get; set; } = 100;
    }

    /// <summary>
    /// Git відкат до версії на віддаленому пристрої
    /// </summary>
    [Serializable]
    public class RemoteGitRevertRequest
    {
        public string DeviceName { get; set; } = string.Empty;
        public int DirectoryId { get; set; }
        public string CommitHash { get; set; } = string.Empty;
    }

    [Serializable]
    public class ServiceStatus
    {
        public bool IsRunning { get; set; }
        public int TotalDirectories { get; set; }
        public int SharedDirectories { get; set; }
        public int RegisteredDevices { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? LastCommitTime { get; set; }
        public int CommitIntervalMinutes { get; set; }
        public int UnresolvedErrors { get; set; }
        public int TotalGeoRoadmaps { get; set; } // НОВЕ v0.3
        public bool WebApiEnabled { get; set; } // НОВЕ v0.3
    }

    // =============== ЗАПИТИ v0.3 ===============

    [Serializable]
    public class CreateGeoRoadmapRequest
    {
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public MapProvider MapProvider { get; set; }
        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public int ZoomLevel { get; set; }
    }

    [Serializable]
    public class GeocodeRequest
    {
        public string Address { get; set; }
    }

    [Serializable]
    public class GeocodeResponse
    {
        public bool Success { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string FormattedAddress { get; set; }
    }

    [Serializable]
    public class AddDirectoryRequest
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    [Serializable]
    public class UpdateDirectoryNameRequest
    {
        public int DirectoryId { get; set; }
        public string NewName { get; set; }
    }

    [Serializable]
    public class UpdateDirectoryRequest
    {
        public int DirectoryId { get; set; }
        public string NewName { get; set; }
        public string NewPath { get; set; }
    }

    [Serializable]
    public class SearchDirectoriesRequest
    {
        public string SearchQuery { get; set; }
    }

    [Serializable]
    public class DirectoryStatisticsModel
    {
        public int DirectoryId { get; set; }
        public int ObjectsCount { get; set; }
        public int FoldersCount { get; set; }
        public int FilesCount { get; set; }
        public int AllowedDevicesCount { get; set; }
        public bool IsShared { get; set; }
    }

    [Serializable]
    public class AccessRequest
    {
        public int DirectoryId { get; set; }
        public int DeviceId { get; set; }
    }

    [Serializable]
    public class RevertRequest
    {
        public int DirectoryId { get; set; }
        public string CommitHash { get; set; }
    }

    [Serializable]
    public class CommitRequest
    {
        public int DirectoryId { get; set; }
        public string Message { get; set; }
    }

    [Serializable]
    public class AppSettings
    {
        public bool AutoShareOnAdd { get; set; }
        public bool EnableUpdateNotifications { get; set; }
        public int CommitIntervalMinutes { get; set; }
        public bool EnableWebApi { get; set; } // НОВЕ v0.3
        public int WebApiPort { get; set; } = 5000; // НОВЕ v0.3
        public string DefaultMapProvider { get; set; } = "OpenStreetMap"; // НОВЕ v0.3
    }

    // Існуючі моделі (CommitLogModel, ErrorLogModel, ExternalService тощо)
    [Serializable]
    public class CommitLogModel
    {
        public int Id { get; set; }
        public int DirectoryId { get; set; }
        public string DirectoryPath { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
    }

    [Serializable]
    public class ErrorLogModel
    {
        public int Id { get; set; }
        public string ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string UserFriendlyMessage { get; set; }
        public string StackTrace { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsResolved { get; set; }
    }

    [Serializable]
    public class GitCommitHistoryModel
    {
        public string Hash { get; set; }
        public string Message { get; set; }
        public string Author { get; set; }
        public DateTime Date { get; set; }
    }

    [Serializable]
    public class NetworkDevice
    {
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string HostName { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastSeen { get; set; }
    }

    [Serializable]
    public class NetworkInterfaceInfo
    {
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public string MacAddress { get; set; }
        public string NetworkType { get; set; }
        public bool IsActive { get; set; }
    }

    [Serializable]
    public class ExternalService
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ServiceType { get; set; }
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastUsed { get; set; }
    }

}