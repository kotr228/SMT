using System;
using System.Windows;

namespace DocControlUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Глобальна обробка необроблених виключень
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Критична помилка:\n\n{ex.Message}\n\nДеталі:\n{ex.StackTrace}",
                    "Помилка програми",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Необроблена помилка:\n\n{e.Exception.Message}",
                "Помилка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}