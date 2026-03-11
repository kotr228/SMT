using DocControlNetworkCore.Models;
using DocControlService.Shared;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NetworkCommandType = DocControlNetworkCore.Models.CommandType;

namespace DocControlNetworkCore.Services
{
    /// <summary>
    /// Сервіс для обміну командами між вузлами
    /// </summary>
    public class CommandLayerService : IDisposable
    {
        private readonly PeerIdentity _localIdentity;
        private readonly string _allowedBasePath;
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listenerTask;

        /// <summary>
        /// Callback для перевірки доступу пристрою до шляху
        /// Параметри: (remotePeerName, requestedPath) => hasAccess
        /// </summary>
        public Func<string, string, bool>? CheckAccessCallback { get; set; }

        /// <summary>
        /// Подія отримання команди
        /// </summary>
        public event Action<NetworkCommand, IPEndPoint>? CommandReceived;

        public CommandLayerService(PeerIdentity localIdentity, string allowedBasePath)
        {
            _localIdentity = localIdentity;
            _allowedBasePath = Path.GetFullPath(allowedBasePath);
        }

        /// <summary>
        /// Запустити TCP сервер для прийому команд
        /// </summary>
        public void Start()
        {
            if (_cancellationTokenSource != null)
            {
                Console.WriteLine("[CommandLayer] Сервіс вже запущено");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _tcpListener = new TcpListener(IPAddress.Any, _localIdentity.TcpPort);
            _tcpListener.Start();

            _listenerTask = Task.Run(() => RunListenerAsync(_cancellationTokenSource.Token));

            Console.WriteLine($"[CommandLayer] TCP сервер запущено на порту {_localIdentity.TcpPort}");
        }

        /// <summary>
        /// Зупинити TCP сервер
        /// </summary>
        public void Stop()
        {
            Console.WriteLine("[CommandLayer] Зупинка сервісу...");

            _cancellationTokenSource?.Cancel();
            _tcpListener?.Stop();

            try
            {
                _listenerTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CommandLayer] Помилка при зупинці: {ex.Message}");
            }

            Console.WriteLine("[CommandLayer] Сервіс зупинено");
        }

