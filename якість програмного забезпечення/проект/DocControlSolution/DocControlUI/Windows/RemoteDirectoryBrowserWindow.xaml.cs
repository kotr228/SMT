using DocControlService.Client;
using DocControlService.Shared;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DocControlUI.Windows
{
    public partial class RemoteDirectoryBrowserWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private readonly string _deviceName;
        private List<DirectoryWithAccessModel> _remoteDirectories;
        private DirectoryWithAccessModel _selectedDirectory;
        private ObservableCollection<FileSystemItemViewModel> _fileSystemItems;
        private string _currentPath;
        private Dictionary<string, OpenFileTracker> _openFiles = new Dictionary<string, OpenFileTracker>();
        private string _tempDirectory;

        public RemoteDirectoryBrowserWindow(string deviceName)
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            _deviceName = deviceName;
            _fileSystemItems = new ObservableCollection<FileSystemItemViewModel>();

            // Створити тимчасову директорію для завантажених файлів
            _tempDirectory = Path.Combine(Path.GetTempPath(), "DocControl_Remote", _deviceName);
            Directory.CreateDirectory(_tempDirectory);

            DeviceNameText.Text = $"Пристрій: {_deviceName}";
            Title = $"🌐 Віддалені директорії - {_deviceName} (Сервер)";

            Loaded += RemoteDirectoryBrowserWindow_Loaded;
            Closing += RemoteDirectoryBrowserWindow_Closing;
        }

        private void RemoteDirectoryBrowserWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // КРИТИЧНО: Перевірити ВСІ файли, навіть якщо IsModified = false
            // Бо FileSystemWatcher може не спрацювати для Word/Excel
            var unsavedFiles = new List<OpenFileTracker>();

            foreach (var tracker in _openFiles.Values)
            {
                try
                {
                    if (File.Exists(tracker.LocalPath))
                    {
                        var currentSize = new FileInfo(tracker.LocalPath).Length;
                        var currentModified = File.GetLastWriteTime(tracker.LocalPath);

                        // Перевірка чи файл змінився з моменту останнього збереження
                        if (currentSize != tracker.LastFileSize || currentModified > tracker.LastModified)
                        {
                            tracker.IsModified = true;
                            Console.WriteLine($"[RemoteDirectoryBrowser] ⚠️ Виявлено незбережені зміни при закритті: {Path.GetFileName(tracker.RemotePath)}");
                        }
                    }

                    if (tracker.IsModified)
                    {
                        unsavedFiles.Add(tracker);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RemoteDirectoryBrowser] Помилка перевірки файлу: {ex.Message}");
                }
            }

            if (unsavedFiles.Any())
            {
                var result = MessageBox.Show(
                    $"У вас є {unsavedFiles.Count} незбережених файлів. Завантажити їх назад на віддалений пристрій?\n\n" +
                    $"Так - завантажити\n" +
                    $"Ні - відкинути зміни\n" +
                    $"Скасувати - скасувати закриття",
                    "Незбережені зміни",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // Завантажити всі файли синхронно
                    foreach (var file in unsavedFiles)
                    {
                        try
                        {
                            var uploadTask = UploadFileToRemote(file);
                            uploadTask.Wait(); // Синхронне очікування
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RemoteDirectoryBrowser] Помилка завантаження: {ex.Message}");
                        }
                    }
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            // Розблокувати всі файли і зупинити таймери
            foreach (var tracker in _openFiles.Values)
            {
                // Зупинити таймери
                tracker.AutoSaveTimer?.Stop();
                tracker.HeartbeatTimer?.Stop();

                // Розблокувати файл якщо він був заблокований
                if (tracker.LockInfo != null && tracker.LockInfo.IsOwnedByCurrentDevice)
                {
                    try
                    {
                        var unlockTask = _client.RemoteUnlockFileAsync(_deviceName, tracker.RemotePath);
                        unlockTask.Wait(); // Синхронне розблокування
                        Console.WriteLine($"[RemoteDirectoryBrowser] Файл розблоковано: {tracker.RemotePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RemoteDirectoryBrowser] Помилка розблокування: {ex.Message}");
                    }
                }

                // Dispose resources
                tracker.Dispose();
            }
            _openFiles.Clear();

            // Видалити тимчасові файли
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка видалення temp: {ex.Message}");
            }
        }

        private async void RemoteDirectoryBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadRemoteDirectories();
        }

        #region Directory List Management

        private async Task LoadRemoteDirectories()
        {
            try
            {
                SetStatus("Завантаження shared директорій...");

                _remoteDirectories = await _client.GetRemoteDirectoriesAsync(_deviceName);

                Console.WriteLine($"[RemoteDirectoryBrowser] Отримано {_remoteDirectories.Count} директорій");

                foreach (var dir in _remoteDirectories)
                {
                    dir.SharedStatusText = dir.IsShared ? "✅" : "🔒";
                }

                DirectoriesGrid.ItemsSource = _remoteDirectories;
                DirectoryCountText.Text = $"{_remoteDirectories.Count} директорій";

                SetStatus($"Завантажено {_remoteDirectories.Count} директорій");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка: {ex.Message}");
                SetStatus($"Помилка: {ex.Message}");
                await this.ShowMessageAsync("Помилка підключення",
                    $"Не вдалося завантажити директорії з пристрою '{_deviceName}':\n\n{ex.Message}");
            }
        }

        private void DirectoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DirectoriesGrid.SelectedItem is DirectoryWithAccessModel directory)
            {
                _selectedDirectory = directory;
                ShowDirectoryDetails(directory);
                _ = LoadDirectoryStatistics();
                _ = LoadGitHistory();
            }
            else
            {
                ClearDetails();
            }
        }

        #endregion

        #region Statistics Tab

        private async void ShowDirectoryDetails(DirectoryWithAccessModel directory)
        {
            DetailNameText.Text = directory.Name;
            DetailPathText.Text = directory.Browse;

            // Встановлюємо початковий шлях для файлового провідника
            _currentPath = directory.Browse;
            CurrentPathTextBox.Text = _currentPath;
            NavigateUpButton.IsEnabled = false;

            // Показуємо статус
            StatsSharedText.Text = directory.IsShared ? "✅ Відкрито" : "🔒 Закрито";
            StatusIndicator.Fill = directory.IsShared ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) :
                                                        new SolidColorBrush(Color.FromRgb(158, 158, 158));

            // Завантажити файли для вибраної директорії
            await LoadFileSystemItems();
        }

        private void ClearDetails()
        {
            DetailNameText.Text = "-";
            DetailPathText.Text = "-";
            StatsObjectsText.Text = "0";
            StatsFoldersText.Text = "0";
            StatsFilesText.Text = "0";
            StatsSharedText.Text = "-";
            ObjectsProgressBar.Value = 0;
            FoldersProgressBar.Value = 0;
            FilesProgressBar.Value = 0;
        }

        private async Task LoadDirectoryStatistics()
        {
            if (_selectedDirectory == null) return;

            try
            {
                SetStatus("Завантаження статистики...");

                var stats = await _client.GetRemoteDirectoryStatisticsAsync(_deviceName, _selectedDirectory.Id);

                StatsObjectsText.Text = stats.ObjectsCount.ToString();
                StatsFoldersText.Text = stats.FoldersCount.ToString();
                StatsFilesText.Text = stats.FilesCount.ToString();

                // Оновлюємо прогрес бари
                int maxValue = Math.Max(stats.ObjectsCount, Math.Max(stats.FoldersCount, stats.FilesCount));
                if (maxValue > 0)
                {
                    ObjectsProgressBar.Maximum = maxValue;
                    FoldersProgressBar.Maximum = maxValue;
                    FilesProgressBar.Maximum = maxValue;
                    ObjectsProgressBar.Value = stats.ObjectsCount;
                    FoldersProgressBar.Value = stats.FoldersCount;
                    FilesProgressBar.Value = stats.FilesCount;
                }

                SetStatus("Статистика завантажена");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка завантаження статистики: {ex.Message}");
                SetStatus($"Помилка завантаження статистики: {ex.Message}");
            }
        }

        private async void RefreshStats_Click(object sender, RoutedEventArgs e)
        {
            await LoadDirectoryStatistics();
        }

        private async void ScanRemoteDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть директорію для сканування");
                return;
            }

            try
            {
                SetStatus("Сканування директорії...");

                await _client.RemoteScanDirectoryAsync(_deviceName, _selectedDirectory.Id);

                await this.ShowMessageAsync("Успіх", $"Директорія '{_selectedDirectory.Name}' успішно проск анована");
                await LoadDirectoryStatistics();

                SetStatus("Сканування завершено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка сканування: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося просканувати директорію:\n\n{ex.Message}");
                SetStatus("Помилка сканування");
            }
        }

        #endregion

        #region File Explorer Tab

        private async void NavigateToHome_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null) return;

            _currentPath = _selectedDirectory.Browse;
            CurrentPathTextBox.Text = _currentPath;
            NavigateUpButton.IsEnabled = false;
            await LoadFileSystemItems();
        }

        private async void NavigateUp_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath) || _selectedDirectory == null) return;

            var parentPath = System.IO.Path.GetDirectoryName(_currentPath);
            if (!string.IsNullOrEmpty(parentPath) && parentPath.Length >= _selectedDirectory.Browse.Length)
            {
                _currentPath = parentPath;
                CurrentPathTextBox.Text = _currentPath;
                NavigateUpButton.IsEnabled = _currentPath != _selectedDirectory.Browse;
                await LoadFileSystemItems();
            }
        }

        private async Task LoadFileSystemItems()
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            try
            {
                SetStatus("Завантаження файлів...");

                var items = await _client.GetRemoteDirectoryFileListAsync(_deviceName, _currentPath);

                _fileSystemItems.Clear();
                foreach (var item in items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name))
                {
                    _fileSystemItems.Add(new FileSystemItemViewModel(item));
                }

                FileSystemGrid.ItemsSource = _fileSystemItems;

                SetStatus($"Завантажено {items.Count} елементів");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка завантаження файлів: {ex.Message}");
                SetStatus($"Помилка: {ex.Message}");
            }
        }

        private async void FileSystemGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is not FileSystemItemViewModel item) return;

            if (item.IsDirectory)
            {
                // Перехід до директорії
                _currentPath = item.FullPath;
                CurrentPathTextBox.Text = _currentPath;
                NavigateUpButton.IsEnabled = true;
                await LoadFileSystemItems();
            }
            else
            {
                // Відкриття файлу стандартною програмою
                await OpenRemoteFile(item.FullPath);
            }
        }

        private async Task OpenRemoteFile(string remotePath)
        {
            try
            {
                SetStatus("Завантаження файлу...");

                // Перевірити чи файл вже відкритий
                if (_openFiles.ContainsKey(remotePath))
                {
                    // Файл вже відкритий, просто відкрити знову (якщо програма була закрита)
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _openFiles[remotePath].LocalPath,
                            UseShellExecute = true
                        });
                        SetStatus($"Файл відкрито: {Path.GetFileName(remotePath)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RemoteDirectoryBrowser] Помилка відкриття: {ex.Message}");
                    }
                    return;
                }

                var fileName = Path.GetFileName(remotePath);

                // КРОК 1: Спробувати заблокувати файл на віддаленому пристрої
                FileLockModel lockInfo = null;
                try
                {
                    lockInfo = await _client.RemoteLockFileAsync(_deviceName, remotePath);
                    Console.WriteLine($"[RemoteDirectoryBrowser] Файл заблокований: {lockInfo.LockDescription}");
                }
                catch (Exception lockEx)
                {
                    Console.WriteLine($"[RemoteDirectoryBrowser] Не вдалося заблокувати файл: {lockEx.Message}");
                }

                // Перевірити чи файл заблокований іншим користувачем
                if (lockInfo != null && lockInfo.IsLockedByOther)
                {
                    var result = await this.ShowMessageAsync("Файл зайнятий",
                        $"Файл '{fileName}' зараз редагується:\n\n{lockInfo.LockDescription}\n\n" +
                        $"Відкрити у режимі тільки для читання?",
                        MessageDialogStyle.AffirmativeAndNegative,
                        new MetroDialogSettings
                        {
                            AffirmativeButtonText = "Так, читання",
                            NegativeButtonText = "Скасувати"
                        });

                    if (result != MessageDialogResult.Affirmative)
                    {
                        SetStatus("Відкриття скасовано");
                        return;
                    }
                    // Продовжуємо, але без блокування
                    lockInfo = null;
                }

                // КРОК 2: Завантажити вміст файлу з віддаленого пристрою (бінарний режим)
                var content = await _client.RemoteReadFileBinaryAsync(_deviceName, remotePath);

                // Створити локальний файл у temp директорії
                var localPath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString(), fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                await File.WriteAllBytesAsync(localPath, content);

                Console.WriteLine($"[RemoteDirectoryBrowser] Завантажено файл: {remotePath} -> {localPath}");

                // КРОК 3: Створити FileSystemWatcher для моніторингу змін
                // ВАЖЛИВО: Word/Excel не просто змінюють файл, вони створюють temp і перейменовують
                var watcher = new FileSystemWatcher(Path.GetDirectoryName(localPath))
                {
                    Filter = fileName,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                var tracker = new OpenFileTracker
                {
                    RemotePath = remotePath,
                    LocalPath = localPath,
                    Watcher = watcher,
                    IsModified = false,
                    LastModified = File.GetLastWriteTime(localPath),
                    LastFileSize = new FileInfo(localPath).Length,
                    LockInfo = lockInfo,
                    LastSaved = DateTime.Now
                };

                // КРОК 4: Налаштувати автозбереження (тільки якщо файл заблокований нами)
                if (lockInfo != null && lockInfo.IsOwnedByCurrentDevice)
                {
                    // Таймер автозбереження (кожні 10 секунд для швидкого тестування)
                    // TODO: Збільшити до 30 секунд в продакшені
                    tracker.AutoSaveTimer = new System.Timers.Timer(10000); // 10 секунд
                    tracker.AutoSaveTimer.Elapsed += async (s, e) =>
                    {
                        try
                        {
                            // КРИТИЧНО: Перевірити чи файл дійсно змінився порівняно з останнім збереженням
                            if (File.Exists(tracker.LocalPath))
                            {
                                var currentSize = new FileInfo(tracker.LocalPath).Length;
                                var currentModified = File.GetLastWriteTime(tracker.LocalPath);

                                if (currentSize != tracker.LastFileSize || currentModified > tracker.LastModified)
                                {
                                    tracker.IsModified = true;
                                    tracker.LastFileSize = currentSize;
                                    tracker.LastModified = currentModified;
                                }
                            }

                            if (tracker.IsModified)
                            {
                                Console.WriteLine($"[RemoteDirectoryBrowser] 🔄 Автозбереження: {fileName} → {_deviceName}");
                                await Dispatcher.InvokeAsync(async () =>
                                {
                                    await UploadFileToRemote(tracker);
                                    tracker.LastSaved = DateTime.Now;
                                });
                            }
                            else
                            {
                                // Логування тільки кожні 30 сек щоб не спамити
                                if ((DateTime.Now - tracker.LastSaved).TotalSeconds > 30)
                                {
                                    Console.WriteLine($"[RemoteDirectoryBrowser] ⏭️ Автозбереження: {fileName} без змін");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RemoteDirectoryBrowser] ❌ Помилка автозбереження: {ex.Message}");
                        }
                    };
                    tracker.AutoSaveTimer.Start();

                    // Таймер heartbeat (кожні 30 секунд)
                    tracker.HeartbeatTimer = new System.Timers.Timer(30000); // 30 секунд
                    tracker.HeartbeatTimer.Elapsed += async (s, e) =>
                    {
                        try
                        {
                            await _client.RemoteUpdateFileLockHeartbeatAsync(_deviceName, remotePath);
                            Console.WriteLine($"[RemoteDirectoryBrowser] Heartbeat: {fileName}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[RemoteDirectoryBrowser] Помилка heartbeat: {ex.Message}");
                        }
                    };
                    tracker.HeartbeatTimer.Start();

                    Console.WriteLine($"[RemoteDirectoryBrowser] Автозбереження активовано для: {fileName}");
                }

                // Універсальний обробник для ВСІХ подій (Changed, Created, Renamed)
                // Word/Excel створюють temp файли і перейменовують, тому потрібні всі події
                FileSystemEventHandler fileChangeHandler = async (s, e) =>
                {
                    try
                    {
                        // Невелика затримка щоб файл встиг закритися після збереження
                        await Task.Delay(500);

                        if (!File.Exists(localPath))
                        {
                            Console.WriteLine($"[RemoteDirectoryBrowser] Файл не існує після події: {localPath}");
                            return;
                        }

                        var newModified = File.GetLastWriteTime(localPath);
                        var newSize = new FileInfo(localPath).Length;

                        // Перевірка чи дійсно змінився файл (час АБО розмір)
                        if (newModified > tracker.LastModified || newSize != tracker.LastFileSize)
                        {
                            tracker.IsModified = true;
                            tracker.LastModified = newModified;
                            tracker.LastFileSize = newSize;
                            Console.WriteLine($"[RemoteDirectoryBrowser] 📝 Файл змінено: {fileName} (Event: {e.ChangeType}, Size: {newSize} байт)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RemoteDirectoryBrowser] Помилка обробки події {e.ChangeType}: {ex.Message}");
                    }
                };

                // Підписка на ВСІ події для надійної детекції змін Word/Excel
                watcher.Changed += fileChangeHandler;
                watcher.Created += fileChangeHandler;
                watcher.Renamed += (s, e) => fileChangeHandler(s, e);

                _openFiles[remotePath] = tracker;

                // Відкрити файл стандартною програмою
                Process.Start(new ProcessStartInfo
                {
                    FileName = localPath,
                    UseShellExecute = true
                });

                SetStatus($"Файл відкрито: {fileName}");
                Console.WriteLine($"[RemoteDirectoryBrowser] Відкрито {_openFiles.Count} файлів");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка відкриття файлу: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося відкрити файл:\n\n{ex.Message}");
                SetStatus("Помилка відкриття файлу");
            }
        }

        private async Task UploadFileToRemote(OpenFileTracker tracker)
        {
            try
            {
                var fileName = Path.GetFileName(tracker.RemotePath);
                SetStatus($"💾 Збереження '{fileName}' на {_deviceName}...");

                // Прочитати локальний файл (бінарний режим)
                var content = await File.ReadAllBytesAsync(tracker.LocalPath);

                Console.WriteLine($"[RemoteDirectoryBrowser] Відправка {content.Length} байт на {_deviceName}: {tracker.RemotePath}");

                // Завантажити на віддалений пристрій (КОМП'ЮТЕР А)
                await _client.RemoteWriteFileBinaryAsync(_deviceName, tracker.RemotePath, content);

                tracker.IsModified = false;
                tracker.LastModified = File.GetLastWriteTime(tracker.LocalPath);

                SetStatus($"✅ Збережено на {_deviceName}: {fileName} ({content.Length:N0} байт)");
                Console.WriteLine($"[RemoteDirectoryBrowser] ✅ Файл збережено на {_deviceName}: {tracker.RemotePath} ({content.Length} байт)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] ❌ Помилка збереження на {_deviceName}: {ex.Message}");
                MessageBox.Show($"Не вдалося зберегти файл на {_deviceName}:\n\n{ex.Message}",
                    "Помилка збереження на сервері", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus($"❌ Помилка збереження на {_deviceName}");
            }
        }

        private bool IsTextFile(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLower();
            return extension switch
            {
                ".txt" or ".log" or ".md" or ".xml" or ".json" or ".csv" or
                ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h" or ".c" or
                ".html" or ".css" or ".scss" or ".sql" or ".sh" or ".bat" or ".ps1" or
                ".yaml" or ".yml" or ".config" or ".ini" or ".conf" => true,
                _ => false
            };
        }

        private void FileSystemGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is FileSystemItemViewModel item)
            {
                ShowFileDetails(item);
                RenameButton.IsEnabled = true;
                DeleteButton.IsEnabled = true;
            }
            else
            {
                ClearFileDetails();
                RenameButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
        }

        private void ShowFileDetails(FileSystemItemViewModel item)
        {
            FileDetailNameText.Text = item.Name;
            FileDetailPathText.Text = item.FullPath;
            FileDetailTypeText.Text = item.IsDirectory ? "📁 Папка" : $"📄 Файл ({item.Extension})";
            FileDetailSizeText.Text = item.SizeString;
            FileDetailCreatedText.Text = item.CreatedString;
            FileDetailModifiedText.Text = item.ModifiedString;
        }

        private void ClearFileDetails()
        {
            FileDetailNameText.Text = "-";
            FileDetailPathText.Text = "-";
            FileDetailTypeText.Text = "-";
            FileDetailSizeText.Text = "-";
            FileDetailCreatedText.Text = "-";
            FileDetailModifiedText.Text = "-";
        }

        private async void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            var folderName = await this.ShowInputAsync("Створити папку", "Введіть назву нової папки:");
            if (string.IsNullOrWhiteSpace(folderName)) return;

            try
            {
                SetStatus("Створення папки...");

                await _client.RemoteCreateFolderAsync(_deviceName, _currentPath, folderName);

                await this.ShowMessageAsync("Успіх", $"Папку '{folderName}' успішно створено");
                await LoadFileSystemItems();

                SetStatus("Папку створено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка створення папки: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося створити папку:\n\n{ex.Message}");
                SetStatus("Помилка створення");
            }
        }

        private async void CreateFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath)) return;

            var fileName = await this.ShowInputAsync("Створити файл", "Введіть назву нового файлу:");
            if (string.IsNullOrWhiteSpace(fileName)) return;

            try
            {
                SetStatus("Створення файлу...");

                await _client.RemoteCreateFileAsync(_deviceName, _currentPath, fileName);

                await this.ShowMessageAsync("Успіх", $"Файл '{fileName}' успішно створено");
                await LoadFileSystemItems();

                SetStatus("Файл створено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка створення файлу: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося створити файл:\n\n{ex.Message}");
                SetStatus("Помилка створення");
            }
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is not FileSystemItemViewModel item) return;

            var newName = await this.ShowInputAsync("Перейменувати", "Введіть нову назву:", new MetroDialogSettings
            {
                DefaultText = item.Name
            });

            if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

            try
            {
                SetStatus("Перейменування...");

                await _client.RemoteRenameFileOrFolderAsync(_deviceName, item.FullPath, newName);

                await this.ShowMessageAsync("Успіх", $"'{item.Name}' успішно перейменовано в '{newName}'");
                await LoadFileSystemItems();

                SetStatus("Перейменовано");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка перейменування: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося перейменувати:\n\n{ex.Message}");
                SetStatus("Помилка перейменування");
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is not FileSystemItemViewModel item) return;

            var result = await this.ShowMessageAsync("Підтвердження видалення",
                $"Ви впевнені, що хочете видалити '{item.Name}'?",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = "Так", NegativeButtonText = "Ні" });

            if (result != MessageDialogResult.Affirmative) return;

            try
            {
                SetStatus("Видалення...");

                await _client.RemoteDeleteFileOrFolderAsync(_deviceName, item.FullPath, item.IsDirectory, recursive: true);

                await this.ShowMessageAsync("Успіх", $"'{item.Name}' успішно видалено");
                await LoadFileSystemItems();

                SetStatus("Видалено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка видалення: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося видалити:\n\n{ex.Message}");
                SetStatus("Помилка видалення");
            }
        }

        private async void RefreshExplorer_Click(object sender, RoutedEventArgs e)
        {
            await LoadFileSystemItems();
        }

        #endregion

        #region Git Tab

        private async Task LoadGitHistory()
        {
            if (_selectedDirectory == null) return;

            try
            {
                SetStatus("Завантаження git історії...");

                var history = await _client.RemoteGetGitHistoryAsync(_deviceName, _selectedDirectory.Id, 100);

                GitHistoryGrid.ItemsSource = history;

                SetStatus($"Завантажено {history.Count} комітів");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка завантаження git історії: {ex.Message}");
                SetStatus("Помилка завантаження історії");
            }
        }

        private async void GitCommit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть директорію");
                return;
            }

            var message = CommitMessageTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                await this.ShowMessageAsync("Помилка", "Введіть повідомлення коміту");
                return;
            }

            try
            {
                SetStatus("Виконання коміту...");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                GitStatusLabel.Text = "Виконується...";

                await _client.RemoteGitCommitAsync(_deviceName, _selectedDirectory.Id, message);

                await this.ShowMessageAsync("Успіх", $"Коміт успішно виконано:\n{message}");
                CommitMessageTextBox.Clear();
                await LoadGitHistory();

                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                GitStatusLabel.Text = "Готово";
                SetStatus("Коміт виконано");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка коміту: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося виконати коміт:\n\n{ex.Message}");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                GitStatusLabel.Text = "Помилка";
                SetStatus("Помилка коміту");
            }
        }

        private async void GitShowHistory_Click(object sender, RoutedEventArgs e)
        {
            await LoadGitHistory();
        }

        private async void GitRevert_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть директорію");
                return;
            }

            if (GitHistoryGrid.SelectedItem is not GitCommitHistoryModel commit)
            {
                await this.ShowMessageAsync("Помилка", "Оберіть коміт з історії для відкату");
                return;
            }

            var result = await this.ShowMessageAsync("Підтвердження відкату",
                $"Ви впевнені, що хочете відкотити до коміту:\n\n{commit.Hash}\n{commit.Message}\n\nЦе видалить всі зміни після цього коміту!",
                MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings { AffirmativeButtonText = "Так, відкотити", NegativeButtonText = "Скасувати" });

            if (result != MessageDialogResult.Affirmative) return;

            try
            {
                SetStatus("Відкат версії...");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                GitStatusLabel.Text = "Відкат...";

                await _client.RemoteGitRevertAsync(_deviceName, _selectedDirectory.Id, commit.Hash);

                await this.ShowMessageAsync("Успіх", $"Успішно відкочено до коміту:\n{commit.Hash}");
                await LoadGitHistory();

                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                GitStatusLabel.Text = "Готово";
                SetStatus("Відкат виконано");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteDirectoryBrowser] Помилка відкату: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося виконати відкат:\n\n{ex.Message}");
                GitStatusIndicator.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                GitStatusLabel.Text = "Помилка";
                SetStatus("Помилка відкату");
            }
        }

        private async void GitRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadGitHistory();
        }

        #endregion

        #region Common

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRemoteDirectories();
            if (_selectedDirectory != null)
            {
                await LoadDirectoryStatistics();
                await LoadGitHistory();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        #endregion
    }

    /// <summary>
    /// ViewModel для відображення файлової системи
    /// </summary>
    public class FileSystemItemViewModel
    {
        private readonly FileSystemItemModel _model;

        public FileSystemItemViewModel(FileSystemItemModel model)
        {
            _model = model;
        }

        public string Name => _model.Name;
        public string FullPath => _model.FullPath;
        public bool IsDirectory => _model.IsDirectory;
        public long Size => _model.Size;
        public DateTime CreatedDate => _model.CreatedDate;
        public DateTime ModifiedDate => _model.ModifiedDate;
        public string Extension => _model.Extension;

        public string Icon => IsDirectory ? "📁" : GetFileIcon(Extension);

        public string SizeString
        {
            get
            {
                if (IsDirectory) return "<DIR>";
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F2} KB";
                if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024.0):F2} MB";
                return $"{Size / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }

        public string CreatedString => CreatedDate.ToString("dd.MM.yyyy HH:mm");
        public string ModifiedString => ModifiedDate.ToString("dd.MM.yyyy HH:mm");

        private string GetFileIcon(string extension)
        {
            return extension?.ToLower() switch
            {
                ".txt" => "📄",
                ".pdf" => "📕",
                ".doc" or ".docx" => "📘",
                ".xls" or ".xlsx" => "📊",
                ".zip" or ".rar" or ".7z" => "📦",
                ".jpg" or ".jpeg" or ".png" or ".gif" => "🖼️",
                ".mp3" or ".wav" => "🎵",
                ".mp4" or ".avi" => "🎬",
                ".cs" or ".js" or ".py" or ".java" => "💻",
                ".xml" or ".json" => "📋",
                _ => "📄"
            };
        }
    }

    /// <summary>
    /// Трекер для відстеження відкритих віддалених файлів
    /// </summary>
    public class OpenFileTracker : IDisposable
    {
        /// <summary>
        /// Шлях до файлу на віддаленому пристрої
        /// </summary>
        public string RemotePath { get; set; }

        /// <summary>
        /// Локальний шлях (у temp директорії)
        /// </summary>
        public string LocalPath { get; set; }

        /// <summary>
        /// FileSystemWatcher для моніторингу змін
        /// </summary>
        public FileSystemWatcher Watcher { get; set; }

        /// <summary>
        /// Чи файл був змінений
        /// </summary>
        public bool IsModified { get; set; }

        /// <summary>
        /// Час останньої модифікації
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Розмір файлу при останній перевірці
        /// </summary>
        public long LastFileSize { get; set; }

        /// <summary>
        /// Інформація про блокування файлу
        /// </summary>
        public FileLockModel LockInfo { get; set; }

        /// <summary>
        /// Таймер для автозбереження (кожні 30 секунд)
        /// </summary>
        public System.Timers.Timer AutoSaveTimer { get; set; }

        /// <summary>
        /// Таймер для heartbeat (кожні 30 секунд)
        /// </summary>
        public System.Timers.Timer HeartbeatTimer { get; set; }

        /// <summary>
        /// Час останнього збереження
        /// </summary>
        public DateTime LastSaved { get; set; }

        public void Dispose()
        {
            Watcher?.Dispose();
            AutoSaveTimer?.Dispose();
            HeartbeatTimer?.Dispose();
        }
    }
}
