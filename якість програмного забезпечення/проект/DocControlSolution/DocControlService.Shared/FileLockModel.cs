// File: Shared/FileLockModel.cs
using System;

namespace DocControlService.Shared
{
    /// <summary>
    /// Модель для відстеження блокувань файлів (багатокористувацький режим)
    /// </summary>
    [Serializable]
    public class FileLockModel
    {
        /// <summary>
        /// ID блокування
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Повний шлях до файлу
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Ім'я пристрою який заблокував файл
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Ім'я користувача
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// Час блокування
        /// </summary>
        public DateTime LockTime { get; set; }

        /// <summary>
        /// Час останньої модифікації (heartbeat)
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Чи активне блокування
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Чи належить блокування поточному пристрою
        /// </summary>
        public bool IsOwnedByCurrentDevice { get; set; }

        /// <summary>
        /// Чи файл заблокований іншим користувачем
        /// </summary>
        public bool IsLockedByOther => IsActive && !IsOwnedByCurrentDevice;

        /// <summary>
        /// Людино-читабельний опис блокування
        /// </summary>
        public string LockDescription
        {
            get
            {
                if (!IsActive) return "Не заблокований";
                var user = string.IsNullOrEmpty(UserName) ? DeviceName : $"{UserName} ({DeviceName})";
                var duration = DateTime.UtcNow - LockTime;
                return $"Заблокований: {user} | {duration.TotalMinutes:F0} хв. тому";
            }
        }
    }
}
