using System;
using System.Windows;
using System.Windows.Controls;

namespace DocControlUI.Helpers
{
    /// <summary>
    /// UserControl для вбудовування вікон у вкладки
    /// </summary>
    public class WindowHostControl : UserControl
    {
        private Window _hostedWindow;

        public WindowHostControl(Window window)
        {
            _hostedWindow = window ?? throw new ArgumentNullException(nameof(window));

            // Витягти контент з вікна і помістити у цей UserControl
            if (window.Content is UIElement content)
            {
                // Видалити content з вікна
                window.Content = null;

                // Встановити як вміст цього UserControl
                this.Content = content;
            }
            else
            {
                // Якщо немає контенту - показати заглушку
                this.Content = new TextBlock
                {
                    Text = "⚠️ Вікно не містить контенту",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16
                };
            }

            // Встановити розміри якщо були задані
            if (window.Width > 0 && !double.IsNaN(window.Width))
            {
                this.Width = window.Width;
            }

            if (window.Height > 0 && !double.IsNaN(window.Height))
            {
                this.Height = window.Height;
            }

            // Прив'язати DataContext
            this.DataContext = window.DataContext;

            // Викликати Loaded якщо воно було в вікні
            this.Loaded += (s, e) =>
            {
                // Імітувати Loaded event вікна
                try
                {
                    var loadedEvent = window.GetType().GetMethod("OnLoaded",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (loadedEvent != null)
                    {
                        loadedEvent.Invoke(window, new object[] { s, e });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WindowHostControl] Помилка виклику Loaded: {ex.Message}");
                }
            };
        }

        /// <summary>
        /// Отримати оригінальне вікно
        /// </summary>
        public Window GetHostedWindow() => _hostedWindow;
    }
}
