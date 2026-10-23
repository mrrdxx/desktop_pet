using System.Runtime.InteropServices;

namespace DesktopPet.App.Platform;

/// <summary>
/// Single-instance guard per ARCHITECTURE §3:
/// "单进程承载所有宠物；重复启动应恢复既有实例或给明确反馈".
/// Uses a named mutex to detect an already-running instance.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Global\DesktopPet.App.SingleInstance";
    private Mutex? _mutex;
    private bool _ownsMutex;

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns true if this is the first instance; false if another is running.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out _ownsMutex);
        return _ownsMutex;
    }

    /// <summary>True if this process owns the mutex (is the active instance).</summary>
    public bool IsOwner => _ownsMutex;

    public void Dispose()
    {
        if (_mutex is not null)
        {
            if (_ownsMutex)
                _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
