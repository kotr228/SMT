using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DocControlUI.Helpers
{
    /// <summary>
    /// Вкладка з можливістю закриття
    /// </summary>
    public class ClosableTabItem : TabItem
    {
        /// <summary>
        /// Чи можна закрити цю вкладку (false для постійних вкладок)
        /// </summary>
        public bool IsClosable { get; set; } = true;

        /// <summary>
        /// Подія закриття вкладки
        /// </summary>
        public event RoutedEventHandler CloseRequested;

        public ClosableTabItem()
        {
            // Налаштуємо header з кнопкою закриття
            HeaderTemplate = CreateHeaderTemplate();
        }

        private DataTemplate CreateHeaderTemplate()
        {
            // Створюємо template програмно
            var factory = new FrameworkElementFactory(typeof(DockPanel));
            factory.SetValue(DockPanel.LastChildFillProperty, true);

            // Іконка та текст
            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Header") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            textFactory.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 10, 0));
            textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.AppendChild(textFactory);

            // Кнопка закриття
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(Button.ContentProperty, "✕");
            buttonFactory.SetValue(Button.WidthProperty, 20.0);
            buttonFactory.SetValue(Button.HeightProperty, 20.0);
            buttonFactory.SetValue(Button.PaddingProperty, new Thickness(0));
            buttonFactory.SetValue(Button.MarginProperty, new Thickness(5, 0, 0, 0));
            buttonFactory.SetValue(Button.BackgroundProperty, Brushes.Transparent);
            buttonFactory.SetValue(Button.BorderThicknessProperty, new Thickness(0));
            buttonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
            buttonFactory.SetValue(Button.FontSizeProperty, 14.0);
            buttonFactory.SetValue(Button.CursorProperty, Cursors.Hand);
            buttonFactory.SetValue(Button.ToolTipProperty, "Закрити вкладку");
            buttonFactory.SetValue(DockPanel.DockProperty, Dock.Right);

            // Додаємо visibility binding для IsClosable
            var visibilityFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetValue(Button.VisibilityProperty, Visibility.Visible);

            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(CloseButton_Click));

            factory.AppendChild(buttonFactory);

            var template = new DataTemplate { VisualTree = factory };
            return template;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Запобігти активації вкладки при кліку на закриття

            if (IsClosable)
            {
                CloseRequested?.Invoke(this, e);
            }
        }

        /// <summary>
        /// Встановити header з іконкою
        /// </summary>
        public void SetHeaderWithIcon(string icon, string text)
        {
            Header = $"{icon} {text}";
        }
    }
}