        /// <summary>
        /// TCP Listener - приймає вхідні з'єднання
        /// </summary>
        private async Task RunListenerAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("[CommandLayer] Listener запущено");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener!.AcceptTcpClientAsync(cancellationToken);
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CommandLayer] Помилка прийому з'єднання: {ex.Message}");
                }
            }

            Console.WriteLine("[CommandLayer] Listener зупинено");
        }

        /// <summary>
        /// Обробка клієнтського з'єднання
        /// </summary>
        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                var endpoint = (IPEndPoint)client.Client.RemoteEndPoint!;
                Console.WriteLine($"[CommandLayer] Нове з'єднання від {endpoint}");

                // Читання команди
                var commandJson = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(commandJson))
                {
                    Console.WriteLine("[CommandLayer] Порожня команда");
                    return;
                }

                var command = JsonSerializer.Deserialize<NetworkCommand>(commandJson);
                if (command == null)
                {
                    Console.WriteLine("[CommandLayer] Неможливо розпарсити команду");
                    return;
                }

                Console.WriteLine($"[CommandLayer] Отримано команду: {command.Type} від {endpoint}");

                // Обробка команди
                var response = await ProcessCommandAsync(command, endpoint);

                // Відправка відповіді
                var responseJson = JsonSerializer.Serialize(response);
                await writer.WriteLineAsync(responseJson);

                Console.WriteLine($"[CommandLayer] Відповідь відправлено: Success={response.Success}");

                // Виклик події
                CommandReceived?.Invoke(command, endpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CommandLayer] Помилка обробки клієнта: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        /// <summary>
        /// Обробка команди
        /// </summary>
        private async Task<CommandResponse> ProcessCommandAsync(NetworkCommand command, IPEndPoint senderEndpoint)
        {
            try
            {
                switch (command.Type)
                {
                    case NetworkCommandType.GetFileList:
                        return await HandleGetFileListAsync(command, senderEndpoint);

                    case NetworkCommandType.GetFileMeta:
                        return await HandleGetFileMetaAsync(command, senderEndpoint);

                    case NetworkCommandType.Ping:
                        return HandlePing(command);

                    case NetworkCommandType.Heartbeat:
                        return HandleHeartbeat(command);

                    case NetworkCommandType.GetSharedDirectories:
                        return await HandleGetSharedDirectoriesAsync(command, senderEndpoint);

                    // Remote операції - проксируємо до локального DocControlService
                    case NetworkCommandType.GetDirectoryStatistics:
                    case NetworkCommandType.GetDirectoryFileList:
                    case NetworkCommandType.CreateFolder:
                    case NetworkCommandType.CreateFile:
                    case NetworkCommandType.RenameFileOrFolder:
                    case NetworkCommandType.DeleteFileOrFolder:
                    case NetworkCommandType.ScanDirectory:
                    case NetworkCommandType.GitCommit:
                    case NetworkCommandType.GitHistory:
                    case NetworkCommandType.GitRevert:
                    case NetworkCommandType.ReadFileContent:
                    case NetworkCommandType.WriteFileContent:
                    case NetworkCommandType.ReadFileBinary:
                    case NetworkCommandType.WriteFileBinary:
                    case NetworkCommandType.LockFile:
                    case NetworkCommandType.UnlockFile:
                    case NetworkCommandType.GetFileLockInfo:
                    case NetworkCommandType.UpdateFileLockHeartbeat:
                        return await ForwardToLocalServiceAsync(command);

                    default:
                        return new CommandResponse
                        {
                            RequestId = command.RequestId,
                            Success = false,
                            ErrorMessage = $"Непідтримувана команда: {command.Type}"
                        };
                }
            }
            catch (Exception ex)
            {
                return new CommandResponse
                {
                    RequestId = command.RequestId,
                    Success = false,
                    ErrorMessage = $"Помилка обробки команди: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Обробка команди GetFileList
        /// </summary>
        private async Task<CommandResponse> HandleGetFileListAsync(NetworkCommand command, IPEndPoint senderEndpoint)
        {
            try
            {
                var request = JsonSerializer.Deserialize<GetFileListRequest>(command.Payload);
                if (request == null)
                {
                    return new CommandResponse
                    {
                        RequestId = command.RequestId,
                        Success = false,
                        ErrorMessage = "Невалідний запит"
                    };
                }

                // Перевірка доступу пристрою (через callback)
                if (CheckAccessCallback != null)
                {
                    string senderIp = senderEndpoint.Address.ToString();
                    bool hasAccess = CheckAccessCallback(senderIp, request.DirectoryPath);

                    if (!hasAccess)
                    {
                        Console.WriteLine($"[CommandLayer] ДОСТУП ЗАБОРОНЕНО: {senderIp} -> {request.DirectoryPath}");
                        return new CommandResponse
                        {
                            RequestId = command.RequestId,
                            Success = false,
                            ErrorMessage = "Доступ заборонено: пристрій не має дозволу на цю директорію"
                        };
                    }

                    Console.WriteLine($"[CommandLayer] Доступ надано: {senderIp} -> {request.DirectoryPath}");
                }

                // Валідація шляху (безпека)
                var fullPath = Path.GetFullPath(Path.Combine(_allowedBasePath, request.DirectoryPath));
                if (!fullPath.StartsWith(_allowedBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    return new CommandResponse
                    {
                        RequestId = command.RequestId,
                        Success = false,
                        ErrorMessage = "Доступ заборонено: шлях поза дозволеною директорією"
                    };
                }

                if (!Directory.Exists(fullPath))
                {
                    return new CommandResponse
                    {
                        RequestId = command.RequestId,
                        Success = false,
                        ErrorMessage = "Директорія не знайдена"
                    };
                }

                // Отримання списку файлів
                var searchOption = request.IncludeSubdirectories
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                var files = Directory.GetFiles(fullPath, request.Filter, searchOption);
                var directories = Directory.GetDirectories(fullPath, "*", searchOption);

                var fileList = files.Select(f =>
                {
                    var fi = new FileInfo(f);
                    return new FileMetadata
                    {
                        FileName = fi.Name,
                        FullPath = f.Replace(_allowedBasePath, "").TrimStart('\\', '/'),
                        Size = fi.Length,
                        CreatedDate = fi.CreationTime,
                        ModifiedDate = fi.LastWriteTime,
                        IsDirectory = false,
                        Extension = fi.Extension
                    };
                }).Concat(directories.Select(d =>
                {
                    var di = new DirectoryInfo(d);
                    return new FileMetadata
                    {
                        FileName = di.Name,
                        FullPath = d.Replace(_allowedBasePath, "").TrimStart('\\', '/'),
                        Size = 0,
                        CreatedDate = di.CreationTime,
                        ModifiedDate = di.LastWriteTime,
                        IsDirectory = true,
                        Extension = ""
                    };
                })).ToList();

                return new CommandResponse
                {
                    RequestId = command.RequestId,
                    Success = true,
                    Data = JsonSerializer.Serialize(fileList)
                };
            }
            catch (Exception ex)
            {
                return new CommandResponse
                {
                    RequestId = command.RequestId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Обробка команди GetFileMeta
        /// </summary>
        private async Task<CommandResponse> HandleGetFileMetaAsync(NetworkCommand command, IPEndPoint senderEndpoint)
        {
            try
            {
                var filePath = command.Payload;

                // Перевірка доступу пристрою (через callback)
                if (CheckAccessCallback != null)
                {
                    string senderIp = senderEndpoint.Address.ToString();
                    bool hasAccess = CheckAccessCallback(senderIp, filePath);

                    if (!hasAccess)
                    {
                        Console.WriteLine($"[CommandLayer] ДОСТУП ЗАБОРОНЕНО: {senderIp} -> {filePath}");
                        return new CommandResponse
                        {
                            RequestId = command.RequestId,
                            Success = false,
                            ErrorMessage = "Доступ заборонено: пристрій не має дозволу на цей файл"
                        };
                    }
                }

                // Валідація шляху
                var fullPath = Path.GetFullPath(Path.Combine(_allowedBasePath, filePath));
                if (!fullPath.StartsWith(_allowedBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    return new CommandResponse
                    {
                        RequestId = command.RequestId,
                        Success = false,
                        ErrorMessage = "Доступ заборонено"
                    };
                }

                if (!File.Exists(fullPath))
                {
                    return new CommandResponse
                    {
                        RequestId = command.RequestId,
                        Success = false,
                        ErrorMessage = "Файл не знайдено"
                    };
                }

                var fi = new FileInfo(fullPath);
                var metadata = new FileMetadata
                {
                    FileName = fi.Name,
                    FullPath = filePath,
                    Size = fi.Length,
                    CreatedDate = fi.CreationTime,
                    ModifiedDate = fi.LastWriteTime,
                    IsDirectory = false,
                    Extension = fi.Extension
                };

                return new CommandResponse
                {
                    RequestId = command.RequestId,
                    Success = true,
                    Data = JsonSerializer.Serialize(metadata)
                };
            }
            catch (Exception ex)
            {
                return new CommandResponse
                {
                    RequestId = command.RequestId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Обробка Ping
        /// </summary>
        private CommandResponse HandlePing(NetworkCommand command)
        {
            return new CommandResponse
            {
                RequestId = command.RequestId,
                Success = true,
                Data = JsonSerializer.Serialize(new { Message = "Pong", Identity = _localIdentity })
            };
        }

        /// <summary>
        /// Обробка Heartbeat
        /// </summary>
        private CommandResponse HandleHeartbeat(NetworkCommand command)
        {
            return new CommandResponse
            {
                RequestId = command.RequestId,
                Success = true,
                Data = JsonSerializer.Serialize(new { Status = "Alive", Timestamp = DateTime.Now })
            };
        }

        /// <summary>
        /// Обробка запиту GetSharedDirectories - повертає список директорій, які цей вузол відкриває для доступу
        /// </summary>
        private async Task<CommandResponse> HandleGetSharedDirectoriesAsync(NetworkCommand command, IPEndPoint senderEndpoint)
        {
            try
            {
                Console.WriteLine($"[CommandLayer] GetSharedDirectories запит від {senderEndpoint}");

                // Запит до DocControlService для отримання списку shared директорій
                var sharedDirectories = await QueryLocalDocControlServiceForSharedDirectoriesAsync();

                if (sharedDirectories == null)
                {
                    return new CommandResponse
                    {
                        RequestId = command.RequestId,
                        Success = false,
                        ErrorMessage = "Не вдалося отримати список shared директорій з локального сервісу"
                    };
                }

                Console.WriteLine($"[CommandLayer] Повертаємо {sharedDirectories.Count} shared директорій");

                return new CommandResponse
                {
                    RequestId = command.RequestId,
                    Success = true,
                    Data = JsonSerializer.Serialize(sharedDirectories)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CommandLayer] Помилка GetSharedDirectories: {ex.Message}");
                return new CommandResponse
                {
                    RequestId = command.RequestId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Запит до локального DocControlService через Named Pipe для отримання shared директорій
        /// </summary>
        private async Task<List<DirectoryWithAccessModel>> QueryLocalDocControlServiceForSharedDirectoriesAsync()
        {
            try
            {
                using (var pipeClient = new NamedPipeClientStream(".", "DocControlServicePipe", PipeDirection.InOut))
                {
                    await pipeClient.ConnectAsync(5000);

                    if (!pipeClient.IsConnected)
                    {
                        Console.WriteLine("[CommandLayer] Не вдалося підключитися до DocControlService");
                        return null;
                    }

                    // Створити команду GetSharedDirectories
                    var command = new ServiceCommand
                    {
                        Type = DocControlService.Shared.CommandType.GetDirectories,
                        Data = ""
                    };

                    // Відправити команду
                    string requestJson = JsonSerializer.Serialize(command);
                    byte[] requestData = Encoding.UTF8.GetBytes(requestJson + "\n");
                    await pipeClient.WriteAsync(requestData, 0, requestData.Length);
                    await pipeClient.FlushAsync();

                    // Отримати відповідь
                    string responseJson;
                    using (var reader = new StreamReader(pipeClient, Encoding.UTF8, false, 1024, true))
                    {
                        responseJson = await reader.ReadLineAsync();
                    }

                    var response = JsonSerializer.Deserialize<ServiceResponse>(responseJson);
                    if (response != null && response.Success)
                    {
                        var directories = JsonSerializer.Deserialize<List<DirectoryWithAccessModel>>(response.Data);

                        // Фільтруємо тільки shared директорії
                        var sharedDirectories = directories?.Where(d => d.IsShared).ToList() ?? new List<DirectoryWithAccessModel>();

                        Console.WriteLine($"[CommandLayer] Отримано {sharedDirectories.Count} shared директорій з DocControlService");
                        return sharedDirectories;
                    }

                    return new List<DirectoryWithAccessModel>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CommandLayer] Помилка запиту до DocControlService: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Універсальний метод для проксирування remote операцій до локального DocControlService
        /// </summary>
        private async Task<CommandResponse> ForwardToLocalServiceAsync(NetworkCommand networkCommand)
        {
            try
            {
                Console.WriteLine($"[CommandLayer] ForwardToLocalService: {networkCommand.Type}");

                // Конвертуємо NetworkCommand в ServiceCommand на основі типу
                var serviceCommand = ConvertNetworkCommandToServiceCommand(networkCommand);
                if (serviceCommand == null)
                {
                    return new CommandResponse
                    {
                        RequestId = networkCommand.RequestId,
                        Success = false,
                        ErrorMessage = $"Не вдалося сконвертувати команду {networkCommand.Type}"
                    };
                }

                // Відправляємо до локального DocControlService
                using (var pipeClient = new NamedPipeClientStream(".", "DocControlServicePipe", PipeDirection.InOut))
                {
                    await pipeClient.ConnectAsync(5000);

                    if (!pipeClient.IsConnected)
                    {
                        return new CommandResponse
                        {
                            RequestId = networkCommand.RequestId,
                            Success = false,
                            ErrorMessage = "Не вдалося підключитися до DocControlService"
                        };
                    }

                    // Відправити команду
                    string requestJson = JsonSerializer.Serialize(serviceCommand);
                    byte[] requestData = Encoding.UTF8.GetBytes(requestJson + "\n");
                    await pipeClient.WriteAsync(requestData, 0, requestData.Length);
                    await pipeClient.FlushAsync();

                    // Отримати відповідь
                    string responseJson;
                    using (var reader = new StreamReader(pipeClient, Encoding.UTF8, false, 1024, true))
                    {
                        responseJson = await reader.ReadLineAsync();
                    }

                    var serviceResponse = JsonSerializer.Deserialize<ServiceResponse>(responseJson);

                    // Конвертуємо ServiceResponse назад в NetworkCommand.CommandResponse
                    return new CommandResponse
                    {
                        RequestId = networkCommand.RequestId,
                        Success = serviceResponse?.Success ?? false,
                        ErrorMessage = serviceResponse?.Message,
                        Data = serviceResponse?.Data
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CommandLayer] Помилка ForwardToLocalService: {ex.Message}");
                return new CommandResponse
                {
                    RequestId = networkCommand.RequestId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Конвертує NetworkCommand в ServiceCommand
        /// </summary>
        private ServiceCommand ConvertNetworkCommandToServiceCommand(NetworkCommand networkCommand)
        {
            // Payload вже містить серіалізовані дані запиту, просто передаємо їх далі
            // ServiceCommand буде мати той самий payload
            return networkCommand.Type switch
            {
                NetworkCommandType.GetDirectoryStatistics => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.GetDirectoryStatistics,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.GetDirectoryFileList => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.GetDirectoryFileList,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.ScanDirectory => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.ScanDirectory,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.GitCommit => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.CommitDirectory,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.GitHistory => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.GetGitHistory,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.GitRevert => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.RevertToCommit,
                    Data = networkCommand.Payload
                },
                // Для file операцій використовуємо file system операції
                NetworkCommandType.CreateFolder => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.CreateFolder,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.CreateFile => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.CreateFile,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.RenameFileOrFolder => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.RenameFileOrFolder,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.DeleteFileOrFolder => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.DeleteFileOrFolder,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.ReadFileContent => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.ReadFileContent,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.WriteFileContent => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.WriteFileContent,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.ReadFileBinary => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.ReadFileBinary,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.WriteFileBinary => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.WriteFileBinary,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.LockFile => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.LockFile,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.UnlockFile => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.UnlockFile,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.GetFileLockInfo => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.GetFileLockInfo,
                    Data = networkCommand.Payload
                },
                NetworkCommandType.UpdateFileLockHeartbeat => new ServiceCommand
                {
                    Type = DocControlService.Shared.CommandType.UpdateFileLockHeartbeat,
                    Data = networkCommand.Payload
                },
                _ => null
            };
        }

        /// <summary>
        /// Відправити команду іншому вузлу
        /// </summary>
        public async Task<CommandResponse?> SendCommandAsync(string ipAddress, int port, NetworkCommand command, int timeoutMs = 5000)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(ipAddress, port).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));

                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                using var reader = new StreamReader(stream, Encoding.UTF8);

                // Відправка команди
                var commandJson = JsonSerializer.Serialize(command);
                await writer.WriteLineAsync(commandJson);

                Console.WriteLine($"[CommandLayer] Команду відправлено до {ipAddress}:{port}");

                // Очікування відповіді
                var responseJson = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
                if (string.IsNullOrEmpty(responseJson))
                {
                    Console.WriteLine("[CommandLayer] Порожня відповідь");
                    return null;
                }

                var response = JsonSerializer.Deserialize<CommandResponse>(responseJson);
                Console.WriteLine($"[CommandLayer] Отримано відповідь: Success={response?.Success}");

                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CommandLayer] Помилка відправки команди: {ex.Message}");
                return null;
            }
        }

        public void Dispose()
        {
            Stop();
            _tcpListener?.Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}
