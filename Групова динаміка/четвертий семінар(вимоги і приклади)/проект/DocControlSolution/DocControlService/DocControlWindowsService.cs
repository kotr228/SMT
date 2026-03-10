using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Services;
using DocControlService.Shared;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetworkCommand = DocControlNetworkCore.Models.NetworkCommand;
using NetworkCommandType = DocControlNetworkCore.Models.CommandType;
using NetworkCommandResponse = DocControlNetworkCore.Models.CommandResponse;
using ServiceCommandType = DocControlService.Shared.CommandType;


namespace DocControlService
{
    /// <summary>
    /// Головний Windows Service для керування мережевими шарами та версіонуванням директорій
    /// </summary>
    public partial class DocControlWindowsService : ServiceBase
    {
        private readonly DatabaseManager _dbManager;
        private readonly DirectoryRepository _dirRepo;
        private readonly DeviceRepository _deviceRepo;
        private readonly NetworkAccessRepository _accessRepo;
        private readonly FileLockRepository _fileLockRepo;
        private readonly DirectoryScanner _scanner;
        private readonly VersionControlFactory _versionFactory;

        private CancellationTokenSource _cancellationTokenSource;
        private Task _pipeServerTask;
        private Task _versionControlTask;
        private readonly bool _debugMode;
        private DateTime _startTime;
        private DateTime? _lastCommitTime;
        private int _commitIntervalMinutes = 720; // 12 годин за замовчуванням

        private CommitLogRepository _commitLogRepo;
        private ErrorLogRepository _errorLogRepo;
        private AppSettingsRepository _settingsRepo;

        private RoadmapRepository _roadmapRepo;
        private RoadmapService _roadmapService;
        private NetworkDiscoveryService _networkService;
        private ExternalServiceRepository _externalServiceRepo;

        private GeoRoadmapRepository _geoRoadmapRepo;
        private GeoMappingService _geoMappingService;
        private IpFilterService _ipFilterService;

        // Координатор файлових систем (локальна + мережева)
        private FileSystemCoordinator _fileSystemCoordinator;

        // Список активних мережевих вузлів (отриманих від DocControlNetworkCore)
        private Dictionary<string, (RemoteNode Node, DateTime LastSeen)> _activeNetworkNodes;
        private readonly object _nodesLock = new object();

        public DocControlWindowsService(bool debugMode = false)
        {
            _debugMode = debugMode;
            InitializeComponent();

            ServiceName = "DocControlService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;

            // Ініціалізація бази даних та репозиторіїв
            _dbManager = new DatabaseManager();
            _dirRepo = new DirectoryRepository(_dbManager);
            _deviceRepo = new DeviceRepository(_dbManager);
            _accessRepo = new NetworkAccessRepository(_dbManager);
            _fileLockRepo = new FileLockRepository(_dbManager);
            _scanner = new DirectoryScanner(_dbManager);
            _versionFactory = new VersionControlFactory(_dirRepo);

            // Ініціалізація геокарт у версії 0.3
            _geoRoadmapRepo = new GeoRoadmapRepository(_dbManager);
            _geoMappingService = new GeoMappingService();
            _ipFilterService = new IpFilterService(_dbManager);

            // Підписуємось на події Git комітів
            foreach (var vcs in _versionFactory.GetAllServices())
            {
                vcs.OnCommitStatusChanged += (path, status, message) =>
                {
                    // Знаходимо ID директорії
                    var dir = _dirRepo.GetAllDirectories().FirstOrDefault(d => d.Browse == path);
                    if (dir != null)
                    {
                        _commitLogRepo.LogCommit(dir.Id, path, status, message);
                    }
                };
            }

            _commitLogRepo = new CommitLogRepository(_dbManager);
            _errorLogRepo = new ErrorLogRepository(_dbManager);
            _settingsRepo = new AppSettingsRepository(_dbManager);

            _roadmapRepo = new RoadmapRepository(_dbManager);
            _roadmapService = new RoadmapService();
            _networkService = new NetworkDiscoveryService();
            _externalServiceRepo = new ExternalServiceRepository(_dbManager);

            // Координатор файлових систем
            _fileSystemCoordinator = new FileSystemCoordinator(_dbManager);

            // Ініціалізація списку активних вузлів
            _activeNetworkNodes = new Dictionary<string, (RemoteNode, DateTime)>();

            // Ініціалізуємо дефолтні налаштування
            _settingsRepo.InitializeDefaults();
        }

        protected override void OnStart(string[] args)
        {
            _startTime = DateTime.Now;
            _cancellationTokenSource = new CancellationTokenSource();

            Log("Service starting...");

            try
            {
                // ВИМКНЕНО: NetworkCore запускається як окремий процес DocControlNetworkCore.exe
                // Він передає знайдені пристрої через Named Pipes
                // var sharedDir = GetSharedDirectory();
                // _fileSystemCoordinator.StartNetworkCore(sharedDir);
                // Log($"Network Core started with shared directory: {sharedDir}");

                // Відновлюємо мережеві шари для активних директорій (DEPRECATED - буде замінено на NetworkCore)
                // RestoreNetworkShares();

                // Запускаємо Named Pipe сервер для комунікації з UI
                _pipeServerTask = Task.Run(() => RunPipeServer(_cancellationTokenSource.Token));

                // Запускаємо автоматичне версіонування
                _versionControlTask = Task.Run(() => AutoCommitLoop(_cancellationTokenSource.Token));

                Log("Service started successfully");
            }
            catch (Exception ex)
            {
                Log($"Error during service start: {ex.Message}", EventLogEntryType.Error);
                throw;
            }
        }

        protected override void OnStop()
        {
            Log("Service stopping...");

            try
            {
                _cancellationTokenSource?.Cancel();

                // Зупиняємо мережеве ядро
                _fileSystemCoordinator?.StopNetworkCore();

                // Чекаємо завершення задач
                Task.WaitAll(new[] { _pipeServerTask, _versionControlTask }
                    .Where(t => t != null).ToArray(),
                    TimeSpan.FromSeconds(10));

                // Закриваємо всі шари при зупинці сервісу (DEPRECATED)
                // CloseAllNetworkShares();

                Log("Service stopped");
            }
            catch (Exception ex)
            {
                Log($"Error during service stop: {ex.Message}", EventLogEntryType.Warning);
            }
        }

        #region Version Control

        private async Task AutoCommitLoop(CancellationToken cancellationToken)
        {
            Log($"Starting auto-commit loop (interval: {_commitIntervalMinutes} minutes)...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_commitIntervalMinutes), cancellationToken);

                    Log("Performing scheduled commit...");
                    PerformCommitForAllDirectories();
                    _lastCommitTime = DateTime.Now;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Error in auto-commit: {ex.Message}", EventLogEntryType.Warning);
                }
            }
        }

