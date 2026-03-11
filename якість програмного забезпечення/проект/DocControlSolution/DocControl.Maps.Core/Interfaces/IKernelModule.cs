using System.Threading.Tasks;

namespace DocControl.Maps.Core.Interfaces
{
    /// <summary>
    /// Базовий інтерфейс для всіх ядер системи
    /// </summary>
    public interface IKernelModule
    {
        string ModuleName { get; }
        string Version { get; }

        Task InitializeAsync();
        Task ShutdownAsync();

        bool IsInitialized { get; }
    }
}