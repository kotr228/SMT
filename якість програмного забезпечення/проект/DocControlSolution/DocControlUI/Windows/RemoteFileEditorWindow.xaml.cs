using DocControlService.Client;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace DocControlUI.Windows
{
    public partial class RemoteFileEditorWindow : MetroWindow
    {
        private readonly DocControlServiceClient _client;
        private readonly string _deviceName;
        private readonly string _filePath;
        private string _originalContent;
        private bool _isModified;

        public RemoteFileEditorWindow(string deviceName, string filePath)
        {
            InitializeComponent();
            _client = new DocControlServiceClient();
            _deviceName = deviceName;
            _filePath = filePath;
            _isModified = false;

            FileNameText.Text = Path.GetFileName(filePath);
            FilePathText.Text = filePath;
            Title = $"📝 {Path.GetFileName(filePath)} - {deviceName}";

            Loaded += RemoteFileEditorWindow_Loaded;
            Closing += RemoteFileEditorWindow_Closing;
        }

        private async void RemoteFileEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadFileContent();
        }

        private async void RemoteFileEditorWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isModified)
            {
                var result = await this.ShowMessageAsync("Незбережені зміни",
                    "Файл має незбережені зміни. Зберегти перед закриттям?",
                    MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary,
                    new MetroDialogSettings
                    {
                        AffirmativeButtonText = "Зберегти",
                        NegativeButtonText = "Не зберігати",
                        FirstAuxiliaryButtonText = "Скасувати"
                    });

                if (result == MessageDialogResult.Affirmative)
                {
                    e.Cancel = true;
                    await SaveFileContent();
                    Close();
                }
                else if (result == MessageDialogResult.FirstAuxiliary)
                {
                    e.Cancel = true;
                }
            }
        }

        private async Task LoadFileContent()
        {
            try
            {
                SetStatus("Завантаження...", Colors.Orange);

                var content = await _client.RemoteReadFileAsync(_deviceName, _filePath);

                ContentTextBox.Text = content;
                _originalContent = content;
                _isModified = false;

                UpdateInfo();
                SetStatus("Готово", Colors.Green);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteFileEditor] Помилка завантаження: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося завантажити файл:\n\n{ex.Message}");
                SetStatus("Помилка завантаження", Colors.Red);
            }
        }

        private async Task SaveFileContent()
        {
            try
            {
                SetStatus("Збереження...", Colors.Orange);

                await _client.RemoteWriteFileAsync(_deviceName, _filePath, ContentTextBox.Text);

                _originalContent = ContentTextBox.Text;
                _isModified = false;

                await this.ShowMessageAsync("Успіх", "Файл успішно збережено");
                SetStatus("Збережено", Colors.Green);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RemoteFileEditor] Помилка збереження: {ex.Message}");
                await this.ShowMessageAsync("Помилка", $"Не вдалося зберегти файл:\n\n{ex.Message}");
                SetStatus("Помилка збереження", Colors.Red);
            }
        }

        private void ContentTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_originalContent != null)
            {
                _isModified = ContentTextBox.Text != _originalContent;
                UpdateTitle();
                UpdateInfo();
            }
        }

        private void UpdateTitle()
        {
            string modified = _isModified ? " *" : "";
            Title = $"📝 {Path.GetFileName(_filePath)}{modified} - {_deviceName}";
        }

        private void UpdateInfo()
        {
            int lines = ContentTextBox.LineCount;
            int chars = ContentTextBox.Text.Length;
            InfoText.Text = $"Рядків: {lines} | Символів: {chars}";
        }

        private void SetStatus(string message, Color color)
        {
            StatusText.Text = message;
            StatusIndicator.Fill = new SolidColorBrush(color);
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveFileContent();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isModified)
            {
                var result = await this.ShowMessageAsync("Незбережені зміни",
                    "Файл має незбережені зміни. Відновити з віддаленого пристрою?",
                    MessageDialogStyle.AffirmativeAndNegative,
                    new MetroDialogSettings
                    {
                        AffirmativeButtonText = "Так, відновити",
                        NegativeButtonText = "Скасувати"
                    });

                if (result != MessageDialogResult.Affirmative)
                    return;
            }

            await LoadFileContent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
