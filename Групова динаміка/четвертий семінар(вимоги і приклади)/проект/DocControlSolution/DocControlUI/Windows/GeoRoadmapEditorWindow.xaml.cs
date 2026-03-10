using DocControlService.Client;
using DocControlService.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MahApps.Metro.Controls;

namespace DocControlUI.Windows
{
    public partial class GeoRoadmapEditorWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private GeoRoadmap _currentRoadmap;
        private List<GeoRoadmapNode> _nodes;
        private List<GeoRoadmapRoute> _routes;
        private List<GeoRoadmapArea> _areas;
        private List<GeoRoadmapTemplate> _templates;

        private GeoRoadmapNode _selectedNode;
        private bool _isAddingNode = false;
        private bool _isConnectingNodes = false;
        private GeoRoadmapNode _connectFromNode = null;

        // WebView2
        private WebView2 _mapWebView;
        private bool _isMapInitialized = false;

        public GeoRoadmapEditorWindow(DocControlServiceClient client, int? roadmapId = null)
        {
            InitializeComponent();

            _client = client ?? throw new ArgumentNullException(nameof(client));
            _nodes = new List<GeoRoadmapNode>();
            _routes = new List<GeoRoadmapRoute>();
            _areas = new List<GeoRoadmapArea>();

            Loaded += async (s, e) => await InitializeAsync(roadmapId);
        }

        #region Initialization

