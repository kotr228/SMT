using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;

namespace DocControlService
{
    static class Program
    {
        /// <summary>
        /// Головна точка входу для додатку
        /// </summary>
        static void Main(string[] args)
        {
            // Перевірка прав адміністратора
            if (!IsRunAsAdministrator())
            {
                Console.WriteLine("═══════════════════════════════════════════════════════");
                Console.WriteLine("  ПОМИЛКА: Сервіс потребує прав адміністратора!");
                Console.WriteLine("═══════════════════════════════════════════════════════");
                Console.WriteLine();
                Console.WriteLine("Спроба перезапуску з правами адміністратора...");

                RestartAsAdministrator(args);
                return;
            }

            // Визначаємо режим роботи
            bool debugMode = args.Contains("--debug") || args.Contains("-d") ||
                           args.Contains("--console") || args.Contains("-c") ||
                           Environment.UserInteractive;

            if (debugMode)
            {
                // DEBUG MODE - запуск через консоль
                Console.Title = "DocControl Service - Debug Mode";
                var service = new DocControlWindowsService(debugMode: true);
                service.StartDebug(args);
            }
            else
            {
                // PRODUCTION MODE - запуск як Windows Service
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new DocControlWindowsService(debugMode: false)
                };
                ServiceBase.Run(ServicesToRun);
            }
        }

        /// <summary>
        /// Перевірка чи додаток запущений з правами адміністратора
        /// </summary>
        private static bool IsRunAsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Перезапуск додатку з правами адміністратора
        /// </summary>
        private static void RestartAsAdministrator(string[] args)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    Arguments = string.Join(" ", args),
                    Verb = "runas" // Запит на підвищення прав
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не вдалося перезапустити з правами адміністратора: {ex.Message}");
                Console.WriteLine("Будь ласка, запустіть програму вручну з правами адміністратора.");
                Console.WriteLine();
                Console.WriteLine("Натисніть будь-яку клавішу для виходу...");
                Console.ReadKey();
            }
        }
    }
}