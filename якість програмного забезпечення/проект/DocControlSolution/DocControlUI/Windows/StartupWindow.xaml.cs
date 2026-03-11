using DocControlService.Client;
using MahApps.Metro.Controls;
using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;

namespace DocControlUI.Windows
{
    public partial class StartupWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private bool _serviceRunning = false;
        private bool _networkRunning = false;

        public StartupWindow()
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            Loaded += StartupWindow_Loaded;
        }

        private async void StartupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckSystemStatus();
        }

        /// <summary>
        /// Перевірка стану системи
        /// </summary>
        private async Task CheckSystemStatus()
        {
            try
            {
                StatusMessageText.Text = "Перевірка Windows Service...";

                // Крок 1: Перевірка Windows Service
                _serviceRunning = await CheckWindowsService();

                if (_serviceRunning)
                {
                    ServiceStatusText.Text = "✅ Запущено та працює";
                    ServiceStatusIcon.Text = "✅";
                    ServiceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                }
                else
                {
                    ServiceStatusText.Text = "❌ Не запущено або недоступно";
                    ServiceStatusIcon.Text = "❌";
                    ServiceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                }

                await Task.Delay(500); // Пауза для візуального ефекту

                // Крок 2: Перевірка Network Core (тільки якщо сервіс запущений)
                StatusMessageText.Text = "Перевірка Network Core...";

                if (_serviceRunning)
                {
                    _networkRunning = await CheckNetworkCore();

                    if (_networkRunning)
                    {
                        NetworkStatusText.Text = "✅ Запущено";
                        NetworkStatusIcon.Text = "✅";
                        NetworkStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                    }
                    else
                    {
                        NetworkStatusText.Text = "⚠️ Не запущено (буде запущено автоматично)";
                        NetworkStatusIcon.Text = "⚠️";
                        NetworkStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                    }
                }
                else
                {
                    NetworkStatusText.Text = "❌ Недоступно (потребує Windows Service)";
                    NetworkStatusIcon.Text = "❌";
                    NetworkStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                }

                // Крок 3: Завершення перевірки
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Visibility = Visibility.Collapsed;

                // Показати відповідні кнопки
                if (_serviceRunning)
                {
                    StatusMessageText.Text = "✅ Система готова до роботи!";
                    StatusMessageText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                    ContinueButton.Visibility = Visibility.Visible;
                }
                else
                {
                    StatusMessageText.Text = "❌ Windows Service не запущено";
                    StatusMessageText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);

                    // Показати повідомлення про помилку
                    ShowError("Windows Service не запущено.\n\n" +
                             "Варіанти:\n" +
                             "1. Запустити сервіс (потрібні права адміністратора)\n" +
                             "2. Працювати в UI режимі (без локальних функцій)\n" +
                             "3. Запустити вручну через Services.msc");

                    StartServiceButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                LoadingProgress.IsIndeterminate = false;
                LoadingProgress.Visibility = Visibility.Collapsed;
                StatusMessageText.Text = "❌ Помилка перевірки системи";
                ShowError($"Помилка ініціалізації:\n{ex.Message}");
            }
        }

        /// <summary>
        /// Перевірка стану Windows Service
        /// </summary>
        private async Task<bool> CheckWindowsService()
        {
            try
            {
                // Спроба підключитися до сервісу через Named Pipe
                bool available = await _client.IsServiceAvailableAsync();
                return available;
            }
            catch
            {
                // Якщо не вдалося підключитися - перевіряємо через ServiceController
                try
                {
                    using (var sc = new ServiceController("DocControlService"))
                    {
                        return sc.Status == ServiceControllerStatus.Running;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Перевірка стану Network Core
        /// </summary>
        private async Task<bool> CheckNetworkCore()
        {
            try
            {
                var (isRunning, _) = await _client.GetNetworkCoreStatusAsync();
                return isRunning;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Запустити Windows Service
        /// </summary>
        private async void StartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartServiceButton.IsEnabled = false;
                StatusMessageText.Text = "Спроба запуску Windows Service...";
                LoadingProgress.Visibility = Visibility.Visible;
                LoadingProgress.IsIndeterminate = true;

                // Перевірити чи програма запущена з правами адміністратора
                if (!IsRunAsAdmin())
                {
                    var result = MessageBox.Show(
                        "Для запуску Windows Service потрібні права адміністратора.\n\n" +
                        "Перезапустити програму від імені адміністратора?",
                        "Права адміністратора",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        RestartAsAdmin();
                        Application.Current.Shutdown();
                        return;
                    }
                    else
                    {
                        StartServiceButton.IsEnabled = true;
                        LoadingProgress.Visibility = Visibility.Collapsed;
                        StatusMessageText.Text = "❌ Скасовано користувачем";
                        return;
                    }
                }

                // Спроба запустити сервіс
                using (var sc = new ServiceController("DocControlService"))
                {
                    if (sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                }

                await Task.Delay(2000); // Дати сервісу час запуститися

                // Повторна перевірка
                await CheckSystemStatus();
            }
            catch (Exception ex)
            {
                LoadingProgress.Visibility = Visibility.Collapsed;
                StartServiceButton.IsEnabled = true;
                ShowError($"Помилка запуску сервісу:\n{ex.Message}\n\n" +
                         "Спробуйте запустити вручну через Services.msc або " +
                         "запустіть програму від імені адміністратора.");
            }
        }

        /// <summary>
        /// Продовжити до головного вікна
        /// </summary>
        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            OpenMainWindow();
        }

        /// <summary>
        /// Запустити в UI режимі (без Windows Service)
        /// </summary>
        private void UIOnly_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "UI режим дозволяє працювати тільки з мережевою частиною без локальних функцій.\n\n" +
                "Недоступні функції:\n" +
                "• Локальні директорії\n" +
                "• Автоматичне версіонування\n" +
                "• AI аналіз\n\n" +
                "Продовжити?",
                "UI режим",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                OpenMainWindow();
            }
        }

        /// <summary>
        /// Вихід з програми
        /// </summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Закрити вікно (хрестик)
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Відкрити головне вікно
        /// </summary>
        private void OpenMainWindow()
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        /// <summary>
        /// Показати повідомлення про помилку
        /// </summary>
        private void ShowError(string message)
        {
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorMessageText.Text = message;
        }

        /// <summary>
        /// Перевірити чи запущено з правами адміністратора
        /// </summary>
        private bool IsRunAsAdmin()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Перезапустити програму з правами адміністратора
        /// </summary>
        private void RestartAsAdmin()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = true,
                    Verb = "runas" // Запустити від імені адміністратора
                };

                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося перезапустити з правами адміністратора:\n{ex.Message}",
                    "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