        private async Task InitializeAsync(int? roadmapId)
        {
            try
            {
                SetStatus("Завантаження шаблонів...");
                _templates = await _client.GetGeoRoadmapTemplatesAsync();
                TemplateComboBox.ItemsSource = _templates;
                TemplateComboBox.DisplayMemberPath = "Name";

                if (roadmapId.HasValue)
                {
                    SetStatus("Завантаження геокарти...");
                    _currentRoadmap = await _client.GetGeoRoadmapByIdAsync(roadmapId.Value);
                    LoadRoadmap();
                }
                else
                {
                    SetStatus("Створення нової геокарти...");
                    await ShowNewRoadmapDialogAsync();
                }

                // Ініціалізуємо WebView2 карту
                await InitializeMapAsync();

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка ініціалізації: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private async Task InitializeMapAsync()
        {
            try
            {
                MapLoadingStatus.Text = "Ініціалізація WebView2...";

                // Створюємо WebView2
                _mapWebView = new WebView2();

                // Налаштовуємо оточення WebView2
                var env = await CoreWebView2Environment.CreateAsync(null,
                    Path.Combine(Path.GetTempPath(), "DocControlWebView2"));

                await _mapWebView.EnsureCoreWebView2Async(env);

                // Налаштування
                _mapWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                _mapWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _mapWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                MapLoadingStatus.Text = "Завантаження карти...";

                // Завантажуємо HTML карту
                var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Maps", "map.html");

                if (!File.Exists(htmlPath))
                {
                    throw new FileNotFoundException($"Файл карти не знайдено: {htmlPath}");
                }

                _mapWebView.Source = new Uri(htmlPath);

                // Слухаємо повідомлення з JavaScript
                _mapWebView.WebMessageReceived += MapWebView_WebMessageReceived;

                // Заміняємо loading panel на WebView2
                MapContainer.Child = _mapWebView;

                _isMapInitialized = true;
                SetStatus("Карта завантажена");
            }
            catch (Exception ex)
            {
                MapLoadingStatus.Text = $"Помилка: {ex.Message}";
                MessageBox.Show(
                    $"Не вдалося завантажити карту:\n\n{ex.Message}\n\n" +
                    "Переконайтеся що:\n" +
                    "1. WebView2 Runtime встановлений\n" +
                    "2. Файли Maps/* існують\n" +
                    "3. Файли копіюються в вихідну директорію",
                    "Помилка карти",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task ShowNewRoadmapDialogAsync()
        {
            try
            {
                SetStatus("Завантаження списку директорій...");

                // 1. Використовуємо метод, який точно існує (як в MainWindow)
                var directories = await _client.GetDirectoriesAsync();

                if (directories == null || !directories.Any())
                {
                    MessageBox.Show("Не знайдено доступних директорій. Спочатку додайте директорію в головному вікні.",
                        "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                    return;
                }

                // 2. Передаємо реальний список у діалог
                var dialog = new NewGeoRoadmapDialog(directories);

                if (dialog.ShowDialog() == true)
                {
                    _currentRoadmap = new GeoRoadmap
                    {
                        DirectoryId = dialog.SelectedDirectoryId,
                        Name = dialog.RoadmapName,
                        Description = dialog.RoadmapDescription,
                        MapProvider = MapProvider.OpenStreetMap,
                        CenterLatitude = 50.4501,
                        CenterLongitude = 30.5234,
                        ZoomLevel = 10,
                        CreatedAt = DateTime.Now
                    };

                    LoadRoadmap();
                }
                else
                {
                    // Якщо користувач скасував створення - закриваємо вікно редактора
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка отримання списку директорій: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void LoadRoadmap()
        {
            if (_currentRoadmap == null) return;

            RoadmapTitle.Text = $"📍 {_currentRoadmap.Name}";
            Title = $"Редактор геокарт - {_currentRoadmap.Name}";

            MapNameTextBox.Text = _currentRoadmap.Name;
            MapDescriptionTextBox.Text = _currentRoadmap.Description;
            CenterLatTextBox.Text = _currentRoadmap.CenterLatitude.ToString("F6");
            CenterLngTextBox.Text = _currentRoadmap.CenterLongitude.ToString("F6");
            ZoomSlider.Value = _currentRoadmap.ZoomLevel;

            _nodes = _currentRoadmap.Nodes ?? new List<GeoRoadmapNode>();
            _routes = _currentRoadmap.Routes ?? new List<GeoRoadmapRoute>();
            _areas = _currentRoadmap.Areas ?? new List<GeoRoadmapArea>();

            RefreshUI();
        }

        #endregion

        #region WebView2 Communication

        private void MapWebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.TryGetWebMessageAsString();
                var message = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (!message.ContainsKey("type")) return;

                var type = message["type"].GetString();

                switch (type)
                {
                    case "mapReady":
                        OnMapReady();
                        break;

                    case "nodeClicked":
                        var nodeId = message["nodeId"].GetInt32();
                        HandleNodeClick(nodeId);
                        break;

                    case "mapClicked":
                        var lat = message["lat"].GetDouble();
                        var lng = message["lng"].GetDouble();
                        HandleMapClick(lat, lng);
                        break;

                    case "nodeMoved":
                        var movedNodeId = message["nodeId"].GetInt32();
                        var newLat = message["lat"].GetDouble();
                        var newLng = message["lng"].GetDouble();
                        HandleNodeMoved(movedNodeId, newLat, newLng);
                        break;

                    case "mapMoved":
                        var centerLat = message["lat"].GetDouble();
                        var centerLng = message["lng"].GetDouble();
                        CoordinatesText.Text = $"Lat: {centerLat:F6}, Lng: {centerLng:F6}";
                        break;

                    case "zoomChanged":
                        var zoom = message["zoom"].GetInt32();
                        ZoomSlider.Value = zoom;
                        break;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка обробки повідомлення: {ex.Message}");
            }
        }

        private async void OnMapReady()
        {
            SetStatus("Карта готова, завантаження об'єктів...");

            // Встановлюємо провайдер
            await ChangeMapProvider(_currentRoadmap.MapProvider.ToString());

            // Встановлюємо центр
            await SetMapCenter(_currentRoadmap.CenterLatitude, _currentRoadmap.CenterLongitude, _currentRoadmap.ZoomLevel);

            // Завантажуємо всі об'єкти
            await RenderMap();

            SetStatus("Готово");
        }

        private async Task SendMessageToMap(object message)
        {
            if (!_isMapInitialized || _mapWebView?.CoreWebView2 == null) return;

            try
            {
                var json = JsonSerializer.Serialize(message);
                await _mapWebView.CoreWebView2.ExecuteScriptAsync($"window.postMessage({json}, '*')");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка відправки повідомлення: {ex.Message}");
            }
        }

        #endregion

        #region Map Operations

        private async Task AddNodeToMap(GeoRoadmapNode node)
        {
            var tempId = node.Id != 0 ? node.Id : 1000 + _nodes.IndexOf(node);

            await SendMessageToMap(new
            {
                action = "addNode",
                data = new
                {
                    id = tempId,
                    lat = node.Latitude,
                    lng = node.Longitude,
                    title = node.Title,
                    description = node.Description ?? "",
                    type = node.Type.ToString(),
                    color = node.Color ?? "#2196F3"
                }
            });
        }

        private async Task UpdateNodeOnMap(GeoRoadmapNode node)
        {
            await SendMessageToMap(new
            {
                action = "updateNode",
                data = new
                {
                    id = node.Id,
                    lat = node.Latitude,
                    lng = node.Longitude,
                    title = node.Title,
                    description = node.Description ?? "",
                    type = node.Type.ToString(),
                    color = node.Color ?? "#2196F3"
                }
            });
        }

        private async Task RemoveNodeFromMap(int nodeId)
        {
            await SendMessageToMap(new
            {
                action = "removeNode",
                data = new { id = nodeId }
            });
        }

        private async Task AddRouteToMap(GeoRoadmapRoute route)
        {
            var fromNode = _nodes.FirstOrDefault(n => n.Id == route.FromNodeId);
            var toNode = _nodes.FirstOrDefault(n => n.Id == route.ToNodeId);

            if (fromNode == null || toNode == null) return;

            var fromId = fromNode.Id != 0 ? fromNode.Id : 1000 + _nodes.IndexOf(fromNode);
            var toId = toNode.Id != 0 ? toNode.Id : 1000 + _nodes.IndexOf(toNode);

            await SendMessageToMap(new
            {
                action = "addRoute",
                data = new
                {
                    id = route.Id != 0 ? route.Id : 2000 + _routes.IndexOf(route),
                    fromId,
                    toId,
                    color = route.Color ?? "#2196F3",
                    width = route.StrokeWidth,
                    style = route.Style.ToString(),
                    label = route.Label ?? ""
                }
            });
        }

        private async Task SetMapCenter(double lat, double lng, int zoom)
        {
            await SendMessageToMap(new
            {
                action = "setCenter",
                data = new { lat, lng, zoom }
            });
        }

        private async Task ChangeMapProvider(string provider)
        {
            await SendMessageToMap(new
            {
                action = "changeProvider",
                data = new { provider }
            });
        }

        private async Task ClearMap()
        {
            await SendMessageToMap(new { action = "clearAll" });
        }

        private async Task RenderMap()
        {
            if (!_isMapInitialized) return;

            await ClearMap();

            foreach (var node in _nodes)
            {
                await AddNodeToMap(node);
            }

            foreach (var route in _routes)
            {
                await AddRouteToMap(route);
            }
        }

        #endregion

        #region Event Handlers

        private void HandleNodeClick(int nodeId)
        {
            var node = _nodes.FirstOrDefault(n =>
                (n.Id != 0 && n.Id == nodeId) ||
                (n.Id == 0 && 1000 + _nodes.IndexOf(n) == nodeId));

            if (node != null)
            {
                if (_isConnectingNodes)
                {
                    if (_connectFromNode == null)
                    {
                        _connectFromNode = node;
                        SetStatus($"Перша точка: {node.Title}. Виберіть другу точку.");
                    }
                    else
                    {
                        CreateRouteBetweenNodes(_connectFromNode, node);
                        _connectFromNode = null;
                        _isConnectingNodes = false;
                        MapModeText.Text = "Режим: Перегляд";
                    }
                }
                else
                {
                    _selectedNode = node;
                    NodesListBox.SelectedItem = node;
                    ShowNodeProperties(node);
                }
            }
        }

        private async void HandleMapClick(double lat, double lng)
        {
            if (_isAddingNode)
            {
                var dialog = new NodeEditDialog(null);
                if (dialog.ShowDialog() == true)
                {
                    var newNode = new GeoRoadmapNode
                    {
                        GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                        Title = dialog.NodeTitle,
                        Description = dialog.NodeDescription,
                        Latitude = lat,
                        Longitude = lng,
                        Type = dialog.SelectedNodeType,
                        Color = dialog.SelectedColor,
                        OrderIndex = _nodes.Count
                    };

                    _nodes.Add(newNode);
                    await AddNodeToMap(newNode);
                    RefreshUI();

                    _isAddingNode = false;
                    MapModeText.Text = "Режим: Перегляд";
                    SetStatus($"Додано точку: {newNode.Title}");
                }
            }
        }

        private void HandleNodeMoved(int nodeId, double newLat, double newLng)
        {
            var node = _nodes.FirstOrDefault(n =>
                (n.Id != 0 && n.Id == nodeId) ||
                (n.Id == 0 && 1000 + _nodes.IndexOf(n) == nodeId));

            if (node != null)
            {
                node.Latitude = newLat;
                node.Longitude = newLng;

                if (_selectedNode == node)
                {
                    NodeLatTextBox.Text = newLat.ToString("F6");
                    NodeLngTextBox.Text = newLng.ToString("F6");
                }

                SetStatus($"Точку {node.Title} переміщено");
            }
        }

        private async void CreateRouteBetweenNodes(GeoRoadmapNode from, GeoRoadmapNode to)
        {
            var route = new GeoRoadmapRoute
            {
                GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                FromNodeId = from.Id != 0 ? from.Id : 1000 + _nodes.IndexOf(from),
                ToNodeId = to.Id != 0 ? to.Id : 1000 + _nodes.IndexOf(to),
                Color = "#2196F3",
                Style = RouteStyle.Solid,
                StrokeWidth = 2
            };

            _routes.Add(route);
            await AddRouteToMap(route);
            RefreshUI();

            SetStatus($"Створено маршрут: {from.Title} → {to.Title}");
        }

        #endregion

        #region UI Updates

        private void RefreshUI()
        {
            NodesListBox.ItemsSource = null;
            NodesListBox.ItemsSource = _nodes;

            NodeCountText.Text = _nodes.Count.ToString();
            RouteCountText.Text = _routes.Count.ToString();
            AreaCountText.Text = _areas.Count.ToString();

            double totalDistance = CalculateTotalDistance();
            TotalDistanceText.Text = $"{totalDistance:F2} км";
        }

        private void ShowNodeProperties(GeoRoadmapNode node)
        {
            NodePropertiesGroup.Visibility = Visibility.Visible;
            NodeTitleTextBox.Text = node.Title;
            NodeDescriptionTextBox.Text = node.Description;
            NodeTypeComboBox.Text = node.Type.ToString();
            NodeLatTextBox.Text = node.Latitude.ToString("F6");
            NodeLngTextBox.Text = node.Longitude.ToString("F6");
            NodeAddressTextBox.Text = node.Address;

            foreach (ComboBoxItem item in NodeColorComboBox.Items)
            {
                if (item.Tag?.ToString() == node.Color)
                {
                    NodeColorComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private double CalculateTotalDistance()
        {
            double total = 0;
            foreach (var route in _routes)
            {
                var fromNode = _nodes.FirstOrDefault(n => n.Id == route.FromNodeId);
                var toNode = _nodes.FirstOrDefault(n => n.Id == route.ToNodeId);

                if (fromNode != null && toNode != null)
                {
                    total += CalculateDistance(
                        fromNode.Latitude, fromNode.Longitude,
                        toNode.Latitude, toNode.Longitude);
                }
            }
            return total;
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double degrees) => degrees * Math.PI / 180.0;

        private void SetStatus(string message)
        {
            StatusText.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
        }

        #endregion

        #region Button Handlers

        private async void SaveRoadmap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("Збереження геокарти...");

                _currentRoadmap.Name = MapNameTextBox.Text;
                _currentRoadmap.Description = MapDescriptionTextBox.Text;
                _currentRoadmap.Nodes = _nodes;
                _currentRoadmap.Routes = _routes;
                _currentRoadmap.Areas = _areas;

                if (_currentRoadmap.Id == 0)
                {
                    var request = new CreateGeoRoadmapRequest
                    {
                        DirectoryId = _currentRoadmap.DirectoryId,
                        Name = _currentRoadmap.Name,
                        Description = _currentRoadmap.Description,
                        MapProvider = _currentRoadmap.MapProvider,
                        CenterLatitude = _currentRoadmap.CenterLatitude,
                        CenterLongitude = _currentRoadmap.CenterLongitude,
                        ZoomLevel = _currentRoadmap.ZoomLevel
                    };

                    _currentRoadmap.Id = await _client.CreateGeoRoadmapAsync(request);

                    foreach (var node in _nodes)
                    {
                        node.GeoRoadmapId = _currentRoadmap.Id;
                        node.Id = await _client.AddGeoNodeAsync(node);
                    }

                    foreach (var route in _routes)
                    {
                        route.GeoRoadmapId = _currentRoadmap.Id;
                        route.Id = await _client.AddGeoRouteAsync(route);
                    }
                }
                else
                {
                    await _client.UpdateGeoRoadmapAsync(_currentRoadmap);
                }

                SetStatus("Геокарту збережено");
                MessageBox.Show("Геокарту успішно збережено!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка збереження: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddNode_Click(object sender, RoutedEventArgs e)
        {
            _isAddingNode = true;
            _isConnectingNodes = false;
            MapModeText.Text = "Режим: Додавання точки (клікніть на карті)";
            SetStatus("Клікніть на карті для додавання точки");
        }

        private void ConnectNodes_Click(object sender, RoutedEventArgs e)
        {
            _isConnectingNodes = true;
            _isAddingNode = false;
            _connectFromNode = null;
            MapModeText.Text = "Режим: З'єднання точок (виберіть дві точки)";
            SetStatus("Виберіть першу точку для з'єднання");
        }

        private async void AddNodeFromPanel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NodeEditDialog(null);
            if (dialog.ShowDialog() == true)
            {
                var newNode = new GeoRoadmapNode
                {
                    GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                    Title = dialog.NodeTitle,
                    Description = dialog.NodeDescription,
                    Latitude = _currentRoadmap?.CenterLatitude ?? 50.4501,
                    Longitude = _currentRoadmap?.CenterLongitude ?? 30.5234,
                    Type = dialog.SelectedNodeType,
                    Color = dialog.SelectedColor,
                    OrderIndex = _nodes.Count
                };

                _nodes.Add(newNode);
                await AddNodeToMap(newNode);
                RefreshUI();
                SetStatus($"Додано точку: {newNode.Title}");
            }
        }

        private async void EditNode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null)
            {
                MessageBox.Show("Виберіть точку для редагування", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new NodeEditDialog(_selectedNode);
            if (dialog.ShowDialog() == true)
            {
                _selectedNode.Title = dialog.NodeTitle;
                _selectedNode.Description = dialog.NodeDescription;
                _selectedNode.Type = dialog.SelectedNodeType;
                _selectedNode.Color = dialog.SelectedColor;

                await UpdateNodeOnMap(_selectedNode);
                RefreshUI();
                SetStatus($"Оновлено точку: {_selectedNode.Title}");
            }
        }

        private async void DeleteNode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null)
            {
                MessageBox.Show("Виберіть точку для видалення", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Видалити точку '{_selectedNode.Title}'?\n\nБудуть також видалені всі пов'язані маршрути.",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var nodeId = _selectedNode.Id != 0 ? _selectedNode.Id : 1000 + _nodes.IndexOf(_selectedNode);
                await RemoveNodeFromMap(nodeId);

                _routes.RemoveAll(r => r.FromNodeId == nodeId || r.ToNodeId == nodeId);
                _nodes.Remove(_selectedNode);
                _selectedNode = null;
                NodePropertiesGroup.Visibility = Visibility.Collapsed;

                await RenderMap();
                RefreshUI();
                SetStatus("Точку видалено");
            }
        }

        private async void Geocode_Click(object sender, RoutedEventArgs e)
        {
            var address = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть адресу для пошуку:",
                "Геокодування",
                "");

            if (!string.IsNullOrWhiteSpace(address))
            {
                try
                {
                    SetStatus("Пошук адреси...");
                    var result = await _client.GeocodeAddressAsync(address);

                    if (result.Success)
                    {
                        await SetMapCenter(result.Latitude, result.Longitude, 15);

                        var addResult = MessageBox.Show(
                            $"Знайдено:\n\n{result.FormattedAddress}\n\n" +
                            $"Lat: {result.Latitude:F6}\nLng: {result.Longitude:F6}\n\n" +
                            "Додати точку на цю адресу?",
                            "Геокодування",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (addResult == MessageBoxResult.Yes)
                        {
                            var dialog = new NodeEditDialog(null);
                            if (dialog.ShowDialog() == true)
                            {
                                var newNode = new GeoRoadmapNode
                                {
                                    GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                                    Title = dialog.NodeTitle,
                                    Description = dialog.NodeDescription,
                                    Latitude = result.Latitude,
                                    Longitude = result.Longitude,
                                    Address = result.FormattedAddress,
                                    Type = dialog.SelectedNodeType,
                                    Color = dialog.SelectedColor,
                                    OrderIndex = _nodes.Count
                                };

                                _nodes.Add(newNode);
                                await AddNodeToMap(newNode);
                                RefreshUI();
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Адресу не знайдено", "Помилка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    SetStatus("Готово");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка геокодування: {ex.Message}", "Помилка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void CenterMap_Click(object sender, RoutedEventArgs e)
        {
            if (_nodes.Count > 0)
            {
                double avgLat = _nodes.Average(n => n.Latitude);
                double avgLng = _nodes.Average(n => n.Longitude);

                _currentRoadmap.CenterLatitude = avgLat;
                _currentRoadmap.CenterLongitude = avgLng;

                CenterLatTextBox.Text = avgLat.ToString("F6");
                CenterLngTextBox.Text = avgLng.ToString("F6");

                await SetMapCenter(avgLat, avgLng, 10);
                SetStatus("Карту відцентровано");
            }
        }

        private async void MapProvider_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (MapProviderComboBox.SelectedItem is ComboBoxItem item)
            {
                await ChangeMapProvider(item.Content.ToString());
            }
        }

        private async void Zoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentRoadmap != null && _isMapInitialized)
            {
                _currentRoadmap.ZoomLevel = (int)e.NewValue;
                await SetMapCenter(
                    _currentRoadmap.CenterLatitude,
                    _currentRoadmap.CenterLongitude,
                    _currentRoadmap.ZoomLevel
                );
            }
        }

        private async void ReloadMap_Click(object sender, RoutedEventArgs e)
        {
            if (_mapWebView?.CoreWebView2 != null)
            {
                _mapWebView.Reload();
                await Task.Delay(1000);
                await RenderMap();
                SetStatus("Карту перезавантажено");
            }
        }

        private void Node_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodesListBox.SelectedItem is GeoRoadmapNode node)
            {
                _selectedNode = node;
                ShowNodeProperties(node);
            }
        }

        private async void FindNodeOnMap_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode != null)
            {
                await SetMapCenter(_selectedNode.Latitude, _selectedNode.Longitude, 15);
                SetStatus($"Відцентровано на: {_selectedNode.Title}");
            }
        }

        private async void OptimizeRoute_Click(object sender, RoutedEventArgs e)
        {
            if (_nodes.Count < 3)
            {
                MessageBox.Show("Для оптимізації потрібно принаймні 3 точки",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var optimized = new List<GeoRoadmapNode> { _nodes[0] };
            var remaining = new List<GeoRoadmapNode>(_nodes.Skip(1));

            while (remaining.Count > 0)
            {
                var current = optimized.Last();
                GeoRoadmapNode nearest = null;
                double minDist = double.MaxValue;

                foreach (var node in remaining)
                {
                    var dist = CalculateDistance(
                        current.Latitude, current.Longitude,
                        node.Latitude, node.Longitude);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = node;
                    }
                }

                if (nearest != null)
                {
                    optimized.Add(nearest);
                    remaining.Remove(nearest);
                }
            }

            for (int i = 0; i < optimized.Count; i++)
            {
                optimized[i].OrderIndex = i;
            }

            _nodes = optimized;
            _routes.Clear();

            for (int i = 0; i < _nodes.Count - 1; i++)
            {
                _routes.Add(new GeoRoadmapRoute
                {
                    GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                    FromNodeId = _nodes[i].Id != 0 ? _nodes[i].Id : 1000 + i,
                    ToNodeId = _nodes[i + 1].Id != 0 ? _nodes[i + 1].Id : 1000 + i + 1,
                    Color = "#2196F3",
                    Style = RouteStyle.Solid,
                    StrokeWidth = 2
                });
            }

            await RenderMap();
            RefreshUI();
            MessageBox.Show("Маршрут оптимізовано!", "Успіх",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MeasureDistance_Click(object sender, RoutedEventArgs e)
        {
            if (_nodes.Count >= 2)
            {
                var distance = CalculateTotalDistance();
                MessageBox.Show(
                    $"Загальна відстань маршруту: {distance:F2} км",
                    "Вимірювання відстані",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async void AddArea_Click(object sender, RoutedEventArgs e)
        {
            // Вимикаємо інші режими
            _isAddingNode = false;
            _isConnectingNodes = false;
            _connectFromNode = null;

            // Вмикаємо режим додавання області
            MapModeText.Text = "Режим: Малювання області (клікніть для старту)";
            SetStatus("Почніть малювати область на карті");

            // Відправляємо команду в JavaScript
            await SendMessageToMap(new
            {
                action = "startDrawingArea",
                data = new
                {
                    // Тут можна задати ID або колір за замовчуванням
                    fillColor = "#2196F3",
                    strokeColor = "#1976D2"
                }
            });
        }

        private async void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть назву шаблону:",
                "Зберегти як шаблон",
                "");

            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    await _client.SaveAsTemplateAsync(
                        _currentRoadmap.Id, name, "Користувацький шаблон", "Користувацькі");

                    MessageBox.Show("Шаблон збережено!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    _templates = await _client.GetGeoRoadmapTemplatesAsync();
                    TemplateComboBox.ItemsSource = _templates;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка збереження: {ex.Message}", "Помилка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Template_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private async void ApplyTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateComboBox.SelectedItem is not GeoRoadmapTemplate template)
            {
                MessageBox.Show("Будь ласка, виберіть шаблон для застосування.", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(template.TemplateJson))
            {
                MessageBox.Show("Обраний шаблон порожній і не містить даних.", "Помилка шаблону",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show(
                $"Застосувати шаблон '{template.Name}'?\n\n" +
                "Всі існуючі точки та маршрути на цій карті буде видалено.",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No) return;

            try
            {
                SetStatus($"Застосування шаблону: {template.Name}...");

                // Десеріалізуємо JSON шаблону в тимчасовий об'єкт GeoRoadmap
                // Використовуємо JsonSerializerOptions, щоб ігнорувати регістр властивостей
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var templateMap = JsonSerializer.Deserialize<GeoRoadmap>(template.TemplateJson, options);

                if (templateMap == null)
                {
                    throw new Exception("Не вдалося розпізнати дані шаблону.");
                }

                // Очищуємо поточну карту
                _nodes.Clear();
                _routes.Clear();
                _areas.Clear();
                await ClearMap(); // Очищує карту в WebView2

                // Копіюємо налаштування з шаблону (окрім назви та опису)
                _currentRoadmap.CenterLatitude = templateMap.CenterLatitude;
                _currentRoadmap.CenterLongitude = templateMap.CenterLongitude;
                _currentRoadmap.ZoomLevel = templateMap.ZoomLevel;
                _currentRoadmap.MapProvider = templateMap.MapProvider;

                // Додаємо нові вузли з шаблону
                if (templateMap.Nodes != null)
                {
                    foreach (var node in templateMap.Nodes)
                    {
                        node.Id = 0; // Скидаємо ID, щоб вони були створені як нові
                        node.GeoRoadmapId = _currentRoadmap?.Id ?? 0;
                        _nodes.Add(node);
                    }
                }

                // Додаємо нові маршрути з шаблону
                if (templateMap.Routes != null)
                {
                    foreach (var route in templateMap.Routes)
                    {
                        route.Id = 0; // Скидаємо ID
                        route.GeoRoadmapId = _currentRoadmap?.Id ?? 0;
                        _routes.Add(route);
                    }
                }

                // Оновлюємо UI та перемальовуємо карту
                await RenderMap();
                RefreshUI();

                // Оновлюємо поля налаштувань карти в UI
                MapNameTextBox.Text = _currentRoadmap.Name;
                MapDescriptionTextBox.Text = _currentRoadmap.Description;
                CenterLatTextBox.Text = _currentRoadmap.CenterLatitude.ToString("F6");
                CenterLngTextBox.Text = _currentRoadmap.CenterLongitude.ToString("F6");
                ZoomSlider.Value = _currentRoadmap.ZoomLevel;

                SetStatus("Шаблон успішно застосовано.");
            }
            catch (Exception ex)
            {
                SetStatus($"Помилка застосування шаблону: {ex.Message}");
                MessageBox.Show($"Помилка застосування шаблону: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"georoadmap_{_currentRoadmap?.Name}_{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_currentRoadmap,
                        new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Геокарту експортовано!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка експорту: {ex.Message}", "Помилка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Зберегти зміни перед закриттям?",
                "Підтвердження",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveRoadmap_Click(sender, e);
                Close();
            }
            else if (result == MessageBoxResult.No)
            {
                Close();
            }
        }

        #endregion
    }

    #region Helper Dialogs

    public class NewGeoRoadmapDialog : Window
    {
        private TextBox nameTextBox;
        private TextBox descriptionTextBox;
        private ComboBox directoryComboBox;

        public string RoadmapName => nameTextBox.Text;
        public string RoadmapDescription => descriptionTextBox.Text;

        // Беремо реальний ID вибраної директорії
        public int SelectedDirectoryId => (int)directoryComboBox.SelectedValue;

        // Конструктор тепер приймає список моделей (як в MainWindow)
        public NewGeoRoadmapDialog(IEnumerable<DirectoryWithAccessModel> directories)
        {
            Title = "Нова геодорожня карта";
            Width = 500;
            Height = 350; // Трохи збільшив висоту
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 1. Назва
            var nameLabel = new TextBlock { Text = "Назва карти:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            nameTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(nameTextBox, 0);
            grid.Children.Add(nameTextBox);

            // 2. Опис
            var descLabel = new TextBlock { Text = "Опис:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(descLabel, 1);
            grid.Children.Add(descLabel);

            descriptionTextBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                Height = 60,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true
            };
            Grid.SetRow(descriptionTextBox, 1);
            grid.Children.Add(descriptionTextBox);

            // 3. Директорія (Випадаючий список)
            var dirLabel = new TextBlock { Text = "Прив'язати до директорії:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(dirLabel, 2);
            grid.Children.Add(dirLabel);

            directoryComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                ItemsSource = directories,      // Прив'язуємо реальні дані
                DisplayMemberPath = "Name",     // Показуємо назву
                SelectedValuePath = "Id"        // Зберігаємо ID
            };

            // Вибираємо перший елемент, щоб не було пусто
            if (directories.Any())
            {
                directoryComboBox.SelectedIndex = 0;
            }

            Grid.SetRow(directoryComboBox, 2);
            grid.Children.Add(directoryComboBox);

            // 4. Кнопки
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Grid.SetRow(buttonPanel, 4);

            var okButton = new Button
            {
                Content = "Створити",
                Width = 100,
                Margin = new Thickness(5),
                IsDefault = true
            };

            // Валідація
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(nameTextBox.Text))
                {
                    MessageBox.Show("Будь ласка, введіть назву карти.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (directoryComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Будь ласка, виберіть директорію.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                DialogResult = true;
                Close();
            };

            var cancelButton = new Button
            {
                Content = "Скасувати",
                Width = 100,
                Margin = new Thickness(5),
                IsCancel = true
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    public class NodeEditDialog : Window
    {
        private TextBox titleTextBox;
        private TextBox descriptionTextBox;
        private ComboBox typeComboBox;
        private ComboBox colorComboBox;

        public string NodeTitle => titleTextBox.Text;
        public string NodeDescription => descriptionTextBox.Text;
        public NodeType SelectedNodeType => Enum.Parse<NodeType>(typeComboBox.Text);
        public string SelectedColor => (colorComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "#2196F3";

        public NodeEditDialog(GeoRoadmapNode existingNode)
        {
            Title = existingNode == null ? "Нова точка" : "Редагування точки";
            Width = 400;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleLabel = new TextBlock { Text = "Назва точки:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(titleLabel, 0);
            grid.Children.Add(titleLabel);

            titleTextBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                Text = existingNode?.Title ?? ""
            };
            Grid.SetRow(titleTextBox, 0);
            grid.Children.Add(titleTextBox);

            var descLabel = new TextBlock { Text = "Опис:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(descLabel, 1);
            grid.Children.Add(descLabel);

            descriptionTextBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10),
                Height = 60,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Text = existingNode?.Description ?? ""
            };
            Grid.SetRow(descriptionTextBox, 1);
            grid.Children.Add(descriptionTextBox);

            var typeLabel = new TextBlock { Text = "Тип:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(typeLabel, 2);
            grid.Children.Add(typeLabel);

            typeComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var type in Enum.GetValues(typeof(NodeType)))
            {
                typeComboBox.Items.Add(type.ToString());
            }
            typeComboBox.SelectedIndex = 0;
            if (existingNode != null)
                typeComboBox.Text = existingNode.Type.ToString();
            Grid.SetRow(typeComboBox, 2);
            grid.Children.Add(typeComboBox);

            var colorLabel = new TextBlock { Text = "Колір:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(colorLabel, 3);
            grid.Children.Add(colorLabel);

            colorComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🔵 Синій", Tag = "#2196F3" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🔴 Червоний", Tag = "#F44336" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟢 Зелений", Tag = "#4CAF50" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟡 Жовтий", Tag = "#FFEB3B" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟣 Фіолетовий", Tag = "#9C27B0" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟠 Помаранчевий", Tag = "#FF9800" });
            colorComboBox.SelectedIndex = 0;
            Grid.SetRow(colorComboBox, 3);
            grid.Children.Add(colorComboBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Grid.SetRow(buttonPanel, 5);

            var okButton = new Button
            {
                Content = "OK",
                Width = 100,
                Margin = new Thickness(5),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                {
                    MessageBox.Show("Введіть назву точки", "Увага",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
                Close();
            };

            var cancelButton = new Button
            {
                Content = "Скасувати",
                Width = 100,
                Margin = new Thickness(5),
                IsCancel = true
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    #endregion
}