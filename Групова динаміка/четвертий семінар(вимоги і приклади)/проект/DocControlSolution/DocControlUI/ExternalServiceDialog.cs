using DocControlService.Shared;
using System.Windows;
using System.Windows.Controls;

namespace DocControlUI
{
    /// <summary>
    /// Діалог для додавання/редагування зовнішнього сервісу
    /// </summary>
    public class ExternalServiceDialog : Window
    {
        private TextBox nameTextBox;
        private ComboBox typeComboBox;
        private TextBox urlTextBox;
        private TextBox apiKeyTextBox;

        public string ServiceName => nameTextBox.Text;
        public string ServiceType => typeComboBox.Text;
        public string ServiceUrl => urlTextBox.Text;
        public string ApiKey => apiKeyTextBox.Text;

        public ExternalServiceDialog(ExternalService existingService = null)
        {
            Title = existingService == null ? "Додати зовнішній сервіс" : "Редагувати сервіс";
            Width = 500;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Назва
            var nameLabel = new TextBlock { Text = "Назва сервісу:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            nameTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(nameTextBox, 0);
            Grid.SetColumn(nameTextBox, 1);
            grid.Children.Add(nameTextBox);

            // Тип
            var typeLabel = new TextBlock { Text = "Тип:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(typeLabel, 1);
            grid.Children.Add(typeLabel);

            typeComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            typeComboBox.Items.Add("api");
            typeComboBox.Items.Add("webhook");
            typeComboBox.Items.Add("ftp");
            typeComboBox.Items.Add("http");
            typeComboBox.SelectedIndex = 0;
            Grid.SetRow(typeComboBox, 1);
            Grid.SetColumn(typeComboBox, 1);
            grid.Children.Add(typeComboBox);

            // URL
            var urlLabel = new TextBlock { Text = "URL:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(urlLabel, 2);
            grid.Children.Add(urlLabel);

            urlTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(urlTextBox, 2);
            Grid.SetColumn(urlTextBox, 1);
            grid.Children.Add(urlTextBox);

            // API Key
            var apiKeyLabel = new TextBlock { Text = "API Key (опціонально):", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(apiKeyLabel, 3);
            grid.Children.Add(apiKeyLabel);

            apiKeyTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(apiKeyTextBox, 3);
            Grid.SetColumn(apiKeyTextBox, 1);
            grid.Children.Add(apiKeyTextBox);

            // Підказка
            var hintText = new TextBlock
            {
                Text = "💡 Підказка:\n" +
                       "• API - для REST API endpoints\n" +
                       "• Webhook - для HTTP POST callbacks\n" +
                       "• FTP - для FTP серверів\n" +
                       "• HTTP - для звичайних HTTP запитів",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(hintText, 5);
            Grid.SetColumnSpan(hintText, 2);
            grid.Children.Add(hintText);

            // Кнопки
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Grid.SetRow(buttonPanel, 6);
            Grid.SetColumnSpan(buttonPanel, 2);

            var okButton = new Button
            {
                Content = "Зберегти",
                Width = 100,
                Margin = new Thickness(5),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(nameTextBox.Text) ||
                    string.IsNullOrWhiteSpace(urlTextBox.Text))
                {
                    MessageBox.Show("Заповніть назву та URL", "Увага",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Перевірка URL
                if (!urlTextBox.Text.StartsWith("http://") && !urlTextBox.Text.StartsWith("https://") &&
                    !urlTextBox.Text.StartsWith("ftp://"))
                {
                    MessageBox.Show("URL має починатися з http://, https:// або ftp://", "Увага",
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
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            // Налаштування сітки
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Якщо редагуємо - заповнюємо поля
            if (existingService != null)
            {
                nameTextBox.Text = existingService.Name;
                typeComboBox.Text = existingService.ServiceType;
                urlTextBox.Text = existingService.Url;
                apiKeyTextBox.Text = existingService.ApiKey ?? "";
            }

            Content = grid;
        }
    }
}