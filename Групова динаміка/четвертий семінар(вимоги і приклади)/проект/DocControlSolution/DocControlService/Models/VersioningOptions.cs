// File: Models/VersioningOptions.cs
namespace DocControlService.Models
{
    /// <summary>
    /// Налаштування авто-комітів. Можеш зчитувати з appsettings.json.
    /// </summary>
    public class VersioningOptions
    {
        /// <summary>
        /// Інтервал між автокомітами (хвилини). За замовчуванням 60.
        /// </summary>
        public int CommitIntervalMinutes { get; set; } = 60;

        /// <summary>
        /// Автор для комітів.
        /// </summary>
        public string CommitAuthorName { get; set; } = "DocControlService";

        public string CommitAuthorEmail { get; set; } = "doccontrol@local";

        /// <summary>
        /// Повідомлення за замовчуванням.
        /// </summary>
        public string DefaultCommitMessage { get; set; } = "Auto commit by DocControlService";
    }
}