        private void PerformCommitForAllDirectories()
        {
            var services = _versionFactory.GetAllServices();
            foreach (var vcs in services)
            {
                try
                {
                    vcs.CommitAll($"Auto-commit at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    Log($"Error committing: {ex.Message}", EventLogEntryType.Warning);
                }
            }
        }

        #endregion

        #region Named Pipe Communication

        private async Task RunPipeServer(CancellationToken cancellationToken)
        {
            Log("Starting Named Pipe server...");

            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream pipeServer = null;
                try
                {
                    pipeServer = new NamedPipeServerStream(
                        "DocControlServicePipe",
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,  // ЗМІНЕНО: дозволяємо кілька з'єднань
                        PipeTransmissionMode.Message,                      // ЗМІНЕНО: Message mode
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough);

                    Log("📞 Waiting for client connection...");
                    await pipeServer.WaitForConnectionAsync(cancellationToken);
                    Log("✅ Client connected");

                    try
                    {
                        // Читаємо запит
                        using (var reader = new StreamReader(pipeServer, Encoding.UTF8, false, 1024, true))
                        {
                            string request = await reader.ReadLineAsync();

                            if (string.IsNullOrEmpty(request))
                            {
                                Log("⚠️ Empty request received");
                                continue;
                            }

                            Log($"📨 Request: {request.Substring(0, Math.Min(100, request.Length))}");

                            // Обробляємо команду
                            var response = await ProcessCommand(request);

                            // Серіалізуємо відповідь
                            var responseJson = JsonSerializer.Serialize(response);
                            Log($"📤 Response ready: {responseJson.Length} chars, Success={response.Success}");

                            // КРИТИЧНО: Пишемо відповідь в новому блоці
                            using (var writer = new StreamWriter(pipeServer, Encoding.UTF8, 1024, true))
                            {
                                writer.AutoFlush = true;
                                await writer.WriteLineAsync(responseJson);
                                await writer.FlushAsync();
                                Log("✅ Response written to pipe");
                            }

                            // Чекаємо щоб клієнт прочитав
                            await Task.Delay(200, cancellationToken);
                            Log("✅ Response sent successfully");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ Error processing request: {ex.Message}");
                    }
                    finally
                    {
                        try
                        {
                            if (pipeServer?.IsConnected == true)
                            {
                                pipeServer.Disconnect();
                                Log("🔌 Client disconnected");
                            }
                        }
                        catch { }
                    }
                }
                catch (OperationCanceledException)
                {
                    Log("🛑 Pipe server cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    Log($"❌ Pipe server error: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
                finally
                {
                    try
                    {
                        pipeServer?.Dispose();
                    }
                    catch { }
                }
            }

            Log("Pipe server stopped");
        }

        private async Task<ServiceResponse> ProcessCommand(string requestJson)
        {
            try
            {
                var command = JsonSerializer.Deserialize<ServiceCommand>(requestJson);

                switch (command.Type)
                {
                    case ServiceCommandType.GetDirectories:
                        return HandleGetDirectories();

                    case ServiceCommandType.AddDirectory:
                        return HandleAddDirectory(command.Data);

                    case ServiceCommandType.RemoveDirectory:
                        return HandleRemoveDirectory(command.Data);

                    case ServiceCommandType.UpdateDirectoryName:
                        return HandleUpdateDirectoryName(command.Data);

                    case ServiceCommandType.UpdateDirectory:
                        return HandleUpdateDirectory(command.Data);

                    case ServiceCommandType.ScanDirectory:
                        return HandleScanDirectory(command.Data);

                    case ServiceCommandType.SearchDirectories:
                        return HandleSearchDirectories(command.Data);

                    case ServiceCommandType.GetDirectoryStatistics:
                        return HandleGetDirectoryStatistics(command.Data);

                    case ServiceCommandType.GetDevices:
                        return HandleGetDevices();

                    case ServiceCommandType.AddDevice:
                        return HandleAddDevice(command.Data);

                    case ServiceCommandType.RemoveDevice:
                        return HandleRemoveDevice(command.Data);

                    case ServiceCommandType.GrantAccess:
                        return HandleGrantAccess(command.Data);

                    case ServiceCommandType.RevokeAccess:
                        return HandleRevokeAccess(command.Data);

                    case ServiceCommandType.GetNetworkAccess:
                        return HandleGetNetworkAccess(command.Data);

                    case ServiceCommandType.GetStatus:
                        return HandleGetStatus();

                    case ServiceCommandType.ForceCommit:
                        return HandleForceCommit();

                    case ServiceCommandType.CommitDirectory:
                        return HandleCommitDirectory(command.Data);

                    case ServiceCommandType.SetCommitInterval:
                        return HandleSetCommitInterval(command.Data);

                    case ServiceCommandType.GetCommitLog:
                        return HandleGetCommitLog(command.Data);

                    case ServiceCommandType.GetGitHistory:
                        return HandleGetGitHistory(command.Data);

                    case ServiceCommandType.RevertToCommit:
                        return HandleRevertToCommit(command.Data);

                    case ServiceCommandType.GetErrorLog:
                        return HandleGetErrorLog(command.Data);

                    case ServiceCommandType.MarkErrorResolved:
                        return HandleMarkErrorResolved(command.Data);

                    case ServiceCommandType.ClearResolvedErrors:
                        return HandleClearResolvedErrors();

                    case ServiceCommandType.GetUnresolvedErrorCount:
                        return HandleGetUnresolvedErrorCount();

                    case ServiceCommandType.GetSettings:
                        return HandleGetSettings();

                    case ServiceCommandType.SaveSettings:
                        return HandleSaveSettings(command.Data);

                    case ServiceCommandType.CreateRoadmap:
                        return HandleCreateRoadmap(command.Data);

                    case ServiceCommandType.GetRoadmaps:
                        return HandleGetRoadmaps();

                    case ServiceCommandType.GetRoadmapById:
                        return HandleGetRoadmapById(command.Data);

                    case ServiceCommandType.DeleteRoadmap:
                        return HandleDeleteRoadmap(command.Data);

                    case ServiceCommandType.AnalyzeDirectoryForRoadmap:
                        return HandleAnalyzeDirectoryForRoadmap(command.Data);

                    case ServiceCommandType.ExportRoadmapAsJson:
                        return HandleExportRoadmapAsJson(command.Data);

                    case ServiceCommandType.ScanNetwork:
                        return HandleScanNetwork();

                    case ServiceCommandType.GetNetworkInterfaces:
                        return HandleGetNetworkInterfaces();

                    case ServiceCommandType.GetExternalServices:
                        return HandleGetExternalServices();

                    case ServiceCommandType.AddExternalService:
                        return HandleAddExternalService(command.Data);

                    case ServiceCommandType.UpdateExternalService:
                        return HandleUpdateExternalService(command.Data);

                    case ServiceCommandType.DeleteExternalService:
                        return HandleDeleteExternalService(command.Data);

                    case ServiceCommandType.TestExternalService:
                        return HandleTestExternalService(command.Data);

                    case ServiceCommandType.CreateGeoRoadmap:
                        return HandleCreateGeoRoadmap(command.Data);

                    case ServiceCommandType.GetGeoRoadmaps:
                        return HandleGetGeoRoadmaps();

                    case ServiceCommandType.GetGeoRoadmapById:
                        return HandleGetGeoRoadmapById(command.Data);

                    case ServiceCommandType.UpdateGeoRoadmap:
                        return HandleUpdateGeoRoadmap(command.Data);

                    case ServiceCommandType.DeleteGeoRoadmap:
                        return HandleDeleteGeoRoadmap(command.Data);

                    case ServiceCommandType.AddGeoNode:
                        return HandleAddGeoNode(command.Data);

                    case ServiceCommandType.UpdateGeoNode:
                        return HandleUpdateGeoNode(command.Data);

                    case ServiceCommandType.DeleteGeoNode:
                        return HandleDeleteGeoNode(command.Data);

                    case ServiceCommandType.GetGeoNodesByRoadmap:
                        return HandleGetGeoNodesByRoadmap(command.Data);

                    case ServiceCommandType.AddGeoRoute:
                        return HandleAddGeoRoute(command.Data);

                    case ServiceCommandType.DeleteGeoRoute:
                        return HandleDeleteGeoRoute(command.Data);

                    case ServiceCommandType.AddGeoArea:
                        return HandleAddGeoArea(command.Data);

                    case ServiceCommandType.DeleteGeoArea:
                        return HandleDeleteGeoArea(command.Data);

                    case ServiceCommandType.GetGeoRoadmapTemplates:
                        return HandleGetGeoRoadmapTemplates();

                    case ServiceCommandType.CreateFromTemplate:
                        return HandleCreateFromTemplate(command.Data);

                    case ServiceCommandType.SaveAsTemplate:
                        return HandleSaveAsTemplate(command.Data);

                    case ServiceCommandType.GeocodeAddress:
                        return HandleGeocodeAddress(command.Data);

                    case ServiceCommandType.ReverseGeocode:
                        return HandleReverseGeocode(command.Data);

                    case ServiceCommandType.GetIpFilterRules:
                        return HandleGetIpFilterRules();

                    case ServiceCommandType.AddIpFilterRule:
                        return HandleAddIpFilterRule(command.Data);

                    case ServiceCommandType.UpdateIpFilterRule:
                        return HandleUpdateIpFilterRule(command.Data);

                    case ServiceCommandType.DeleteIpFilterRule:
                        return HandleDeleteIpFilterRule(command.Data);

                    case ServiceCommandType.TestIpAccess:
                        return HandleTestIpAccess(command.Data);

                    // Network Core commands
                    case ServiceCommandType.GetNetworkCoreStatus:
                        return HandleGetNetworkCoreStatus();

                    case ServiceCommandType.GetRemoteNodes:
                        return HandleGetRemoteNodes();

                    case ServiceCommandType.GetRemoteFileList:
                        return await HandleGetRemoteFileListAsync(command.Data);

                    case ServiceCommandType.GetRemoteFileMetadata:
                        return await HandleGetRemoteFileMetadataAsync(command.Data);

                    case ServiceCommandType.DownloadRemoteFile:
                        return await HandleDownloadRemoteFileAsync(command.Data);

                    case ServiceCommandType.PingRemoteNode:
                        return await HandlePingRemoteNodeAsync(command.Data);

                    case ServiceCommandType.GetRemoteDirectories:
                        return await HandleGetRemoteDirectoriesAsync(command.Data);

                    // GetDirectoryStatistics та ScanDirectory вже обробляються вище (lines 459, 453)
                    // CommitDirectory та RevertToCommit також обробляються вище (lines 492, 504)

                    case ServiceCommandType.GetDirectoryFileList:
                        return await HandleGetDirectoryFileList(command.Data);

                    case ServiceCommandType.CreateFolder:
                        return await HandleCreateFolder(command.Data);

                    case ServiceCommandType.CreateFile:
                        return await HandleCreateFile(command.Data);

                    case ServiceCommandType.RenameFileOrFolder:
                        return await HandleRenameFileOrFolder(command.Data);

                    case ServiceCommandType.DeleteFileOrFolder:
                        return await HandleDeleteFileOrFolder(command.Data);

                    case ServiceCommandType.ReadFileContent:
                        return await HandleReadFileContent(command.Data);

                    case ServiceCommandType.WriteFileContent:
                        return await HandleWriteFileContent(command.Data);

                    case ServiceCommandType.ReadFileBinary:
                        return await HandleReadFileBinary(command.Data);

                    case ServiceCommandType.WriteFileBinary:
                        return await HandleWriteFileBinary(command.Data);

                    case ServiceCommandType.LockFile:
                        return await HandleLockFile(command.Data);

                    case ServiceCommandType.UnlockFile:
                        return await HandleUnlockFile(command.Data);

                    case ServiceCommandType.GetFileLockInfo:
                        return await HandleGetFileLockInfo(command.Data);

                    case ServiceCommandType.UpdateFileLockHeartbeat:
                        return await HandleUpdateFileLockHeartbeat(command.Data);

                    default:
                        return new ServiceResponse
                        {
                            Success = false,
                            Message = "Unknown command type"
                        };
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing command: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        #endregion

        #region Command Handlers

        private ServiceResponse HandleGetDirectories()
        {
            Console.WriteLine($"[Service] HandleGetDirectories: Завантаження директорій...");
            var directories = _dirRepo.GetAllDirectories();
            Console.WriteLine($"[Service] HandleGetDirectories: Знайдено {directories.Count} директорій в БД");

            var result = directories.Select(d =>
            {
                // Отримуємо статус Git для директорії
                string gitStatus = "Не ініціалізовано";
                try
                {
                    var vcs = _versionFactory.GetServiceFor(d.Id);
                    if (vcs != null)
                    {
                        gitStatus = vcs.GetStatus();
                    }
                }
                catch (Exception ex)
                {
                    gitStatus = $"Помилка: {ex.Message}";
                }

                var allowedDevices = _accessRepo.GetAllowedDevicesForDirectory(d.Id);
                Console.WriteLine($"[Service]   - Директорія ID={d.Id}, Name='{d.Name}', AllowedDevices: {allowedDevices.Count}");

                return new DirectoryWithAccessModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Browse = d.Browse,
                    IsShared = _accessRepo.IsDirectoryShared(d.Id),
                    GitStatus = gitStatus,
                    AllowedDevices = allowedDevices
                };
            }).ToList();

            Console.WriteLine($"[Service] HandleGetDirectories: Повертаємо {result.Count} директорій з даними доступу");

            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(result)
            };
        }

        private ServiceResponse HandleAddDirectory(string data)
        {
            var request = JsonSerializer.Deserialize<AddDirectoryRequest>(data);

            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Path))
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Name and Path are required"
                };
            }

            if (!Directory.Exists(request.Path))
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Directory does not exist: {request.Path}"
                };
            }

            // Додаємо директорію
            int dirId = _dirRepo.AddDirectory(request.Name, request.Path);

            // Сканування директорії для заповнення залежних таблиць
            _scanner.ScanDirectoryById(dirId);

            // Ініціалізуємо Git репозиторій
            var vcs = _versionFactory.GetServiceFor(dirId);

            Log($"Added directory: {request.Name} (id={dirId})");

            return new ServiceResponse
            {
                Success = true,
                Message = "Directory added successfully",
                Data = dirId.ToString()
            };
        }

        private ServiceResponse HandleRemoveDirectory(string data)
        {
            int dirId = int.Parse(data);
            var dir = _dirRepo.GetById(dirId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            // Видаляємо всі записи доступу
            _accessRepo.SetDirectoryAccessStatus(dirId, false);

            // Видаляємо директорію
            bool deleted = _dirRepo.DeleteDirectory(dirId);

            if (deleted)
            {
                Log($"Removed directory: {dir.Name} (id={dirId})");
                return new ServiceResponse
                {
                    Success = true,
                    Message = "Directory removed successfully"
                };
            }

            return new ServiceResponse
            {
                Success = false,
                Message = "Failed to remove directory"
            };
        }

        private ServiceResponse HandleUpdateDirectoryName(string data)
        {
            var request = JsonSerializer.Deserialize<UpdateDirectoryNameRequest>(data);
            var dir = _dirRepo.GetById(request.DirectoryId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            // Оновлюємо ім'я директорії (потрібно додати метод в DirectoryRepository)
            // Поки що створюємо простий UPDATE
            using (var conn = _dbManager.GetConnection())
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE directory SET Name = @name WHERE id = @id;";
                cmd.Parameters.AddWithValue("@name", request.NewName);
                cmd.Parameters.AddWithValue("@id", request.DirectoryId);
                cmd.ExecuteNonQuery();
            }

            Log($"Updated directory name: {dir.Name} -> {request.NewName}");

            return new ServiceResponse
            {
                Success = true,
                Message = "Directory name updated successfully"
            };
        }

        private ServiceResponse HandleScanDirectory(string data)
        {
            int dirId;

            // Спробуємо спочатку як RemoteScanDirectoryRequest (для remote операцій)
            try
            {
                var remoteRequest = JsonSerializer.Deserialize<RemoteScanDirectoryRequest>(data);
                if (!string.IsNullOrEmpty(remoteRequest.DeviceName))
                {
                    // Це remote запит
                    dirId = remoteRequest.DirectoryId;
                }
                else
                {
                    // Не remote, спробуємо як простий int
                    dirId = int.Parse(data);
                }
            }
            catch
            {
                // Якщо не RemoteScanDirectoryRequest, то просто int
                dirId = int.Parse(data);
            }

            var dir = _dirRepo.GetById(dirId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            _scanner.ScanDirectoryById(dirId);

            Log($"Scanned directory: {dir.Name} (id={dirId})");

            return new ServiceResponse
            {
                Success = true,
                Message = "Directory scanned successfully"
            };
        }

        private ServiceResponse HandleUpdateDirectory(string data)
        {
            var request = JsonSerializer.Deserialize<UpdateDirectoryRequest>(data);
            var dir = _dirRepo.GetById(request.DirectoryId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            bool success = _dirRepo.UpdateDirectory(request.DirectoryId, request.NewName, request.NewPath);

            if (success)
            {
                Log($"Updated directory: {dir.Name} -> {request.NewName}, {dir.Browse} -> {request.NewPath}");
                return new ServiceResponse
                {
                    Success = true,
                    Message = "Directory updated successfully"
                };
            }

            return new ServiceResponse
            {
                Success = false,
                Message = "Failed to update directory"
            };
        }

        private ServiceResponse HandleSearchDirectories(string data)
        {
            var request = JsonSerializer.Deserialize<SearchDirectoriesRequest>(data);
            var directories = _dirRepo.SearchDirectories(request.SearchQuery);

            // Конвертуємо в DirectoryWithAccessModel з GitStatus
            var result = directories.Select(d =>
            {
                // Отримуємо статус Git для директорії
                string gitStatus = "Не ініціалізовано";
                try
                {
                    var vcs = _versionFactory.GetServiceFor(d.Id);
                    if (vcs != null)
                    {
                        gitStatus = vcs.GetStatus();
                    }
                }
                catch (Exception ex)
                {
                    gitStatus = $"Помилка: {ex.Message}";
                }

                return new DirectoryWithAccessModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Browse = d.Browse,
                    IsShared = _accessRepo.IsDirectoryShared(d.Id),
                    GitStatus = gitStatus,
                    AllowedDevices = _accessRepo.GetAllowedDevicesForDirectory(d.Id)
                };
            }).ToList();

            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(result),
                Message = $"Found {result.Count} directories"
            };
        }

        private ServiceResponse HandleGetDirectoryStatistics(string data)
        {
            int dirId;

            // Спробуємо спочатку як RemoteDirectoryStatisticsRequest (для remote операцій)
            try
            {
                var remoteRequest = JsonSerializer.Deserialize<RemoteDirectoryStatisticsRequest>(data);
                if (!string.IsNullOrEmpty(remoteRequest.DeviceName))
                {
                    // Це remote запит
                    dirId = remoteRequest.DirectoryId;
                }
                else
                {
                    // Не remote, спробуємо як простий int
                    dirId = int.Parse(data);
                }
            }
            catch
            {
                // Якщо не RemoteDirectoryStatisticsRequest, то просто int
                dirId = int.Parse(data);
            }

            var dir = _dirRepo.GetById(dirId);

            if (dir == null)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Directory not found"
                };
            }

            var stats = _dirRepo.GetDirectoryStatistics(dirId);

            // Конвертуємо в модель для клієнта
            var statsModel = new DirectoryStatisticsModel
            {
                DirectoryId = stats.DirectoryId,
                ObjectsCount = stats.ObjectsCount,
                FoldersCount = stats.FoldersCount,
                FilesCount = stats.FilesCount,
                AllowedDevicesCount = stats.AllowedDevicesCount,
                IsShared = stats.IsShared
            };

            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(statsModel)
            };
        }

        private ServiceResponse HandleGetDevices()
        {
            var devices = _deviceRepo.GetAllDevices();
            Console.WriteLine($"[Service] HandleGetDevices: Завантажено {devices.Count} пристроїв з БД");

            // Отримати список онлайн вузлів з внутрішнього кешу (оновлюється NetworkCore через Named Pipe)
            List<string> activeDeviceNames;
            lock (_nodesLock)
            {
                Console.WriteLine($"[Service] HandleGetDevices: Активних вузлів у кеші: {_activeNetworkNodes.Count}");
                foreach (var kvp in _activeNetworkNodes)
                {
                    var age = (DateTime.UtcNow - kvp.Value.LastSeen).TotalSeconds;
                    Console.WriteLine($"[Service]   - Вузол '{kvp.Key}', LastSeen: {age:F1}s назад");
                }

                // Очистити старі вузли (timeout 60 секунд)
                var now = DateTime.UtcNow;
                var expiredKeys = _activeNetworkNodes
                    .Where(kvp => (now - kvp.Value.LastSeen).TotalSeconds > 60)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _activeNetworkNodes.Remove(key);
                    Log($"[Network] Вузол видалено (timeout): {key}");
                    Console.WriteLine($"[Service] ⏰ Вузол '{key}' видалено через timeout");
                }

                activeDeviceNames = _activeNetworkNodes.Keys.ToList();
                Console.WriteLine($"[Service] Активних вузлів після очищення: {activeDeviceNames.Count}");
            }

            // Отримати всі доступи для підрахунку кількості директорій
            var allAccess = _accessRepo.GetAllAccess();
            var accessCountByDevice = allAccess
                .Where(a => a.Status) // Тільки активні доступи
                .GroupBy(a => a.DeviceId)
                .ToDictionary(g => g.Key, g => g.Count());

            // Оновити інформацію про кожний пристрій
            foreach (var device in devices)
            {
                // Позначити чи пристрій онлайн
                device.IsOnline = activeDeviceNames.Contains(device.Name);

                // Підрахувати кількість директорій до яких є доступ
                device.AccessDirectoriesCount = accessCountByDevice.ContainsKey(device.Id)
                    ? accessCountByDevice[device.Id]
                    : 0;

                Console.WriteLine($"[Service]   - Пристрій '{device.Name}': IsOnline={device.IsOnline}, Directories={device.AccessDirectoriesCount}");
            }

            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(devices)
            };
        }

        private ServiceResponse HandleAddDevice(string data)
        {
            var device = JsonSerializer.Deserialize<DeviceModel>(data);
            var existingDevice = _deviceRepo.GetOrCreateDevice(device.Name, device.Access);
            int deviceId = existingDevice.Id;

            Console.WriteLine($"[Service] HandleAddDevice: '{device.Name}' -> ID={deviceId}");
            Log($"Device registered: {device.Name} (id={deviceId})");

            // Парсимо назву пристрою для додавання до списку активних вузлів
            // Формат: "username@machinename (ip)"
            try
            {
                var parts = device.Name.Split(new[] { '@', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    string userName = parts[0].Trim();
                    string machineName = parts[1].Trim();
                    string ipAddress = parts[2].Trim();

                    var remoteNode = new RemoteNode
                    {
                        InstanceId = Guid.NewGuid(),
                        UserName = userName,
                        MachineName = machineName,
                        IpAddress = ipAddress,
                        TcpPort = 8000,
                        IsOnline = true,
                        LastSeen = DateTime.UtcNow
                    };

                    lock (_nodesLock)
                    {
                        _activeNetworkNodes[device.Name] = (remoteNode, DateTime.UtcNow);
                        Console.WriteLine($"[Service] ✅ Оновлено _activeNetworkNodes['{device.Name}'], LastSeen={DateTime.UtcNow:HH:mm:ss}");
                    }

                    Log($"[Network] Додано/оновлено активний вузол: {device.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка парсингу: {ex.Message}");
                Log($"[Network] Помилка парсингу назви пристрою: {ex.Message}", EventLogEntryType.Warning);
            }

            return new ServiceResponse
            {
                Success = true,
                Message = "Device added successfully",
                Data = deviceId.ToString()
            };
        }

        private ServiceResponse HandleRemoveDevice(string data)
        {
            int deviceId = int.Parse(data);
            bool deleted = _deviceRepo.DeleteDevice(deviceId);

            if (deleted)
            {
                Log($"Removed device (id={deviceId})");
                return new ServiceResponse
                {
                    Success = true,
                    Message = "Device removed successfully"
                };
            }

            return new ServiceResponse
            {
                Success = false,
                Message = "Failed to remove device"
            };
        }

        private ServiceResponse HandleGrantAccess(string data)
        {
            var request = JsonSerializer.Deserialize<AccessRequest>(data);

            // Надаємо доступ в БД
            int accessId = _accessRepo.GrantAccess(request.DirectoryId, request.DeviceId);

            // ПРИМІТКА: NetworkCore використовує власну систему доступу через БД
            // Windows Shares НЕ потрібні - доступ контролюється в CommandLayerService

            Log($"Granted access: Directory {request.DirectoryId} -> Device {request.DeviceId} (NetworkCore)");

            return new ServiceResponse
            {
                Success = true,
                Message = "Access granted successfully"
            };
        }

        private ServiceResponse HandleRevokeAccess(string data)
        {
            var request = JsonSerializer.Deserialize<AccessRequest>(data);

            bool revoked = _accessRepo.RevokeAccess(request.DirectoryId, request.DeviceId);

            // ПРИМІТКА: NetworkCore використовує власну систему доступу через БД
            // Windows Shares НЕ потрібні - доступ контролюється в CommandLayerService

            Log($"Revoked access: Directory {request.DirectoryId} -> Device {request.DeviceId} (NetworkCore)");

            return new ServiceResponse
            {
                Success = revoked,
                Message = revoked ? "Access revoked successfully" : "Failed to revoke access"
            };
        }

        private ServiceResponse HandleGetNetworkAccess(string data)
        {
            try
            {
                int directoryId = int.Parse(data);
                var accessList = _accessRepo.GetAccessByDirectory(directoryId);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(accessList),
                    Message = "Network access list retrieved successfully"
                };
            }
            catch (Exception ex)
            {
                Log($"Error getting network access: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = $"Failed to get network access: {ex.Message}"
                };
            }
        }

        private ServiceResponse HandleGetStatus()
        {
            var status = new ServiceStatus
            {
                IsRunning = true,
                TotalDirectories = _dirRepo.GetAllDirectories().Count,
                SharedDirectories = _dirRepo.GetAllDirectories().Count(d => _accessRepo.IsDirectoryShared(d.Id)),
                RegisteredDevices = _deviceRepo.GetAllDevices().Count,
                StartTime = _startTime,
                LastCommitTime = _lastCommitTime,
                CommitIntervalMinutes = _commitIntervalMinutes,
                UnresolvedErrors = _errorLogRepo.GetUnresolvedCount()
            };

            return new ServiceResponse
            {
                Success = true,
                Data = JsonSerializer.Serialize(status)
            };
        }

        private ServiceResponse HandleForceCommit()
        {
            PerformCommitForAllDirectories();
            _lastCommitTime = DateTime.Now;

            Log("Forced commit executed");

            return new ServiceResponse
            {
                Success = true,
                Message = "Commit performed successfully"
            };
        }

        private ServiceResponse HandleCommitDirectory(string data)
        {
            try
            {
                // Спробуємо спочатку як RemoteGitCommitRequest (для remote операцій)
                try
                {
                    var remoteRequest = JsonSerializer.Deserialize<RemoteGitCommitRequest>(data);
                    if (!string.IsNullOrEmpty(remoteRequest.DeviceName))
                    {
                        // Це remote запит, використовуємо directoryId та message
                        var vcs = _versionFactory.GetServiceFor(remoteRequest.DirectoryId);
                        if (vcs == null)
                        {
                            return new ServiceResponse
                            {
                                Success = false,
                                Message = "Version control service not found for directory"
                            };
                        }

                        string message = string.IsNullOrWhiteSpace(remoteRequest.CommitMessage)
                            ? $"Remote commit at {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                            : remoteRequest.CommitMessage;

                        vcs.CommitAll(message);
                        Log($"Remote commit executed for directory {remoteRequest.DirectoryId}: {message}");

                        return new ServiceResponse
                        {
                            Success = true,
                            Message = "Commit performed successfully"
                        };
                    }
                }
                catch { }

                // Якщо не RemoteGitCommitRequest, то звичайний CommitRequest
                var request = JsonSerializer.Deserialize<CommitRequest>(data);
                var vcs2 = _versionFactory.GetServiceFor(request.DirectoryId);

                if (vcs2 == null)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Version control service not found for directory"
                    };
                }

                string msg = string.IsNullOrWhiteSpace(request.Message)
                    ? $"Manual commit at {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    : request.Message;

                vcs2.CommitAll(msg);
                Log($"Commit executed for directory {request.DirectoryId}: {msg}");

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Commit performed successfully"
                };
            }
            catch (Exception ex)
            {
                Log($"Error committing directory: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        private ServiceResponse HandleSetCommitInterval(string data)
        {
            int minutes = int.Parse(data);

            if (minutes < 1)
            {
                return new ServiceResponse
                {
                    Success = false,
                    Message = "Interval must be at least 1 minute"
                };
            }

            _commitIntervalMinutes = minutes;

            Log($"Commit interval set to {minutes} minutes");

            return new ServiceResponse
            {
                Success = true,
                Message = $"Commit interval set to {minutes} minutes"
            };
        }

        #endregion

        #region Network Core Handlers

        /// <summary>
        /// Отримати статус мережевого ядра
        /// </summary>
        private ServiceResponse HandleGetNetworkCoreStatus()
        {
            try
            {
                // Перевіряємо чи процес DocControlNetworkCore.exe запущений
                bool isRunning = false;
                PeerIdentity localIdentity = null;

                try
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName("DocControlNetworkCore");
                    isRunning = processes.Length > 0;

                    // Якщо процес запущений, спробуємо отримати локальну ідентичність з активних вузлів
                    if (isRunning)
                    {
                        // Створюємо базову ідентичність з системної інформації
                        localIdentity = new PeerIdentity
                        {
                            InstanceId = Guid.NewGuid(),
                            UserName = System.Environment.UserName,
                            MachineName = System.Environment.MachineName,
                            IpAddress = GetLocalIpAddress(),
                            TcpPort = 8000,
                            UdpPort = 9000,
                            LastSeen = DateTime.UtcNow,
                            ProtocolVersion = "1.0"
                        };
                    }
                }
                catch
                {
                    // Якщо не можемо перевірити процес - вважаємо що не запущений
                    isRunning = false;
                }

                var status = new
                {
                    IsRunning = isRunning,
                    LocalIdentity = localIdentity
                };

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(status)
                };
            }
            catch (Exception ex)
            {
                Log($"Error getting network core status: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Отримати список активних віддалених вузлів
        /// </summary>
        private ServiceResponse HandleGetRemoteNodes()
        {
            try
            {
                // Отримати список вузлів з внутрішнього кешу
                List<RemoteNode> nodes;
                lock (_nodesLock)
                {
                    // Очистити старі вузли (timeout 60 секунд)
                    var now = DateTime.UtcNow;
                    var expiredKeys = _activeNetworkNodes
                        .Where(kvp => (now - kvp.Value.LastSeen).TotalSeconds > 60)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in expiredKeys)
                    {
                        _activeNetworkNodes.Remove(key);
                    }

                    nodes = _activeNetworkNodes.Values.Select(v => v.Node).ToList();
                }

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(nodes)
                };
            }
            catch (Exception ex)
            {
                Log($"Error getting remote nodes: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Отримати список файлів з віддаленого вузла
        /// </summary>
        private async Task<ServiceResponse> HandleGetRemoteFileListAsync(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteFileListRequest>(data);
                if (request == null)
                {
                    return new ServiceResponse { Success = false, Message = "Invalid request" };
                }

                var remoteFs = _fileSystemCoordinator.GetRemoteFileSystem(request.PeerId);
                if (remoteFs == null)
                {
                    return new ServiceResponse { Success = false, Message = "Remote node not found" };
                }

                var result = await remoteFs.GetFileListAsync(request.Path, request.Filter, request.IncludeSubdirectories);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(result)
                };
            }
            catch (Exception ex)
            {
                Log($"Error getting remote file list: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// Отримати метадані файлу з віддаленого вузла
        /// </summary>
        private async Task<ServiceResponse> HandleGetRemoteFileMetadataAsync(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteFileRequest>(data);
                if (request == null)
                {
                    return new ServiceResponse { Success = false, Message = "Invalid request" };
                }

                var remoteFs = _fileSystemCoordinator.GetRemoteFileSystem(request.PeerId);
                if (remoteFs == null)
                {
                    return new ServiceResponse { Success = false, Message = "Remote node not found" };
                }

                var metadata = await remoteFs.GetFileMetadataAsync(request.FilePath);

                if (metadata == null)
                {
                    return new ServiceResponse { Success = false, Message = "File not found" };
                }

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(metadata)
                };
            }
            catch (Exception ex)
            {
                Log($"Error getting remote file metadata: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// Завантажити файл з віддаленого вузла
        /// </summary>
        private async Task<ServiceResponse> HandleDownloadRemoteFileAsync(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteDownloadRequest>(data);
                if (request == null)
                {
                    return new ServiceResponse { Success = false, Message = "Invalid request" };
                }

                var remoteFs = _fileSystemCoordinator.GetRemoteFileSystem(request.PeerId);
                if (remoteFs == null)
                {
                    return new ServiceResponse { Success = false, Message = "Remote node not found" };
                }

                var success = await remoteFs.DownloadFileAsync(request.RemotePath, request.LocalPath);

                return new ServiceResponse
                {
                    Success = success,
                    Message = success ? "File downloaded successfully" : "Download failed"
                };
            }
            catch (Exception ex)
            {
                Log($"Error downloading remote file: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// Перевірити доступність віддаленого вузла
        /// </summary>
        private async Task<ServiceResponse> HandlePingRemoteNodeAsync(string data)
        {
            try
            {
                var peerId = Guid.Parse(data);
                var remoteFs = _fileSystemCoordinator.GetRemoteFileSystem(peerId);

                if (remoteFs == null)
                {
                    return new ServiceResponse { Success = false, Message = "Remote node not found" };
                }

                var isAvailable = await remoteFs.PingAsync();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(new { IsAvailable = isAvailable })
                };
            }
            catch (Exception ex)
            {
                Log($"Error pinging remote node: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// Отримати список shared директорій з віддаленого пристрою
        /// </summary>
        private async Task<ServiceResponse> HandleGetRemoteDirectoriesAsync(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteDirectoriesRequest>(data);
                Console.WriteLine($"[Service] HandleGetRemoteDirectories: Запит директорій з пристрою '{request.DeviceName}'");

                // Знайти RemoteNode в кеші активних вузлів
                RemoteNode remoteNode = null;
                lock (_nodesLock)
                {
                    if (_activeNetworkNodes.TryGetValue(request.DeviceName, out var nodeInfo))
                    {
                        remoteNode = nodeInfo.Node;
                        Console.WriteLine($"[Service] Знайдено вузол: IP={remoteNode.IpAddress}, Port={remoteNode.TcpPort}");
                    }
                }

                if (remoteNode == null)
                {
                    Console.WriteLine($"[Service] ❌ Пристрій '{request.DeviceName}' не знайдено в активних вузлах");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = $"Пристрій '{request.DeviceName}' не в мережі або не знайдено"
                    };
                }

                // Відправити NetworkCommand до віддаленого пристрою
                var networkCommand = new NetworkCommand
                {
                    Type = NetworkCommandType.GetSharedDirectories,
                    SenderId = Guid.Empty, // Можна використати локальний InstanceId якщо потрібно
                    Payload = ""
                };

                Console.WriteLine($"[Service] Відправка GetSharedDirectories до {remoteNode.IpAddress}:{remoteNode.TcpPort}");

                // Використовуємо TCP клієнт для відправки команди
                using var tcpClient = new System.Net.Sockets.TcpClient();
                await tcpClient.ConnectAsync(remoteNode.IpAddress, remoteNode.TcpPort);

                using var stream = tcpClient.GetStream();
                using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                // Відправити команду
                var commandJson = JsonSerializer.Serialize(networkCommand);
                await writer.WriteLineAsync(commandJson);

                Console.WriteLine($"[Service] Очікування відповіді...");

                // Отримати відповідь
                var responseJson = await reader.ReadLineAsync();
                var networkResponse = JsonSerializer.Deserialize<NetworkCommandResponse>(responseJson);

                if (networkResponse == null || !networkResponse.Success)
                {
                    Console.WriteLine($"[Service] ❌ Помилка отримання директорій: {networkResponse?.ErrorMessage}");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = networkResponse?.ErrorMessage ?? "Не вдалося отримати відповідь"
                    };
                }

                // Десеріалізувати список директорій
                var directories = JsonSerializer.Deserialize<List<DirectoryWithAccessModel>>(networkResponse.Data);
                Console.WriteLine($"[Service] ✅ Отримано {directories?.Count ?? 0} директорій з віддаленого пристрою");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(directories ?? new List<DirectoryWithAccessModel>())
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка HandleGetRemoteDirectories: {ex.Message}");
                Log($"Error getting remote directories: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }
        /// <summary>
        /// Отримати список файлів/папок у директорії (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleGetDirectoryFileList(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteDirectoryFileListRequest>(data);
                Console.WriteLine($"[Service] GetDirectoryFileList: DeviceName={request.DeviceName}, Path={request.DirectoryPath}");

                // Перевірка: віддалений чи локальний запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    // Віддалений запит - відправити до іншого пристрою
                    return await HandleRemoteDirectoryFileList(request);
                }

                // Локальний запит - виконати на цьому пристрої
                if (!Directory.Exists(request.DirectoryPath))
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = $"Директорія не існує: {request.DirectoryPath}"
                    };
                }

                var items = new List<FileSystemItemModel>();

                // Додаємо директорії
                foreach (var dir in Directory.GetDirectories(request.DirectoryPath))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        items.Add(new FileSystemItemModel
                        {
                            Name = dirInfo.Name,
                            FullPath = dirInfo.FullName,
                            IsDirectory = true,
                            Size = 0,
                            ModifiedDate = dirInfo.LastWriteTime,
                            CreatedDate = dirInfo.CreationTime,
                            Extension = string.Empty
                        });
                    }
                    catch { }
                }

                // Додаємо файли
                foreach (var file in Directory.GetFiles(request.DirectoryPath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        items.Add(new FileSystemItemModel
                        {
                            Name = fileInfo.Name,
                            FullPath = fileInfo.FullName,
                            IsDirectory = false,
                            Size = fileInfo.Length,
                            ModifiedDate = fileInfo.LastWriteTime,
                            CreatedDate = fileInfo.CreationTime,
                            Extension = fileInfo.Extension
                        });
                    }
                    catch { }
                }

                Console.WriteLine($"[Service] ✅ Повернуто {items.Count} елементів (локально)");
                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(items)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка GetDirectoryFileList: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Виконати GetDirectoryFileList на віддаленому пристрої
        /// </summary>
        private async Task<ServiceResponse> HandleRemoteDirectoryFileList(RemoteDirectoryFileListRequest request)
        {
            try
            {
                Console.WriteLine($"[Service] 🌐 Віддалений запит GetDirectoryFileList: {request.DeviceName} -> {request.DirectoryPath}");

                // Знайти віддалений вузол
                RemoteNode remoteNode = null;
                lock (_nodesLock)
                {
                    if (_activeNetworkNodes.TryGetValue(request.DeviceName, out var nodeInfo))
                    {
                        remoteNode = nodeInfo.Node;
                        Console.WriteLine($"[Service] Знайдено вузол: IP={remoteNode.IpAddress}, Port={remoteNode.TcpPort}");
                    }
                }

                if (remoteNode == null)
                {
                    Console.WriteLine($"[Service] ❌ Пристрій '{request.DeviceName}' не знайдено в активних вузлах");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = $"Пристрій '{request.DeviceName}' не в мережі або не знайдено"
                    };
                }

                // Створити NetworkCore команду
                var networkRequest = new DocControlNetworkCore.Models.RemoteFileListRequest
                {
                    DirectoryPath = request.DirectoryPath
                };

                var networkCommand = new NetworkCommand
                {
                    Type = NetworkCommandType.GetDirectoryFileList,
                    SenderId = Guid.Empty,
                    Payload = JsonSerializer.Serialize(networkRequest)
                };

                Console.WriteLine($"[Service] Відправка GetDirectoryFileList до {remoteNode.IpAddress}:{remoteNode.TcpPort}");

                // Відправити команду через TCP
                using var tcpClient = new System.Net.Sockets.TcpClient();
                await tcpClient.ConnectAsync(remoteNode.IpAddress, remoteNode.TcpPort);

                using var stream = tcpClient.GetStream();
                using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                // Відправити команду
                var commandJson = JsonSerializer.Serialize(networkCommand);
                await writer.WriteLineAsync(commandJson);

                Console.WriteLine($"[Service] Очікування відповіді...");

                // Отримати відповідь
                var responseJson = await reader.ReadLineAsync();
                var networkResponse = JsonSerializer.Deserialize<NetworkCommandResponse>(responseJson);

                if (networkResponse == null || !networkResponse.Success)
                {
                    Console.WriteLine($"[Service] ❌ Помилка отримання файлів: {networkResponse?.ErrorMessage}");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = networkResponse?.ErrorMessage ?? "Не вдалося отримати відповідь"
                    };
                }

                // Десеріалізувати список файлів
                var items = JsonSerializer.Deserialize<List<FileSystemItemModel>>(networkResponse.Data);
                Console.WriteLine($"[Service] ✅ Отримано {items?.Count ?? 0} елементів з віддаленого пристрою");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(items ?? new List<FileSystemItemModel>())
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка HandleRemoteDirectoryFileList: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Універсальний метод для пересилання команд на віддалені пристрої
        /// </summary>
        private async Task<ServiceResponse> ForwardRemoteCommand<TRequest>(string deviceName, NetworkCommandType commandType, TRequest requestPayload)
        {
            try
            {
                Console.WriteLine($"[Service] 🌐 Віддалений запит {commandType}: {deviceName}");

                // Знайти віддалений вузол
                RemoteNode remoteNode = null;
                lock (_nodesLock)
                {
                    if (_activeNetworkNodes.TryGetValue(deviceName, out var nodeInfo))
                    {
                        remoteNode = nodeInfo.Node;
                        Console.WriteLine($"[Service] Знайдено вузол: IP={remoteNode.IpAddress}, Port={remoteNode.TcpPort}");
                    }
                }

                if (remoteNode == null)
                {
                    Console.WriteLine($"[Service] ❌ Пристрій '{deviceName}' не знайдено в активних вузлах");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = $"Пристрій '{deviceName}' не в мережі або не знайдено"
                    };
                }

                // Створити NetworkCore команду
                var networkCommand = new NetworkCommand
                {
                    Type = commandType,
                    SenderId = Guid.Empty,
                    Payload = JsonSerializer.Serialize(requestPayload)
                };

                Console.WriteLine($"[Service] Відправка {commandType} до {remoteNode.IpAddress}:{remoteNode.TcpPort}");

                // Відправити команду через TCP
                using var tcpClient = new System.Net.Sockets.TcpClient();
                await tcpClient.ConnectAsync(remoteNode.IpAddress, remoteNode.TcpPort);

                using var stream = tcpClient.GetStream();
                using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);

                // Відправити команду
                var commandJson = JsonSerializer.Serialize(networkCommand);
                await writer.WriteLineAsync(commandJson);

                Console.WriteLine($"[Service] Очікування відповіді...");

                // Отримати відповідь
                var responseJson = await reader.ReadLineAsync();
                var networkResponse = JsonSerializer.Deserialize<NetworkCommandResponse>(responseJson);

                if (networkResponse == null || !networkResponse.Success)
                {
                    Console.WriteLine($"[Service] ❌ Помилка віддаленої операції: {networkResponse?.ErrorMessage}");
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = networkResponse?.ErrorMessage ?? "Не вдалося отримати відповідь"
                    };
                }

                Console.WriteLine($"[Service] ✅ Віддалена операція виконана успішно");
                return new ServiceResponse
                {
                    Success = true,
                    Data = networkResponse.Data,
                    Message = "Операція виконана успішно"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка ForwardRemoteCommand: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Створити папку (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleCreateFolder(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteCreateFolderRequest>(data);
                Console.WriteLine($"[Service] CreateFolder: DeviceName={request.DeviceName}, Path={request.ParentPath}/{request.FolderName}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.CreateFolder,
                        new DocControlNetworkCore.Models.RemoteCreateFolderRequest
                        {
                            ParentPath = request.ParentPath,
                            FolderName = request.FolderName
                        });
                }

                // Локальний запит
                string newFolderPath = Path.Combine(request.ParentPath, request.FolderName);

                if (Directory.Exists(newFolderPath))
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Папка з такою назвою вже існує"
                    };
                }

                Directory.CreateDirectory(newFolderPath);

                return new ServiceResponse
                {
                    Success = true,
                    Message = $"Папку '{request.FolderName}' успішно створено"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка CreateFolder: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Створити файл (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleCreateFile(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteCreateFileRequest>(data);
                Console.WriteLine($"[Service] CreateFile: DeviceName={request.DeviceName}, Path={request.ParentPath}/{request.FileName}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.CreateFile,
                        new DocControlNetworkCore.Models.RemoteCreateFileRequest
                        {
                            ParentPath = request.ParentPath,
                            FileName = request.FileName
                        });
                }

                // Локальний запит
                string newFilePath = Path.Combine(request.ParentPath, request.FileName);

                if (File.Exists(newFilePath))
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Файл з такою назвою вже існує"
                    };
                }

                File.Create(newFilePath).Dispose();

                return new ServiceResponse
                {
                    Success = true,
                    Message = $"Файл '{request.FileName}' успішно створено"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка CreateFile: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Перейменувати файл/папку (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleRenameFileOrFolder(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteRenameRequest>(data);
                Console.WriteLine($"[Service] Rename: DeviceName={request.DeviceName}, {request.OldPath} → {request.NewName}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.RenameFileOrFolder,
                        new DocControlNetworkCore.Models.RemoteRenameRequest
                        {
                            OldPath = request.OldPath,
                            NewName = request.NewName
                        });
                }

                // Локальний запит
                string parentDir = Path.GetDirectoryName(request.OldPath);
                string newPath = Path.Combine(parentDir, request.NewName);

                bool isDirectory = Directory.Exists(request.OldPath);

                if (isDirectory)
                {
                    if (Directory.Exists(newPath))
                    {
                        return new ServiceResponse
                        {
                            Success = false,
                            Message = "Папка з такою назвою вже існує"
                        };
                    }
                    Directory.Move(request.OldPath, newPath);
                }
                else
                {
                    if (File.Exists(newPath))
                    {
                        return new ServiceResponse
                        {
                            Success = false,
                            Message = "Файл з такою назвою вже існує"
                        };
                    }
                    File.Move(request.OldPath, newPath);
                }

                return new ServiceResponse
                {
                    Success = true,
                    Message = $"Успішно перейменовано на '{request.NewName}'"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка Rename: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Видалити файл/папку (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleDeleteFileOrFolder(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteDeleteRequest>(data);
                Console.WriteLine($"[Service] Delete: DeviceName={request.DeviceName}, Path={request.Path} (IsDirectory={request.IsDirectory}, Recursive={request.Recursive})");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.DeleteFileOrFolder,
                        new DocControlNetworkCore.Models.RemoteDeleteRequest
                        {
                            Path = request.Path,
                            IsDirectory = request.IsDirectory,
                            Recursive = request.Recursive
                        });
                }

                // Локальний запит
                if (request.IsDirectory)
                {
                    Directory.Delete(request.Path, request.Recursive);
                }
                else
                {
                    File.Delete(request.Path);
                }

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Успішно видалено"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка Delete: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Читання вмісту файлу (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleReadFileContent(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteReadFileRequest>(data);
                Console.WriteLine($"[Service] ReadFileContent: DeviceName={request.DeviceName}, Path={request.FilePath}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.ReadFileContent,
                        new DocControlNetworkCore.Models.RemoteReadFileRequest
                        {
                            FilePath = request.FilePath
                        });
                }

                // Локальний запит
                if (!File.Exists(request.FilePath))
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = $"Файл не існує: {request.FilePath}"
                    };
                }

                string content = await File.ReadAllTextAsync(request.FilePath);

                return new ServiceResponse
                {
                    Success = true,
                    Data = content
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка ReadFileContent: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Запис вмісту файлу (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleWriteFileContent(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteWriteFileRequest>(data);
                Console.WriteLine($"[Service] WriteFileContent: DeviceName={request.DeviceName}, Path={request.FilePath}, Content length={request.Content.Length}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.WriteFileContent,
                        new DocControlNetworkCore.Models.RemoteWriteFileRequest
                        {
                            FilePath = request.FilePath,
                            Content = request.Content
                        });
                }

                // Локальний запит
                await File.WriteAllTextAsync(request.FilePath, request.Content);

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Файл успішно збережено"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка WriteFileContent: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Читання бінарного файлу (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleReadFileBinary(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteReadFileBinaryRequest>(data);
                Console.WriteLine($"[Service] ReadFileBinary: DeviceName={request.DeviceName}, Path={request.FilePath}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.ReadFileBinary,
                        new DocControlNetworkCore.Models.RemoteReadFileBinaryRequest
                        {
                            FilePath = request.FilePath
                        });
                }

                // Локальний запит
                if (!File.Exists(request.FilePath))
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = $"Файл не існує: {request.FilePath}"
                    };
                }

                byte[] content = await File.ReadAllBytesAsync(request.FilePath);
                string base64Content = Convert.ToBase64String(content);

                return new ServiceResponse
                {
                    Success = true,
                    Data = base64Content
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка ReadFileBinary: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Запис бінарного файлу (підтримує як локальні, так і віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleWriteFileBinary(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteWriteFileBinaryRequest>(data);
                Console.WriteLine($"[Service] WriteFileBinary: DeviceName={request.DeviceName}, Path={request.FilePath}, Content length={request.Content.Length}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.WriteFileBinary,
                        new DocControlNetworkCore.Models.RemoteWriteFileBinaryRequest
                        {
                            FilePath = request.FilePath,
                            Content = request.Content
                        });
                }

                // Локальний запит - ЗБЕРЕЖЕННЯ НА ДИСК СЕРВЕРА (КОМП'ЮТЕР А)
                var fileInfo = new FileInfo(request.FilePath);
                var directory = fileInfo.Directory?.FullName ?? "?";

                Console.WriteLine($"[Service] 💾 Збереження файлу на ЛОКАЛЬНИЙ диск сервера:");
                Console.WriteLine($"[Service]    Шлях: {request.FilePath}");
                Console.WriteLine($"[Service]    Розмір: {request.Content.Length:N0} байт");
                Console.WriteLine($"[Service]    Директорія: {directory}");

                await File.WriteAllBytesAsync(request.FilePath, request.Content);

                Console.WriteLine($"[Service] ✅ Файл успішно збережено на сервері: {fileInfo.Name} ({request.Content.Length} байт)");

                return new ServiceResponse
                {
                    Success = true,
                    Message = $"Файл успішно збережено на сервері ({request.Content.Length:N0} байт)"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка WriteFileBinary: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Отримати шляхдо спільної директорії
        /// </summary>
        private string GetSharedDirectory()
        {
            // Спробуємо отримати з налаштувань
            try
            {
                var setting = _settingsRepo.GetSetting("NetworkCore_SharedDirectory");
                if (!string.IsNullOrEmpty(setting))
                {
                    return setting;
                }
            }
            catch { }

            // За замовчуванням - перша зареєстрована директорія або C:\SharedFiles
            var dirs = _dirRepo.GetAllDirectories();
            if (dirs.Count > 0)
            {
                return dirs[0].Browse;
            }

            // Fallback
            var defaultPath = @"C:\SharedFiles";
            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }

            return defaultPath;
        }

        #endregion

        #region Logging

        private void Log(string message, EventLogEntryType type = EventLogEntryType.Information)
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            if (_debugMode)
            {
                Console.WriteLine(logMessage);
            }
            else
            {
                try
                {
                    EventLog.WriteEntry(ServiceName, logMessage, type);
                }
                catch
                {
                    // Ігноруємо помилки логування
                }
            }
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        #endregion

        #region Service Infrastructure

        private void InitializeComponent()
        {
            this.ServiceName = "DocControlService";
        }

        /// <summary>
        /// Метод для запуску в Debug режимі через консоль
        /// </summary>
        public void StartDebug(string[] args)
        {
            Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
            Console.WriteLine("║   DocControl Service - DEBUG MODE                     ║");
            Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
            Console.WriteLine();

            OnStart(args);

            Console.WriteLine();
            Console.WriteLine("Service is running. Press 'Q' to stop, 'S' for status...");
            Console.WriteLine();

            while (true)
            {
                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Q)
                {
                    Console.WriteLine("Stopping service...");
                    break;
                }
                else if (key.Key == ConsoleKey.S)
                {
                    ShowStatus();
                }
                else if (key.Key == ConsoleKey.C)
                {
                    Console.WriteLine("Forcing commit...");
                    PerformCommitForAllDirectories();
                }
                else if (key.Key == ConsoleKey.H)
                {
                    ShowHelp();
                }
            }

            OnStop();
            Console.WriteLine("Service stopped. Press any key to exit...");
            Console.ReadKey();
        }

        private void ShowStatus()
        {
            Console.WriteLine();
            Console.WriteLine("═══ SERVICE STATUS ═══");
            Console.WriteLine($"Running since: {_startTime:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Uptime: {DateTime.Now - _startTime:hh\\:mm\\:ss}");
            Console.WriteLine($"Total directories: {_dirRepo.GetAllDirectories().Count}");
            Console.WriteLine($"Shared directories: {_dirRepo.GetAllDirectories().Count(d => _accessRepo.IsDirectoryShared(d.Id))}");
            Console.WriteLine($"Registered devices: {_deviceRepo.GetAllDevices().Count}");
            Console.WriteLine($"Last commit: {_lastCommitTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
            Console.WriteLine($"Commit interval: {_commitIntervalMinutes} minutes");
            Console.WriteLine("══════════════════════");
            Console.WriteLine();
        }

        private void ShowHelp()
        {
            Console.WriteLine();
            Console.WriteLine("═══ AVAILABLE COMMANDS ═══");
            Console.WriteLine("Q - Quit (stop service)");
            Console.WriteLine("S - Show status");
            Console.WriteLine("C - Force commit now");
            Console.WriteLine("H - Show this help");
            Console.WriteLine("═══════════════════════════");
            Console.WriteLine();
        }

        #endregion

        #region New Command Handlers

        private ServiceResponse HandleGetCommitLog(string data)
        {
            try
            {
                List<CommitStatusLog> logs;
                if (data.Contains(","))
                {
                    var parts = data.Split(',');
                    int dirId = int.Parse(parts[0]);
                    int limit = int.Parse(parts[1]);
                    logs = _commitLogRepo.GetLogsByDirectory(dirId, limit);
                }
                else
                {
                    int limit = int.Parse(data);
                    logs = _commitLogRepo.GetRecentLogs(limit);
                }

                var models = logs.Select(l => new CommitLogModel
                {
                    Id = l.Id,
                    DirectoryId = l.DirectoryId,
                    DirectoryPath = l.DirectoryPath,
                    Status = l.Status,
                    Message = l.Message,
                    Timestamp = l.Timestamp
                }).ToList();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(models)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetCommitLog: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGitHistory(string data)
        {
            try
            {
                int dirId;
                int maxCount = 50;

                // Спробуємо спочатку як RemoteGitHistoryRequest (для remote операцій)
                try
                {
                    var remoteRequest = JsonSerializer.Deserialize<RemoteGitHistoryRequest>(data);
                    if (!string.IsNullOrEmpty(remoteRequest.DeviceName))
                    {
                        // Це remote запит
                        dirId = remoteRequest.DirectoryId;
                        maxCount = remoteRequest.MaxCount > 0 ? remoteRequest.MaxCount : 50;
                    }
                    else
                    {
                        // Не remote, спробуємо як простий int
                        dirId = int.Parse(data);
                    }
                }
                catch
                {
                    // Якщо не RemoteGitHistoryRequest, то просто int
                    dirId = int.Parse(data);
                }

                var vcs = _versionFactory.GetServiceFor(dirId);

                if (vcs == null)
                    return new ServiceResponse { Success = false, Message = "Git репозиторій не знайдено" };

                var history = vcs.GetCommitHistory(maxCount);
                var models = history.Select(h => new GitCommitHistoryModel
                {
                    Hash = h.Hash,
                    Message = h.Message,
                    Author = h.Author,
                    Date = h.Date
                }).ToList();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(models)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGitHistory: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleRevertToCommit(string data)
        {
            try
            {
                // Спробуємо спочатку як RemoteGitRevertRequest (для remote операцій)
                try
                {
                    var remoteRequest = JsonSerializer.Deserialize<RemoteGitRevertRequest>(data);
                    if (!string.IsNullOrEmpty(remoteRequest.DeviceName))
                    {
                        // Це remote запит
                        var vcs = _versionFactory.GetServiceFor(remoteRequest.DirectoryId);
                        if (vcs == null)
                            return new ServiceResponse { Success = false, Message = "Git репозиторій не знайдено" };

                        bool success = vcs.RevertToCommit(remoteRequest.CommitHash);
                        Log($"Remote revert executed for directory {remoteRequest.DirectoryId} to commit {remoteRequest.CommitHash}");

                        return new ServiceResponse
                        {
                            Success = success,
                            Message = success ? "Відкат виконано" : "Помилка відкату"
                        };
                    }
                }
                catch { }

                // Якщо не RemoteGitRevertRequest, то звичайний RevertRequest
                var request = JsonSerializer.Deserialize<RevertRequest>(data);
                var vcs2 = _versionFactory.GetServiceFor(request.DirectoryId);

                if (vcs2 == null)
                    return new ServiceResponse { Success = false, Message = "Git репозиторій не знайдено" };

                bool result = vcs2.RevertToCommit(request.CommitHash);
                return new ServiceResponse
                {
                    Success = result,
                    Message = result ? "Відкат виконано" : "Помилка відкату"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleRevertToCommit: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetErrorLog(string data)
        {
            try
            {
                bool onlyUnresolved = bool.Parse(data);
                var errors = _errorLogRepo.GetRecentErrors(100, onlyUnresolved);

                var models = errors.Select(e => new ErrorLogModel
                {
                    Id = e.Id,
                    ErrorType = e.ErrorType,
                    ErrorMessage = e.ErrorMessage,
                    UserFriendlyMessage = e.UserFriendlyMessage,
                    StackTrace = e.StackTrace,
                    Timestamp = e.Timestamp,
                    IsResolved = e.IsResolved
                }).ToList();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(models)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetErrorLog: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleMarkErrorResolved(string data)
        {
            try
            {
                int errorId = int.Parse(data);
                _errorLogRepo.MarkAsResolved(errorId);

                return new ServiceResponse { Success = true, Message = "Помилку позначено як вирішену" };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleMarkErrorResolved: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleClearResolvedErrors()
        {
            try
            {
                _errorLogRepo.ClearResolvedErrors();
                return new ServiceResponse { Success = true, Message = "Вирішені помилки очищено" };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleClearResolvedErrors: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetUnresolvedErrorCount()
        {
            try
            {
                int count = _errorLogRepo.GetUnresolvedCount();
                return new ServiceResponse { Success = true, Data = count.ToString() };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetUnresolvedErrorCount: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    AutoShareOnAdd = _settingsRepo.GetBoolSetting("AutoShareOnAdd", false),
                    EnableUpdateNotifications = _settingsRepo.GetBoolSetting("EnableUpdateNotifications", true),
                    CommitIntervalMinutes = _settingsRepo.GetIntSetting("CommitIntervalMinutes", 720)
                };

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(settings)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetSettings: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleSaveSettings(string data)
        {
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(data);

                _settingsRepo.SetBoolSetting("AutoShareOnAdd", settings.AutoShareOnAdd);
                _settingsRepo.SetBoolSetting("EnableUpdateNotifications", settings.EnableUpdateNotifications);
                _settingsRepo.SetIntSetting("CommitIntervalMinutes", settings.CommitIntervalMinutes);

                // Оновлюємо інтервал комітів
                _commitIntervalMinutes = settings.CommitIntervalMinutes;

                return new ServiceResponse { Success = true, Message = "Налаштування збережено" };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleSaveSettings: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Roadmap Handlers

        private ServiceResponse HandleCreateRoadmap(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                int directoryId = Convert.ToInt32(request["DirectoryId"].ToString());
                string name = request["Name"].ToString();
                string description = request["Description"].ToString();

                var eventsJson = request["Events"].ToString();
                var events = JsonSerializer.Deserialize<List<RoadmapEvent>>(eventsJson);

                int roadmapId = _roadmapRepo.CreateRoadmap(directoryId, name, description, events);

                Log($"Створено дорожню карту: {name} (ID: {roadmapId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = roadmapId.ToString(),
                    Message = "Roadmap створено успішно"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCreateRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetRoadmaps()
        {
            try
            {
                var roadmaps = _roadmapRepo.GetAllRoadmaps();
                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmaps)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetRoadmaps: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetRoadmapById(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var roadmap = _roadmapRepo.GetRoadmapById(roadmapId);

                if (roadmap == null)
                    return new ServiceResponse { Success = false, Message = "Roadmap не знайдено" };

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmap)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetRoadmapById: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteRoadmap(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                bool deleted = _roadmapRepo.DeleteRoadmap(roadmapId);

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Roadmap видалено" : "Roadmap не знайдено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleAnalyzeDirectoryForRoadmap(string data)
        {
            try
            {
                int directoryId = int.Parse(data);
                var dir = _dirRepo.GetById(directoryId);

                if (dir == null)
                    return new ServiceResponse { Success = false, Message = "Директорію не знайдено" };

                Log($"Аналіз директорії для roadmap: {dir.Browse}");
                var events = _roadmapService.AnalyzeDirectory(dir.Browse);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(events),
                    Message = $"Знайдено {events.Count} подій"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAnalyzeDirectoryForRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleExportRoadmapAsJson(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var roadmap = _roadmapRepo.GetRoadmapById(roadmapId);

                if (roadmap == null)
                    return new ServiceResponse { Success = false, Message = "Roadmap не знайдено" };

                string json = _roadmapService.ExportToJson(roadmap);

                return new ServiceResponse
                {
                    Success = true,
                    Data = json,
                    Message = "Експорт успішний"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleExportRoadmapAsJson: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Network Discovery Handlers

        private ServiceResponse HandleScanNetwork()
        {
            try
            {
                Log("Запуск сканування мережі...");
                var devices = _networkService.ScanNetworkAsync().Result;

                Log($"Знайдено {devices.Count} пристроїв у мережі");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(devices),
                    Message = $"Знайдено {devices.Count} пристроїв"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleScanNetwork: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetNetworkInterfaces()
        {
            try
            {
                var interfaces = _networkService.GetNetworkInterfaces();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(interfaces)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetNetworkInterfaces: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region External Services Handlers

        private ServiceResponse HandleGetExternalServices()
        {
            try
            {
                var services = _externalServiceRepo.GetAllServices();
                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(services)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetExternalServices: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleAddExternalService(string data)
        {
            try
            {
                var service = JsonSerializer.Deserialize<ExternalService>(data);

                int serviceId = _externalServiceRepo.AddService(
                    service.Name,
                    service.ServiceType,
                    service.Url,
                    service.ApiKey,
                    service.IsActive);

                Log($"Додано зовнішній сервіс: {service.Name}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = serviceId.ToString(),
                    Message = "Сервіс додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateExternalService(string data)
        {
            try
            {
                var service = JsonSerializer.Deserialize<ExternalService>(data);

                bool updated = _externalServiceRepo.UpdateService(
                    service.Id,
                    service.Name,
                    service.ServiceType,
                    service.Url,
                    service.ApiKey,
                    service.IsActive);

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Сервіс оновлено" : "Сервіс не знайдено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteExternalService(string data)
        {
            try
            {
                int serviceId = int.Parse(data);
                bool deleted = _externalServiceRepo.DeleteService(serviceId);

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Сервіс видалено" : "Сервіс не знайдено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleTestExternalService(string data)
        {
            try
            {
                int serviceId = int.Parse(data);
                var service = _externalServiceRepo.GetServiceById(serviceId);

                if (service == null)
                    return new ServiceResponse { Success = false, Message = "Сервіс не знайдено" };

                // Простий тест доступності URL
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var response = client.GetAsync(service.Url).Result;
                bool success = response.IsSuccessStatusCode;

                if (success)
                {
                    _externalServiceRepo.UpdateLastUsed(serviceId);
                }

                return new ServiceResponse
                {
                    Success = success,
                    Message = success ? $"Сервіс доступний (HTTP {(int)response.StatusCode})" : $"Помилка: HTTP {(int)response.StatusCode}"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleTestExternalService: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = $"Помилка з'єднання: {ex.Message}" };
            }
        }

        #endregion

        #region Geo Roadmap Handlers (v0.3)

        private ServiceResponse HandleCreateGeoRoadmap(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<CreateGeoRoadmapRequest>(data);
                int roadmapId = _geoRoadmapRepo.CreateGeoRoadmap(request, "System");

                Log($"Створено геокарту: {request.Name} (ID: {roadmapId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = roadmapId.ToString(),
                    Message = "Геокарту створено успішно"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCreateGeoRoadmap: {ex.Message}", EventLogEntryType.Error);
                _errorLogRepo.LogError("CreateGeoRoadmap", ex.Message,
                    "Не вдалося створити геокарту", ex.StackTrace);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGeoRoadmaps()
        {
            try
            {
                var roadmaps = _geoRoadmapRepo.GetAllGeoRoadmaps();
                Log($"Завантажено {roadmaps.Count} геокарт");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmaps)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoRoadmaps: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGeoRoadmapById(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var roadmap = _geoRoadmapRepo.GetGeoRoadmapById(roadmapId);

                if (roadmap == null)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Геокарту не знайдено"
                    };
                }

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(roadmap)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoRoadmapById: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoRoadmap(string data)
        {
            try
            {
                var roadmap = JsonSerializer.Deserialize<GeoRoadmap>(data);
                bool updated = _geoRoadmapRepo.UpdateGeoRoadmap(roadmap);

                if (updated)
                    Log($"Оновлено геокарту: {roadmap.Name} (ID: {roadmap.Id})");

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Геокарту оновлено" : "Не вдалося оновити геокарту"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoRoadmap(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteGeoRoadmap(roadmapId);

                if (deleted)
                    Log($"Видалено геокарту ID: {roadmapId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Геокарту видалено" : "Не вдалося видалити геокарту"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geo Node Handlers

        private ServiceResponse HandleAddGeoNode(string data)
        {
            try
            {
                var node = JsonSerializer.Deserialize<GeoRoadmapNode>(data);
                int nodeId = _geoRoadmapRepo.AddNode(node);

                Log($"Додано вузол: {node.Title} (ID: {nodeId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = nodeId.ToString(),
                    Message = "Вузол додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddGeoNode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoNode(string data)
        {
            try
            {
                var node = JsonSerializer.Deserialize<GeoRoadmapNode>(data);
                bool updated = _geoRoadmapRepo.UpdateNode(node);

                if (updated)
                    Log($"Оновлено вузол: {node.Title} (ID: {node.Id})");

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Вузол оновлено" : "Не вдалося оновити вузол"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoNode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoNode(string data)
        {
            try
            {
                int nodeId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteNode(nodeId);

                if (deleted)
                    Log($"Видалено вузол ID: {nodeId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Вузол видалено" : "Не вдалося видалити вузол"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoNode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleGetGeoNodesByRoadmap(string data)
        {
            try
            {
                int roadmapId = int.Parse(data);
                var nodes = _geoRoadmapRepo.GetNodesByRoadmap(roadmapId);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(nodes)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoNodesByRoadmap: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geo Route Handlers

        private ServiceResponse HandleAddGeoRoute(string data)
        {
            try
            {
                var route = JsonSerializer.Deserialize<GeoRoadmapRoute>(data);
                int routeId = _geoRoadmapRepo.AddRoute(route);

                Log($"Додано маршрут ID: {routeId} (від {route.FromNodeId} до {route.ToNodeId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = routeId.ToString(),
                    Message = "Маршрут додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddGeoRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoRoute(string data)
        {
            try
            {
                var route = JsonSerializer.Deserialize<GeoRoadmapRoute>(data);
                // Додати метод UpdateRoute в GeoRoadmapRepository якщо потрібно

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Маршрут оновлено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoRoute(string data)
        {
            try
            {
                int routeId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteRoute(routeId);

                if (deleted)
                    Log($"Видалено маршрут ID: {routeId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Маршрут видалено" : "Не вдалося видалити маршрут"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geo Area Handlers

        private ServiceResponse HandleAddGeoArea(string data)
        {
            try
            {
                var area = JsonSerializer.Deserialize<GeoRoadmapArea>(data);
                int areaId = _geoRoadmapRepo.AddArea(area);

                Log($"Додано область: {area.Name} (ID: {areaId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = areaId.ToString(),
                    Message = "Область додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddGeoArea: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateGeoArea(string data)
        {
            try
            {
                var area = JsonSerializer.Deserialize<GeoRoadmapArea>(data);
                // Додати метод UpdateArea в GeoRoadmapRepository якщо потрібно

                return new ServiceResponse
                {
                    Success = true,
                    Message = "Область оновлено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateGeoArea: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteGeoArea(string data)
        {
            try
            {
                int areaId = int.Parse(data);
                bool deleted = _geoRoadmapRepo.DeleteArea(areaId);

                if (deleted)
                    Log($"Видалено область ID: {areaId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Область видалено" : "Не вдалося видалити область"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteGeoArea: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Template Handlers

        private ServiceResponse HandleGetGeoRoadmapTemplates()
        {
            try
            {
                var templates = _geoRoadmapRepo.GetAllTemplates();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(templates)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetGeoRoadmapTemplates: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleCreateFromTemplate(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                int templateId = Convert.ToInt32(request["TemplateId"].ToString());
                int directoryId = Convert.ToInt32(request["DirectoryId"].ToString());
                string name = request["Name"].ToString();

                var template = _geoRoadmapRepo.GetAllTemplates()
                    .FirstOrDefault(t => t.Id == templateId);

                if (template == null)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Шаблон не знайдено"
                    };
                }

                // Створюємо нову геокарту на основі шаблону
                var templateData = JsonSerializer.Deserialize<Dictionary<string, object>>(template.TemplateJson);

                var createRequest = new CreateGeoRoadmapRequest
                {
                    DirectoryId = directoryId,
                    Name = name,
                    Description = $"Створено з шаблону: {template.Name}",
                    MapProvider = MapProvider.OpenStreetMap,
                    CenterLatitude = 50.4501,
                    CenterLongitude = 30.5234,
                    ZoomLevel = 10
                };

                int roadmapId = _geoRoadmapRepo.CreateGeoRoadmap(createRequest, "System");

                Log($"Створено геокарту з шаблону {template.Name}: {name} (ID: {roadmapId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = roadmapId.ToString(),
                    Message = "Геокарту створено з шаблону"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCreateFromTemplate: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleSaveAsTemplate(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                int roadmapId = Convert.ToInt32(request["RoadmapId"].ToString());
                string name = request["Name"].ToString();
                string description = request["Description"].ToString();
                string category = request["Category"].ToString();

                var roadmap = _geoRoadmapRepo.GetGeoRoadmapById(roadmapId);

                if (roadmap == null)
                {
                    return new ServiceResponse
                    {
                        Success = false,
                        Message = "Геокарту не знайдено"
                    };
                }

                int templateId = _geoRoadmapRepo.SaveAsTemplate(name, description, category, roadmap);

                Log($"Збережено геокарту як шаблон: {name} (ID: {templateId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = templateId.ToString(),
                    Message = "Шаблон збережено"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleSaveAsTemplate: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region Geocoding Handlers

        private ServiceResponse HandleGeocodeAddress(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<GeocodeRequest>(data);

                Log($"Геокодування адреси: {request.Address}");

                var result = _geoMappingService.GeocodeAddressAsync(request.Address).Result;

                if (result.Success)
                    Log($"Знайдено координати: {result.Latitude}, {result.Longitude}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(result)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGeocodeAddress: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleReverseGeocode(string data)
        {
            try
            {
                var coords = JsonSerializer.Deserialize<Dictionary<string, double>>(data);
                double latitude = coords["Latitude"];
                double longitude = coords["Longitude"];

                Log($"Зворотне геокодування: {latitude}, {longitude}");

                var address = _geoMappingService.ReverseGeocodeAsync(latitude, longitude).Result;

                Log($"Знайдено адресу: {address}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = address
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleReverseGeocode: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleCalculateRoute(string data)
        {
            try
            {
                var nodes = JsonSerializer.Deserialize<List<GeoRoadmapNode>>(data);

                var optimizedRoute = _geoMappingService.CalculateOptimalRoute(nodes);

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(optimizedRoute),
                    Message = "Маршрут оптимізовано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleCalculateRoute: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region IP Filter Handlers

        private ServiceResponse HandleGetIpFilterRules()
        {
            try
            {
                var rules = _ipFilterService.GetAllRules();

                return new ServiceResponse
                {
                    Success = true,
                    Data = JsonSerializer.Serialize(rules)
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleGetIpFilterRules: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleAddIpFilterRule(string data)
        {
            try
            {
                var rule = JsonSerializer.Deserialize<IpFilterRule>(data);
                int ruleId = _ipFilterService.AddRule(rule);

                Log($"Додано правило IP фільтрації: {rule.RuleName} (ID: {ruleId})");

                return new ServiceResponse
                {
                    Success = true,
                    Data = ruleId.ToString(),
                    Message = "Правило додано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleAddIpFilterRule: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleUpdateIpFilterRule(string data)
        {
            try
            {
                var rule = JsonSerializer.Deserialize<IpFilterRule>(data);
                bool updated = _ipFilterService.UpdateRule(rule);

                if (updated)
                    Log($"Оновлено правило IP фільтрації: {rule.RuleName} (ID: {rule.Id})");

                return new ServiceResponse
                {
                    Success = updated,
                    Message = updated ? "Правило оновлено" : "Не вдалося оновити правило"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleUpdateIpFilterRule: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleDeleteIpFilterRule(string data)
        {
            try
            {
                int ruleId = int.Parse(data);
                bool deleted = _ipFilterService.DeleteRule(ruleId);

                if (deleted)
                    Log($"Видалено правило IP фільтрації ID: {ruleId}");

                return new ServiceResponse
                {
                    Success = deleted,
                    Message = deleted ? "Правило видалено" : "Не вдалося видалити правило"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleDeleteIpFilterRule: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        private ServiceResponse HandleTestIpAccess(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
                string ipAddress = request["IpAddress"].ToString();

                int? directoryId = request.ContainsKey("DirectoryId") && request["DirectoryId"] != null
                    ? int.Parse(request["DirectoryId"].ToString())
                    : null;

                int? geoRoadmapId = request.ContainsKey("GeoRoadmapId") && request["GeoRoadmapId"] != null
                    ? int.Parse(request["GeoRoadmapId"].ToString())
                    : null;

                bool allowed = _ipFilterService.CheckAccess(ipAddress, directoryId, geoRoadmapId);

                Log($"Перевірка доступу IP {ipAddress}: {(allowed ? "Дозволено" : "Заблоковано")}");

                return new ServiceResponse
                {
                    Success = true,
                    Data = allowed.ToString(),
                    Message = allowed ? "Доступ дозволено" : "Доступ заблоковано"
                };
            }
            catch (Exception ex)
            {
                Log($"Error in HandleTestIpAccess: {ex.Message}", EventLogEntryType.Error);
                return new ServiceResponse { Success = false, Message = ex.Message };
            }
        }

        #endregion

        #region File Locking Handlers (Multi-user support v0.10)

        /// <summary>
        /// Заблокувати файл для редагування (підтримує віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleLockFile(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteLockFileRequest>(data);
                Console.WriteLine($"[Service] LockFile: DeviceName={request.DeviceName}, Path={request.FilePath}, User={request.UserName}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.LockFile,
                        new DocControlNetworkCore.Models.RemoteLockFileRequest
                        {
                            FilePath = request.FilePath,
                            UserName = request.UserName
                        });
                }

                // Локальний запит - спробувати заблокувати файл
                var deviceName = Environment.MachineName;
                var lockInfo = _fileLockRepo.TryLockFile(request.FilePath, deviceName, request.UserName);

                // Серіалізувати FileLockModel в JSON
                var lockJson = JsonSerializer.Serialize(lockInfo);

                return new ServiceResponse
                {
                    Success = lockInfo.IsOwnedByCurrentDevice,
                    Message = lockInfo.IsOwnedByCurrentDevice
                        ? "Файл успішно заблокований"
                        : $"Файл вже заблокований: {lockInfo.LockDescription}",
                    Data = lockJson
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка LockFile: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Розблокувати файл (підтримує віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleUnlockFile(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteUnlockFileRequest>(data);
                Console.WriteLine($"[Service] UnlockFile: DeviceName={request.DeviceName}, Path={request.FilePath}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.UnlockFile,
                        new DocControlNetworkCore.Models.RemoteUnlockFileRequest
                        {
                            FilePath = request.FilePath
                        });
                }

                // Локальний запит
                var deviceName = Environment.MachineName;
                var success = _fileLockRepo.UnlockFile(request.FilePath, deviceName);

                return new ServiceResponse
                {
                    Success = success,
                    Message = success ? "Файл успішно розблокований" : "Файл не був заблокований цим пристроєм"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка UnlockFile: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Отримати інформацію про блокування файлу (підтримує віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleGetFileLockInfo(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteGetFileLockInfoRequest>(data);
                Console.WriteLine($"[Service] GetFileLockInfo: DeviceName={request.DeviceName}, Path={request.FilePath}");

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.GetFileLockInfo,
                        new DocControlNetworkCore.Models.RemoteGetFileLockInfoRequest
                        {
                            FilePath = request.FilePath
                        });
                }

                // Локальний запит
                var lockInfo = _fileLockRepo.GetFileLock(request.FilePath);

                if (lockInfo == null)
                {
                    return new ServiceResponse
                    {
                        Success = true,
                        Message = "Файл не заблокований",
                        Data = null
                    };
                }

                // Перевірити чи належить поточному пристрою
                var deviceName = Environment.MachineName;
                lockInfo.IsOwnedByCurrentDevice = lockInfo.DeviceName == deviceName;

                var lockJson = JsonSerializer.Serialize(lockInfo);

                return new ServiceResponse
                {
                    Success = true,
                    Data = lockJson
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка GetFileLockInfo: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        /// <summary>
        /// Оновити heartbeat блокування (підтримує віддалені запити)
        /// </summary>
        private async Task<ServiceResponse> HandleUpdateFileLockHeartbeat(string data)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RemoteUpdateFileLockHeartbeatRequest>(data);

                // Віддалений запит?
                if (!string.IsNullOrEmpty(request.DeviceName))
                {
                    return await ForwardRemoteCommand(request.DeviceName, NetworkCommandType.UpdateFileLockHeartbeat,
                        new DocControlNetworkCore.Models.RemoteUpdateFileLockHeartbeatRequest
                        {
                            FilePath = request.FilePath
                        });
                }

                // Локальний запит
                var deviceName = Environment.MachineName;
                var success = _fileLockRepo.UpdateLastModified(request.FilePath, deviceName);

                return new ServiceResponse
                {
                    Success = success,
                    Message = success ? "Heartbeat оновлено" : "Блокування не знайдено"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] ❌ Помилка UpdateFileLockHeartbeat: {ex.Message}");
                return new ServiceResponse
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        #endregion

    }
}