using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace DocControlUI.Helpers
{
    /// <summary>
    /// Сервіс навігації для Single Page Application
    /// Підтримує історію та кнопки Назад/Вперед
    /// </summary>
    public class NavigationService
    {
        private readonly ContentControl _contentHost;
        private readonly Stack<NavigationEntry> _backStack;
        private readonly Stack<NavigationEntry> _forwardStack;
        private NavigationEntry _currentPage;

        /// <summary>
        /// Подія зміни можливості навігації
        /// </summary>
        public event EventHandler NavigationChanged;

        public NavigationService(ContentControl contentHost)
        {
            _contentHost = contentHost ?? throw new ArgumentNullException(nameof(contentHost));
            _backStack = new Stack<NavigationEntry>();
            _forwardStack = new Stack<NavigationEntry>();
        }

        /// <summary>
        /// Чи можна повернутися назад
        /// </summary>
        public bool CanGoBack => _backStack.Count > 0;

        /// <summary>
        /// Чи можна перейти вперед
        /// </summary>
        public bool CanGoForward => _forwardStack.Count > 0;

        /// <summary>
        /// Поточна сторінка
        /// </summary>
        public NavigationEntry CurrentPage => _currentPage;

        /// <summary>
        /// Навігація до нової сторінки
        /// </summary>
        public void NavigateTo(UserControl content, string title, string icon = "📄")
        {
            // Зберегти поточну сторінку в історію
            if (_currentPage != null)
            {
                _backStack.Push(_currentPage);
            }

            // Очистити forward stack при новій навігації
            _forwardStack.Clear();

            // Створити нову сторінку
            _currentPage = new NavigationEntry
            {
                Content = content,
                Title = title,
                Icon = icon,
                Timestamp = DateTime.Now
            };

            // Встановити контент
            _contentHost.Content = content;

            // Викликати подію
            NavigationChanged?.Invoke(this, EventArgs.Empty);

            Console.WriteLine($"[Navigation] Навігація → {icon} {title} (History: {_backStack.Count})");
        }

        /// <summary>
        /// Повернутися назад
        /// </summary>
        public void GoBack()
        {
            if (!CanGoBack)
            {
                Console.WriteLine("[Navigation] Неможливо повернутися назад - історія порожня");
                return;
            }

            // Зберегти поточну сторінку в forward stack
            if (_currentPage != null)
            {
                _forwardStack.Push(_currentPage);
            }

            // Взяти попередню сторінку
            _currentPage = _backStack.Pop();

            // Встановити контент
            _contentHost.Content = _currentPage.Content;

            // Викликати подію
            NavigationChanged?.Invoke(this, EventArgs.Empty);

            Console.WriteLine($"[Navigation] Назад → {_currentPage.Icon} {_currentPage.Title}");
        }

        /// <summary>
        /// Перейти вперед
        /// </summary>
        public void GoForward()
        {
            if (!CanGoForward)
            {
                Console.WriteLine("[Navigation] Неможливо перейти вперед");
                return;
            }

            // Зберегти поточну сторінку в back stack
            if (_currentPage != null)
            {
                _backStack.Push(_currentPage);
            }

            // Взяти наступну сторінку
            _currentPage = _forwardStack.Pop();

            // Встановити контент
            _contentHost.Content = _currentPage.Content;

            // Викликати подію
            NavigationChanged?.Invoke(this, EventArgs.Empty);

            Console.WriteLine($"[Navigation] Вперед → {_currentPage.Icon} {_currentPage.Title}");
        }

        /// <summary>
        /// Повернутися на головну сторінку (початкову)
        /// </summary>
        public void GoHome()
        {
            if (_backStack.Count == 0)
            {
                Console.WriteLine("[Navigation] Вже на головній сторінці");
                return;
            }

            // Очистити всі стеки і повернутися до першої сторінки
            while (_backStack.Count > 1)
            {
                _backStack.Pop();
            }

            if (_backStack.Count > 0)
            {
                GoBack();
            }

            Console.WriteLine("[Navigation] Повернення на головну");
        }

        /// <summary>
        /// Очистити історію навігації
        /// </summary>
        public void ClearHistory()
        {
            _backStack.Clear();
            _forwardStack.Clear();
            _currentPage = null;
            _contentHost.Content = null;

            NavigationChanged?.Invoke(this, EventArgs.Empty);

            Console.WriteLine("[Navigation] Історія очищена");
        }

        /// <summary>
        /// Отримати хлібні крихти (breadcrumbs)
        /// </summary>
        public List<string> GetBreadcrumbs()
        {
            var breadcrumbs = new List<string>();

            // Додати всі сторінки з історії
            foreach (var entry in _backStack)
            {
                breadcrumbs.Insert(0, $"{entry.Icon} {entry.Title}");
            }

            // Додати поточну сторінку
            if (_currentPage != null)
            {
                breadcrumbs.Add($"{_currentPage.Icon} {_currentPage.Title}");
            }

            return breadcrumbs;
        }
    }

    /// <summary>
    /// Запис в історії навігації
    /// </summary>
    public class NavigationEntry
    {
        public UserControl Content { get; set; }
        public string Title { get; set; }
        public string Icon { get; set; }
        public DateTime Timestamp { get; set; }

        public override string ToString() => $"{Icon} {Title}";
    }
}
