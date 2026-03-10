using DocControlService.Client;
using DocControlService.Shared;
using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using OpenXmlWord = DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using IOPath = System.IO.Path;

namespace DocControlUI.Windows
{
    public partial class DirectoryManagerWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private List<DirectoryModel> _allDirectories;
        private List<DirectoryModel> _filteredDirectories;
        private DirectoryModel _selectedDirectory;
        private int _selectedDirectoryId;

        // Файловий провідник
        private string _currentPath;
        private FileSystemItem _selectedFileSystemItem;
        private List<FileSystemItem> _currentFileSystemItems;
        private DirectoryModel _currentDirectory; // Поточна директорія з БД
        private bool _isShowingDirectories = true; // Режим: показ директорій БД або вміст папки

        // Контроль версій
        private DirectoryModel _vcSelectedDirectory;
        private List<GitCommitModel> _vcCommitHistory;

        public DirectoryManagerWindow()
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            Loaded += DirectoryManagerWindow_Loaded;
        }

        private async void DirectoryManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshDirectories();
            LoadDirectoriesFromDatabase();
            InitializeVersionControl();
        }

        private async System.Threading.Tasks.Task RefreshDirectories()
        {
            try
            {
                SetStatus("Завантаження директорій...");

                // Отримуємо всі директорії через новий метод або існуючий
                var directoriesWithAccess = await _client.GetDirectoriesAsync();

                // Конвертуємо у простішу модель для відображення
                _allDirectories = directoriesWithAccess.Select(d => new DirectoryModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Browse = d.Browse
                }).ToList();

                _filteredDirectories = _allDirectories;
                DirectoriesGrid.ItemsSource = _filteredDirectories;
                ResultsCountText.Text = $"Знайдено директорій: {_filteredDirectories.Count}";

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося завантажити директорії:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async void SearchTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await PerformSearch();
            }
        }

        private async System.Threading.Tasks.Task PerformSearch()
        {
            try
            {
                string query = SearchTextBox.Text?.Trim();

                if (string.IsNullOrEmpty(query))
                {
                    // Показуємо всі директорії
                    _filteredDirectories = _allDirectories;
                    DirectoriesGrid.ItemsSource = _filteredDirectories;
                    ResultsCountText.Text = $"Знайдено директорій: {_filteredDirectories.Count}";
                    SetStatus("Показано всі директорії");
                    return;
                }

                SetStatus("Пошук...");

                // Використовуємо новий метод пошуку
                var results = await _client.SearchDirectoriesAsync(query);

                _filteredDirectories = results;
                DirectoriesGrid.ItemsSource = _filteredDirectories;
                ResultsCountText.Text = $"Знайдено директорій: {_filteredDirectories.Count}";

                SetStatus($"Знайдено {results.Count} результатів");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка пошуку: {ex.Message}");
                MessageBox.Show($"Помилка при пошуку:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DirectoriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DirectoriesGrid.SelectedItem is DirectoryModel directory)
            {
                _selectedDirectory = directory;
                _selectedDirectoryId = directory.Id;

                // Заповнюємо деталі
                DetailIdText.Text = directory.Id.ToString();
                DetailNameTextBox.Text = directory.Name;
                DetailPathTextBox.Text = directory.Browse;

                // Завантажуємо статистику
                await LoadStatistics(directory.Id);
            }
        }

        private async System.Threading.Tasks.Task LoadStatistics(int directoryId)
        {
            try
            {
                SetStatus("Завантаження статистики...");

                var stats = await _client.GetDirectoryStatisticsAsync(directoryId);

                // Оновлюємо текстові значення
                StatsObjectsText.Text = stats.ObjectsCount.ToString();
                StatsFoldersText.Text = stats.FoldersCount.ToString();
                StatsFilesText.Text = stats.FilesCount.ToString();
                StatsDevicesText.Text = stats.AllowedDevicesCount.ToString();
                StatsSharedText.Text = stats.IsShared ? "✅ Відкрито" : "🔒 Закрито";

                // Оновлюємо легенду
                LegendObjectsText.Text = $"Об'єкти: {stats.ObjectsCount}";
                LegendFoldersText.Text = $"Папки: {stats.FoldersCount}";
                LegendFilesText.Text = $"Файли: {stats.FilesCount}";

                // Малюємо кругову діаграму
                DrawPieChart(stats.ObjectsCount, stats.FoldersCount, stats.FilesCount);

                // Оновлюємо прогрес бари
                UpdateProgressBars(stats);

                // Оновлюємо індикатор статусу
                UpdateStatusIndicator(stats.IsShared);

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка завантаження статистики: {ex.Message}");
                ClearStatistics();
            }
        }

        private void DrawPieChart(int objects, int folders, int files)
        {
            PieChartCanvas.Children.Clear();

            int total = objects + folders + files;
            if (total == 0)
            {
                // Показуємо порожню діаграму
                var emptyCircle = new Ellipse
                {
                    Width = 120,
                    Height = 120,
                    Fill = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    Stroke = new SolidColorBrush(Color.FromRgb(189, 189, 189)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(emptyCircle, 0);
                Canvas.SetTop(emptyCircle, 0);
                PieChartCanvas.Children.Add(emptyCircle);
                return;
            }

            double centerX = 60;
            double centerY = 60;
            double radius = 58;

            double startAngle = -90; // Початок зверху

            // Об'єкти (синій)
            if (objects > 0)
            {
                double angle = (objects / (double)total) * 360;
                DrawPieSlice(centerX, centerY, radius, startAngle, angle, Color.FromRgb(33, 150, 243));
                startAngle += angle;
            }

            // Папки (зелений)
            if (folders > 0)
            {
                double angle = (folders / (double)total) * 360;
                DrawPieSlice(centerX, centerY, radius, startAngle, angle, Color.FromRgb(76, 175, 80));
                startAngle += angle;
            }

            // Файли (помаранчевий)
            if (files > 0)
            {
                double angle = (files / (double)total) * 360;
                DrawPieSlice(centerX, centerY, radius, startAngle, angle, Color.FromRgb(255, 152, 0));
            }
        }

        private void DrawPieSlice(double centerX, double centerY, double radius, double startAngle, double angle, Color color)
        {
            if (angle >= 360)
            {
                // Повне коло
                var circle = new Ellipse
                {
                    Width = radius * 2,
                    Height = radius * 2,
                    Fill = new SolidColorBrush(color),
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(circle, centerX - radius);
                Canvas.SetTop(circle, centerY - radius);
                PieChartCanvas.Children.Add(circle);
                return;
            }

            var path = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.White,
                StrokeThickness = 2
            };

            var figure = new PathFigure { StartPoint = new Point(centerX, centerY) };

            double startRad = startAngle * Math.PI / 180;
            double endRad = (startAngle + angle) * Math.PI / 180;

            Point startPoint = new Point(
                centerX + radius * Math.Cos(startRad),
                centerY + radius * Math.Sin(startRad)
            );

            Point endPoint = new Point(
                centerX + radius * Math.Cos(endRad),
                centerY + radius * Math.Sin(endRad)
            );

            figure.Segments.Add(new LineSegment(startPoint, false));
            figure.Segments.Add(new ArcSegment
            {
                Point = endPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = angle > 180
            });
            figure.Segments.Add(new LineSegment(new Point(centerX, centerY), false));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            path.Data = geometry;

            PieChartCanvas.Children.Add(path);
        }

        private void UpdateProgressBars(DirectoryStatisticsModel stats)
        {
            int total = stats.ObjectsCount + stats.FoldersCount + stats.FilesCount;

            if (total == 0)
            {
                ObjectsProgressBar.Value = 0;
                FoldersProgressBar.Value = 0;
                FilesProgressBar.Value = 0;
            }
            else
            {
                ObjectsProgressBar.Value = (stats.ObjectsCount / (double)total) * 100;
                FoldersProgressBar.Value = (stats.FoldersCount / (double)total) * 100;
                FilesProgressBar.Value = (stats.FilesCount / (double)total) * 100;
            }

            DevicesProgressBar.Value = Math.Min(stats.AllowedDevicesCount, 20);
        }

        private void UpdateStatusIndicator(bool isShared)
        {
            if (isShared)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Зелений
                StatsSharedText.Text = "✅ Відкрито";
                StatusBorder.Background = new SolidColorBrush(Color.FromArgb(20, 76, 175, 80));
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Червоний
                StatsSharedText.Text = "🔒 Закрито";
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            }
        }

        private void ClearStatistics()
        {
            StatsObjectsText.Text = "0";
            StatsFoldersText.Text = "0";
            StatsFilesText.Text = "0";
            StatsDevicesText.Text = "0";
            StatsSharedText.Text = "Невідомо";

            LegendObjectsText.Text = "Об'єкти: 0";
            LegendFoldersText.Text = "Папки: 0";
            LegendFilesText.Text = "Файли: 0";

            PieChartCanvas.Children.Clear();
            ObjectsProgressBar.Value = 0;
            FoldersProgressBar.Value = 0;
            FilesProgressBar.Value = 0;
            DevicesProgressBar.Value = 0;

            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158));
        }

        private async void SaveChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                MessageBox.Show("Виберіть директорію для редагування",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string newName = DetailNameTextBox.Text?.Trim();
                string newPath = DetailPathTextBox.Text?.Trim();

                if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(newPath))
                {
                    MessageBox.Show("Назва та шлях не можуть бути порожніми",
                        "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SetStatus("Збереження змін...");

                await _client.UpdateDirectoryAsync(_selectedDirectoryId, newName, newPath);

                MessageBox.Show("Зміни успішно збережено!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshDirectories();
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося зберегти зміни:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelChanges_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory != null)
            {
                // Відновлюємо оригінальні значення
                DetailNameTextBox.Text = _selectedDirectory.Name;
                DetailPathTextBox.Text = _selectedDirectory.Browse;
                SetStatus("Зміни скасовано");
            }
        }

        private async void ScanDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                MessageBox.Show("Виберіть директорію для сканування",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SetStatus("Сканування директорії...");

                await _client.ScanDirectoryAsync(_selectedDirectoryId);

                MessageBox.Show("Сканування завершено!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                await LoadStatistics(_selectedDirectoryId);
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося відсканувати директорію:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDirectory == null)
            {
                MessageBox.Show("Виберіть директорію для видалення",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Ви впевнені, що хочете видалити директорію '{_selectedDirectory.Name}'?",
                "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                SetStatus("Видалення директорії...");

                await _client.RemoveDirectoryAsync(_selectedDirectoryId);

                MessageBox.Show("Директорію успішно видалено!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshDirectories();
                ClearDetails();
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося видалити директорію:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearDetails()
        {
            DetailIdText.Text = "-";
            DetailNameTextBox.Text = "";
            DetailPathTextBox.Text = "";
            ClearStatistics();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            await RefreshDirectories();
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        #region Export Methods

        private async void ExportToExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel файли (*.xlsx)|*.xlsx",
                    FileName = $"Директорії_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    SetStatus("Експорт в Excel...");

                    await System.Threading.Tasks.Task.Run(() => ExportToExcel(saveFileDialog.FileName));

                    SetStatus("Готово");
                    MessageBox.Show($"Дані успішно експортовано в:\n{saveFileDialog.FileName}",
                        "Експорт завершено", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Відкрити файл
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка експорту: {ex.Message}");
                MessageBox.Show($"Помилка при експорті в Excel:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToExcel(string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Директорії");

            // Заголовок
            worksheet.Cell(1, 1).Value = "Звіт по директоріям";
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 16;
            worksheet.Range(1, 1, 1, 7).Merge();

            worksheet.Cell(2, 1).Value = $"Дата формування: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            worksheet.Range(2, 1, 2, 7).Merge();

            // Заголовки таблиці
            int row = 4;
            worksheet.Cell(row, 1).Value = "ID";
            worksheet.Cell(row, 2).Value = "Назва";
            worksheet.Cell(row, 3).Value = "Шлях";
            worksheet.Cell(row, 4).Value = "Об'єкти";
            worksheet.Cell(row, 5).Value = "Папки";
            worksheet.Cell(row, 6).Value = "Файли";
            worksheet.Cell(row, 7).Value = "Пристрої";

            worksheet.Range(row, 1, row, 7).Style.Font.Bold = true;
            worksheet.Range(row, 1, row, 7).Style.Fill.BackgroundColor = XLColor.LightBlue;

            // Дані
            row++;
            foreach (var directory in _filteredDirectories)
            {
                worksheet.Cell(row, 1).Value = directory.Id;
                worksheet.Cell(row, 2).Value = directory.Name;
                worksheet.Cell(row, 3).Value = directory.Browse;

                // Отримуємо статистику синхронно
                try
                {
                    var stats = _client.GetDirectoryStatisticsAsync(directory.Id).Result;
                    worksheet.Cell(row, 4).Value = stats.ObjectsCount;
                    worksheet.Cell(row, 5).Value = stats.FoldersCount;
                    worksheet.Cell(row, 6).Value = stats.FilesCount;
                    worksheet.Cell(row, 7).Value = stats.AllowedDevicesCount;
                }
                catch
                {
                    worksheet.Cell(row, 4).Value = "-";
                    worksheet.Cell(row, 5).Value = "-";
                    worksheet.Cell(row, 6).Value = "-";
                    worksheet.Cell(row, 7).Value = "-";
                }

                row++;
            }

            // Автоширина колонок
            worksheet.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
        }

        private async void ExportToWord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Word документи (*.docx)|*.docx",
                    FileName = $"Директорії_{DateTime.Now:yyyy-MM-dd_HH-mm}.docx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    SetStatus("Експорт в Word...");

                    await System.Threading.Tasks.Task.Run(() => ExportToWord(saveFileDialog.FileName));

                    SetStatus("Готово");
                    MessageBox.Show($"Дані успішно експортовано в:\n{saveFileDialog.FileName}",
                        "Експорт завершено", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Відкрити файл
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка експорту: {ex.Message}");
                MessageBox.Show($"Помилка при експорті в Word:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToWord(string filePath)
        {
            using var wordDocument = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
            var mainPart = wordDocument.AddMainDocumentPart();
            mainPart.Document = new OpenXmlWord.Document();
            var body = mainPart.Document.AppendChild(new OpenXmlWord.Body());

            // Заголовок
            var titleParagraph = body.AppendChild(new OpenXmlWord.Paragraph());
            var titleRun = titleParagraph.AppendChild(new OpenXmlWord.Run());
            titleRun.AppendChild(new OpenXmlWord.Text("Звіт по директоріям"));
            var titleRunProperties = titleRun.AppendChild(new OpenXmlWord.RunProperties());
            titleRunProperties.AppendChild(new OpenXmlWord.Bold());
            titleRunProperties.AppendChild(new OpenXmlWord.FontSize { Val = "32" });

            // Дата
            var dateParagraph = body.AppendChild(new OpenXmlWord.Paragraph());
            dateParagraph.AppendChild(new OpenXmlWord.Run(new OpenXmlWord.Text($"Дата формування: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")));

            body.AppendChild(new OpenXmlWord.Paragraph()); // Порожній рядок

            // Створюємо таблицю
            var table = new OpenXmlWord.Table();

            // Властивості таблиці
            var tableProperties = new OpenXmlWord.TableProperties(
                new OpenXmlWord.TableBorders(
                    new OpenXmlWord.TopBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 12 },
                    new OpenXmlWord.BottomBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 12 },
                    new OpenXmlWord.LeftBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 12 },
                    new OpenXmlWord.RightBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 12 },
                    new OpenXmlWord.InsideHorizontalBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 12 },
                    new OpenXmlWord.InsideVerticalBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 12 }
                )
            );
            table.AppendChild(tableProperties);

            // Заголовки таблиці
            var headerRow = new OpenXmlWord.TableRow();
            headerRow.Append(
                CreateTableCell("ID", true),
                CreateTableCell("Назва", true),
                CreateTableCell("Шлях", true),
                CreateTableCell("Об'єкти", true),
                CreateTableCell("Папки", true),
                CreateTableCell("Файли", true),
                CreateTableCell("Пристрої", true)
            );
            table.Append(headerRow);

            // Дані
            foreach (var directory in _filteredDirectories)
            {
                try
                {
                    var stats = _client.GetDirectoryStatisticsAsync(directory.Id).Result;

                    var dataRow = new OpenXmlWord.TableRow();
                    dataRow.Append(
                        CreateTableCell(directory.Id.ToString()),
                        CreateTableCell(directory.Name),
                        CreateTableCell(directory.Browse),
                        CreateTableCell(stats.ObjectsCount.ToString()),
                        CreateTableCell(stats.FoldersCount.ToString()),
                        CreateTableCell(stats.FilesCount.ToString()),
                        CreateTableCell(stats.AllowedDevicesCount.ToString())
                    );
                    table.Append(dataRow);
                }
                catch
                {
                    var dataRow = new OpenXmlWord.TableRow();
                    dataRow.Append(
                        CreateTableCell(directory.Id.ToString()),
                        CreateTableCell(directory.Name),
                        CreateTableCell(directory.Browse),
                        CreateTableCell("-"),
                        CreateTableCell("-"),
                        CreateTableCell("-"),
                        CreateTableCell("-")
                    );
                    table.Append(dataRow);
                }
            }

            body.Append(table);
            mainPart.Document.Save();
        }

        private OpenXmlWord.TableCell CreateTableCell(string text, bool isBold = false)
        {
            var cell = new OpenXmlWord.TableCell();
            var paragraph = new OpenXmlWord.Paragraph();
            var run = new OpenXmlWord.Run(new OpenXmlWord.Text(text));

            if (isBold)
            {
                var runProperties = new OpenXmlWord.RunProperties();
                runProperties.AppendChild(new OpenXmlWord.Bold());
                run.PrependChild(runProperties);
            }

            paragraph.Append(run);
            cell.Append(paragraph);
            return cell;
        }

        #endregion

        #region File Explorer Methods

        // Модель для відображення файлів та папок
        private class FileSystemItem
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public bool IsDirectory { get; set; }
            public long Size { get; set; }
            public DateTime Modified { get; set; }
            public DateTime Created { get; set; }
            public FileAttributes Attributes { get; set; }
            public int? DirectoryId { get; set; } // ID директорії з БД (якщо це директорія з БД)

            public string Icon => IsDirectory ? "📁" : "📄";
            public string SizeString => IsDirectory ? "<DIR>" : FormatFileSize(Size);
            public string ModifiedString => Modified.ToString("yyyy-MM-dd HH:mm");

            private static string FormatFileSize(long bytes)
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                int order = 0;
                double size = bytes;
                while (size >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    size /= 1024;
                }
                return $"{size:0.##} {sizes[order]}";
            }
        }

        // Завантаження списку директорій з БД
        private void LoadDirectoriesFromDatabase()
        {
            try
            {
                _isShowingDirectories = true;
                _currentDirectory = null;
                _currentPath = null;

                var items = new List<FileSystemItem>();

                // Показуємо всі директорії з БД
                foreach (var directory in _allDirectories)
                {
                    if (Directory.Exists(directory.Browse))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(directory.Browse);
                            items.Add(new FileSystemItem
                            {
                                Name = directory.Name,
                                FullPath = directory.Browse,
                                IsDirectory = true,
                                DirectoryId = directory.Id,
                                Modified = dirInfo.LastWriteTime,
                                Created = dirInfo.CreationTime,
                                Attributes = dirInfo.Attributes
                            });
                        }
                        catch
                        {
                            // Якщо немає доступу, все одно показуємо
                            items.Add(new FileSystemItem
                            {
                                Name = $"{directory.Name} (недоступно)",
                                FullPath = directory.Browse,
                                IsDirectory = true,
                                DirectoryId = directory.Id,
                                Modified = DateTime.MinValue,
                                Created = DateTime.MinValue
                            });
                        }
                    }
                    else
                    {
                        // Директорія не існує на диску
                        items.Add(new FileSystemItem
                        {
                            Name = $"{directory.Name} (не знайдено)",
                            FullPath = directory.Browse,
                            IsDirectory = true,
                            DirectoryId = directory.Id,
                            Modified = DateTime.MinValue,
                            Created = DateTime.MinValue
                        });
                    }
                }

                items = items.OrderBy(x => x.Name).ToList();
                _currentFileSystemItems = items;
                FileSystemGrid.ItemsSource = _currentFileSystemItems;

                CurrentPathTextBox.Text = "📁 Директорії з бази даних";
                ScanAndSaveButton.IsEnabled = false; // Вимикаємо кнопку на головній сторінці
                SetStatus($"Показано {items.Count} директорій з БД");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка завантаження: {ex.Message}");
                MessageBox.Show($"Помилка завантаження директорій:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Завантаження файлового провідника (вміст директорії з БД)
        private void LoadFileExplorer(string path)
        {
            try
            {
                // Перевірка, чи шлях всередині поточної директорії БД
                if (_currentDirectory != null && !path.StartsWith(_currentDirectory.Browse, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Неможливо вийти за межі директорії з БД", "Увага",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Directory.Exists(path))
                {
                    MessageBox.Show($"Шлях не існує: {path}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _isShowingDirectories = false;
                _currentPath = path;
                CurrentPathTextBox.Text = path;

                var items = new List<FileSystemItem>();

                // Додаємо директорії
                try
                {
                    var directories = Directory.GetDirectories(path);
                    foreach (var dir in directories)
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            items.Add(new FileSystemItem
                            {
                                Name = dirInfo.Name,
                                FullPath = dirInfo.FullName,
                                IsDirectory = true,
                                Modified = dirInfo.LastWriteTime,
                                Created = dirInfo.CreationTime,
                                Attributes = dirInfo.Attributes
                            });
                        }
                        catch
                        {
                            // Ігноруємо папки, до яких немає доступу
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("Немає доступу до деяких папок у цій директорії", "Увага",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Додаємо файли
                try
                {
                    var files = Directory.GetFiles(path);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            items.Add(new FileSystemItem
                            {
                                Name = fileInfo.Name,
                                FullPath = fileInfo.FullName,
                                IsDirectory = false,
                                Size = fileInfo.Length,
                                Modified = fileInfo.LastWriteTime,
                                Created = fileInfo.CreationTime,
                                Attributes = fileInfo.Attributes
                            });
                        }
                        catch
                        {
                            // Ігноруємо файли, до яких немає доступу
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("Немає доступу до деяких файлів у цій директорії", "Увага",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Сортуємо: спочатку папки, потім файли
                items = items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name).ToList();

                _currentFileSystemItems = items;
                FileSystemGrid.ItemsSource = _currentFileSystemItems;

                // Увімкнуємо кнопку "Сканувати і зберегти", коли всередині директорії БД
                ScanAndSaveButton.IsEnabled = _currentDirectory != null;

                SetStatus($"Завантажено {items.Count(x => x.IsDirectory)} папок і {items.Count(x => !x.IsDirectory)} файлів");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка завантаження: {ex.Message}");
                MessageBox.Show($"Помилка завантаження директорії:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Навігація
        private void NavigateToHome_Click(object sender, RoutedEventArgs e)
        {
            // Повернення до списку директорій з БД
            LoadDirectoriesFromDatabase();
        }

        private void NavigateUp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isShowingDirectories)
                {
                    MessageBox.Show("Ви вже на рівні директорій БД", "Інформація",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_currentDirectory == null || string.IsNullOrEmpty(_currentPath))
                {
                    LoadDirectoriesFromDatabase();
                    return;
                }

                // Перевіряємо, чи ми на корені директорії БД
                if (_currentPath.Equals(_currentDirectory.Browse, StringComparison.OrdinalIgnoreCase))
                {
                    // Повертаємось до списку директорій БД
                    LoadDirectoriesFromDatabase();
                    return;
                }

                var parentDir = Directory.GetParent(_currentPath);
                if (parentDir != null)
                {
                    // Перевіряємо, щоб не вийти за межі директорії БД
                    if (parentDir.FullName.StartsWith(_currentDirectory.Browse, StringComparison.OrdinalIgnoreCase))
                    {
                        LoadFileExplorer(parentDir.FullName);
                    }
                    else
                    {
                        // Якщо батьківська папка вище за корінь директорії БД - повертаємось до кореня
                        LoadFileExplorer(_currentDirectory.Browse);
                    }
                }
                else
                {
                    LoadDirectoriesFromDatabase();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка навігації:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Вибір файлу/папки
        private void FileSystemGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileSystemGrid.SelectedItem is FileSystemItem item)
            {
                _selectedFileSystemItem = item;
                ShowFileDetails(item);
                RenameButton.IsEnabled = true;
                DeleteButton.IsEnabled = true;
            }
            else
            {
                _selectedFileSystemItem = null;
                ClearFileDetails();
                RenameButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
            }
        }

        // Подвійний клік - відкрити папку або файл
        private void FileSystemGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedFileSystemItem != null)
            {
                if (_selectedFileSystemItem.IsDirectory)
                {
                    // Якщо це директорія з БД - встановлюємо її як поточну
                    if (_selectedFileSystemItem.DirectoryId.HasValue)
                    {
                        _currentDirectory = _allDirectories.FirstOrDefault(d => d.Id == _selectedFileSystemItem.DirectoryId.Value);
                        if (_currentDirectory != null)
                        {
                            LoadFileExplorer(_currentDirectory.Browse);
                        }
                    }
                    else
                    {
                        // Звичайна підпапка всередині директорії БД
                        LoadFileExplorer(_selectedFileSystemItem.FullPath);
                    }
                }
                else
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _selectedFileSystemItem.FullPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Не вдалося відкрити файл:\n{ex.Message}",
                            "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // Відображення деталей файлу/папки
        private void ShowFileDetails(FileSystemItem item)
        {
            FileDetailNameText.Text = item.Name;
            FileDetailPathText.Text = item.FullPath;
            FileDetailTypeText.Text = item.IsDirectory ? "Папка" : "Файл";
            FileDetailSizeText.Text = item.SizeString;
            FileDetailCreatedText.Text = item.Created.ToString("yyyy-MM-dd HH:mm:ss");
            FileDetailModifiedText.Text = item.Modified.ToString("yyyy-MM-dd HH:mm:ss");
            FileDetailAttributesText.Text = item.Attributes.ToString();

            // Показуємо додаткові дії для файлів
            if (!item.IsDirectory)
            {
                FileActionsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                FileActionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearFileDetails()
        {
            FileDetailNameText.Text = "-";
            FileDetailPathText.Text = "-";
            FileDetailTypeText.Text = "-";
            FileDetailSizeText.Text = "-";
            FileDetailCreatedText.Text = "-";
            FileDetailModifiedText.Text = "-";
            FileDetailAttributesText.Text = "-";
            FileActionsPanel.Visibility = Visibility.Collapsed;
        }

        // CRUD Операції

        // Створення папки
        private void CreateFolder_Click(object sender, RoutedEventArgs e)
        {
            // Перевірка, чи ми всередині директорії БД
            if (_isShowingDirectories || _currentDirectory == null || string.IsNullOrEmpty(_currentPath))
            {
                MessageBox.Show("Спочатку оберіть директорію з БД для роботи", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new InputDialog("Створення папки", "Введіть назву нової папки:");
                if (dialog.ShowDialog() == true)
                {
                    string folderName = dialog.ResponseText?.Trim();
                    if (string.IsNullOrEmpty(folderName))
                    {
                        MessageBox.Show("Назва папки не може бути порожньою",
                            "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string newFolderPath = IOPath.Combine(_currentPath, folderName);
                    if (Directory.Exists(newFolderPath))
                    {
                        MessageBox.Show("Папка з такою назвою вже існує",
                            "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    Directory.CreateDirectory(newFolderPath);
                    MessageBox.Show($"Папку '{folderName}' успішно створено!",
                        "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadFileExplorer(_currentPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося створити папку:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Створення файлу
        private void CreateFile_Click(object sender, RoutedEventArgs e)
        {
            // Перевірка, чи ми всередині директорії БД
            if (_isShowingDirectories || _currentDirectory == null || string.IsNullOrEmpty(_currentPath))
            {
                MessageBox.Show("Спочатку оберіть директорію з БД для роботи", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new InputDialog("Створення файлу", "Введіть назву нового файлу:");
                if (dialog.ShowDialog() == true)
                {
                    string fileName = dialog.ResponseText?.Trim();
                    if (string.IsNullOrEmpty(fileName))
                    {
                        MessageBox.Show("Назва файлу не може бути порожньою",
                            "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    string newFilePath = IOPath.Combine(_currentPath, fileName);
                    if (File.Exists(newFilePath))
                    {
                        MessageBox.Show("Файл з такою назвою вже існує",
                            "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    File.Create(newFilePath).Dispose();
                    MessageBox.Show($"Файл '{fileName}' успішно створено!",
                        "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadFileExplorer(_currentPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося створити файл:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Перейменування
        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileSystemItem == null)
            {
                MessageBox.Show("Виберіть файл або папку для перейменування",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new InputDialog("Перейменування",
                    $"Введіть нову назву для '{_selectedFileSystemItem.Name}':",
                    _selectedFileSystemItem.Name);

                if (dialog.ShowDialog() == true)
                {
                    string newName = dialog.ResponseText?.Trim();
                    if (string.IsNullOrEmpty(newName))
                    {
                        MessageBox.Show("Назва не може бути порожньою",
                            "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (newName == _selectedFileSystemItem.Name)
                    {
                        return; // Назва не змінилась
                    }

                    string newPath = IOPath.Combine(_currentPath, newName);

                    if (_selectedFileSystemItem.IsDirectory)
                    {
                        if (Directory.Exists(newPath))
                        {
                            MessageBox.Show("Папка з такою назвою вже існує",
                                "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        Directory.Move(_selectedFileSystemItem.FullPath, newPath);
                    }
                    else
                    {
                        if (File.Exists(newPath))
                        {
                            MessageBox.Show("Файл з такою назвою вже існує",
                                "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        File.Move(_selectedFileSystemItem.FullPath, newPath);
                    }

                    MessageBox.Show($"Успішно перейменовано на '{newName}'!",
                        "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadFileExplorer(_currentPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося перейменувати:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Видалення
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileSystemItem == null)
            {
                MessageBox.Show("Виберіть файл або папку для видалення",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Ви впевнені, що хочете видалити '{_selectedFileSystemItem.Name}'?",
                "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                if (_selectedFileSystemItem.IsDirectory)
                {
                    // Перевіряємо чи папка порожня
                    if (Directory.GetFileSystemEntries(_selectedFileSystemItem.FullPath).Length > 0)
                    {
                        var deleteAllResult = MessageBox.Show(
                            "Папка не порожня. Видалити разом з усім вмістом?",
                            "Підтвердження", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                        if (deleteAllResult != MessageBoxResult.Yes)
                            return;

                        Directory.Delete(_selectedFileSystemItem.FullPath, true);
                    }
                    else
                    {
                        Directory.Delete(_selectedFileSystemItem.FullPath);
                    }
                }
                else
                {
                    File.Delete(_selectedFileSystemItem.FullPath);
                }

                MessageBox.Show("Успішно видалено!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                LoadFileExplorer(_currentPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося видалити:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Додаткові дії
        private void OpenInNotepad_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileSystemItem != null && !_selectedFileSystemItem.IsDirectory)
            {
                try
                {
                    Process.Start("notepad.exe", _selectedFileSystemItem.FullPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не вдалося відкрити файл у блокноті:\n{ex.Message}",
                        "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileSystemItem != null)
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{_selectedFileSystemItem.FullPath}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не вдалося відкрити розташування:\n{ex.Message}",
                        "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileSystemItem != null)
            {
                try
                {
                    Clipboard.SetText(_selectedFileSystemItem.FullPath);
                    MessageBox.Show("Шлях скопійовано в буфер обміну!",
                        "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не вдалося скопіювати шлях:\n{ex.Message}",
                        "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Сканування та збереження директорії
        private async void ScanAndSave_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDirectory == null)
            {
                MessageBox.Show("Помилка: не обрано директорію з БД",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                SetStatus("Сканування директорії...");
                ScanAndSaveButton.IsEnabled = false; // Вимикаємо кнопку під час сканування

                await _client.ScanDirectoryAsync(_currentDirectory.Id);

                MessageBox.Show("Директорію успішно просканована та збережено в БД!",
                    "Успіх", MessageBoxButton.OK, MessageBoxImage.Information);

                SetStatus("Готово");

                // Оновлюємо відображення файлів
                LoadFileExplorer(_currentPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка сканування:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus($"Помилка: {ex.Message}");
            }
            finally
            {
                ScanAndSaveButton.IsEnabled = _currentDirectory != null; // Відновлюємо кнопку
            }
        }

        #endregion

        #region Version Control Methods

        // Модель для відображення комітів
        private class GitCommitModel
        {
            public string Hash { get; set; }
            public string HashShort => Hash?.Length > 7 ? Hash.Substring(0, 7) : Hash;
            public string Message { get; set; }
            public string Author { get; set; }
            public DateTime Date { get; set; }
            public string DateString => Date.ToString("yyyy-MM-dd HH:mm");
        }

        private void InitializeVersionControl()
        {
            // Заповнюємо комбобокс директоріями
            VcDirectoryCombo.ItemsSource = _allDirectories;

            if (_allDirectories != null && _allDirectories.Count > 0)
            {
                VcDirectoryCombo.SelectedIndex = 0;
            }
        }

        private async void VcDirectoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VcDirectoryCombo.SelectedItem is DirectoryModel directory)
            {
                _vcSelectedDirectory = directory;
                await RefreshVersionControlData();
            }
        }

        private async System.Threading.Tasks.Task RefreshVersionControlData()
        {
            if (_vcSelectedDirectory == null) return;

            try
            {
                SetStatus("Завантаження інформації про репозиторій...");

                // Оновлюємо шлях
                VcRepoPathText.Text = _vcSelectedDirectory.Browse;

                // Отримуємо історію комітів
                var history = await _client.GetGitHistoryAsync(_vcSelectedDirectory.Id);

                _vcCommitHistory = history.Select(h => new GitCommitModel
                {
                    Hash = h.Hash,
                    Message = h.Message,
                    Author = h.Author,
                    Date = h.Date
                }).ToList();

                VcHistoryGrid.ItemsSource = _vcCommitHistory;
                VcTotalCommitsText.Text = _vcCommitHistory.Count.ToString();

                if (_vcCommitHistory.Count > 0)
                {
                    var lastCommit = _vcCommitHistory[0];
                    VcLastCommitText.Text = lastCommit.Date.ToString("yyyy-MM-dd HH:mm");
                    VcLastAuthorText.Text = lastCommit.Author;
                }
                else
                {
                    VcLastCommitText.Text = "-";
                    VcLastAuthorText.Text = "-";
                }

                // Отримуємо статус змін (тут потрібно було б додати API метод, але поки використаємо GitStatus)
                await UpdateGitStatus();

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка: {ex.Message}");
                MessageBox.Show($"Не вдалося завантажити дані:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task UpdateGitStatus()
        {
            try
            {
                // Отримуємо оновлені дані про всі директорії
                var directoriesWithAccess = await _client.GetDirectoriesAsync();
                var currentDir = directoriesWithAccess.FirstOrDefault(d => d.Id == _vcSelectedDirectory.Id);

                if (currentDir != null && !string.IsNullOrEmpty(currentDir.GitStatus))
                {
                    string status = currentDir.GitStatus;
                    VcGitStatusLabel.Text = status;

                    // Оновлюємо колір індикатора
                    if (status.Contains("Чисто"))
                    {
                        VcGitStatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Зелений
                        VcChangesCount.Text = "0";
                        VcChangesListBox.ItemsSource = null;
                        VcCommitButton.IsEnabled = false;
                    }
                    else if (status.Contains("Змін:"))
                    {
                        VcGitStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Помаранчевий

                        // Парсимо кількість змін
                        var match = System.Text.RegularExpressions.Regex.Match(status, @"Змін:\s*(\d+)");
                        if (match.Success)
                        {
                            VcChangesCount.Text = match.Groups[1].Value;
                        }

                        // Тут можна було б показати список файлів, але поки показуємо заглушку
                        VcChangesListBox.ItemsSource = new List<string>
                        {
                            "Модифіковані файли (деталі недоступні)",
                            "Використайте git status у терміналі для деталей"
                        };
                        VcCommitButton.IsEnabled = true;
                    }
                    else
                    {
                        VcGitStatusText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Червоний
                        VcChangesCount.Text = "?";
                        VcChangesListBox.ItemsSource = null;
                        VcCommitButton.IsEnabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка оновлення статусу Git: {ex.Message}");
            }
        }

        private async void VcCommit_Click(object sender, RoutedEventArgs e)
        {
            if (_vcSelectedDirectory == null)
            {
                MessageBox.Show("Виберіть репозиторій", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string commitMessage = VcCommitMessageBox.Text?.Trim();
            if (string.IsNullOrEmpty(commitMessage))
            {
                MessageBox.Show("Введіть повідомлення коміту", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                VcCommitMessageBox.Focus();
                return;
            }

            try
            {
                SetStatus("Виконання коміту...");
                VcCommitButton.IsEnabled = false;

                await _client.CommitDirectoryAsync(_vcSelectedDirectory.Id, commitMessage);

                MessageBox.Show("Коміт успішно виконано!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Очищуємо поле повідомлення
                VcCommitMessageBox.Text = "";

                // Оновлюємо дані
                await RefreshVersionControlData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка виконання коміту:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetStatus("Готово");
            }
        }

        private async void VcShowHistory_Click(object sender, RoutedEventArgs e)
        {
            if (_vcSelectedDirectory == null)
            {
                MessageBox.Show("Виберіть репозиторій", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var history = await _client.GetGitHistoryAsync(_vcSelectedDirectory.Id);

                if (history.Count == 0)
                {
                    MessageBox.Show("Історія комітів порожня", "Інформація",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Можна відкрити окреме вікно з історією, або показати у MessageBox
                var historyText = string.Join("\n", history.Take(20).Select(h =>
                    $"{h.Hash.Substring(0, 7)} - {h.Date:yyyy-MM-dd HH:mm} - {h.Author}\n  {h.Message}"));

                MessageBox.Show($"Історія комітів:\n\n{historyText}",
                    $"Історія: {_vcSelectedDirectory.Name}",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка отримання історії:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void VcRevert_Click(object sender, RoutedEventArgs e)
        {
            if (_vcSelectedDirectory == null)
            {
                MessageBox.Show("Виберіть репозиторій", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedCommit = VcHistoryGrid.SelectedItem as GitCommitModel;
            if (selectedCommit == null)
            {
                MessageBox.Show("Виберіть коміт з історії для відкату", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Ви впевнені, що хочете відкотити репозиторій до коміту?\n\n" +
                $"Hash: {selectedCommit.HashShort}\n" +
                $"Повідомлення: {selectedCommit.Message}\n" +
                $"Автор: {selectedCommit.Author}\n" +
                $"Дата: {selectedCommit.DateString}\n\n" +
                $"УВАГА: Всі незбережені зміни будуть втрачені!",
                "Підтвердження відкату",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                SetStatus("Відкат до коміту...");

                await _client.RevertToCommitAsync(_vcSelectedDirectory.Id, selectedCommit.Hash);

                MessageBox.Show("Відкат успішно виконано!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Оновлюємо дані
                await RefreshVersionControlData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка відкату:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetStatus("Готово");
            }
        }

        private async void VcRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_vcSelectedDirectory == null)
            {
                MessageBox.Show("Виберіть репозиторій", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await RefreshVersionControlData();
        }

        #endregion
    }

    // Діалог для введення тексту
    public class InputDialog : Window
    {
        private TextBox _textBox;
        public string ResponseText { get; private set; }

        public InputDialog(string title, string question, string defaultValue = "")
        {
            Title = title;
            Width = 400;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var questionText = new TextBlock
            {
                Text = question,
                Margin = new Thickness(15, 15, 15, 10),
                FontSize = 14
            };
            Grid.SetRow(questionText, 0);
            grid.Children.Add(questionText);

            _textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(15, 5, 15, 15),
                Padding = new Thickness(5),
                FontSize = 14
            };
            _textBox.KeyUp += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ResponseText = _textBox.Text;
                    DialogResult = true;
                }
            };
            Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(15, 10, 15, 15)
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0)
            };
            okButton.Click += (s, e) =>
            {
                ResponseText = _textBox.Text;
                DialogResult = true;
            };

            var cancelButton = new Button
            {
                Content = "Скасувати",
                Width = 80,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0)
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;

            Loaded += (s, e) => _textBox.Focus();
        }
    }
}
