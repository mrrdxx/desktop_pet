using System.Runtime.InteropServices;

namespace DesktopPet.App.Platform;

/// <summary>
/// Single-instance guard per ARCHITECTURE §3:
/// "单进程承载所有宠物；重复启动应恢复既有实例或给明确反馈".
/// Uses a named mutex to detect an already-running instance.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string DefaultMutexName = @"Global\DesktopPet.App.SingleInstance";
    private readonly string _mutexName;
    private Mutex? _mutex;
    private bool _ownsMutex;

    /// <summary>
    /// Creates a SingleInstanceGuard with the default production mutex name.
    /// </summary>
    public SingleInstanceGuard() : this(DefaultMutexName) { }

    /// <summary>
    /// PREP-001 R2 F-02: Creates a SingleInstanceGuard with a custom mutex
    /// name, allowing diagnostic probes to use an isolated mutex that does
    /// not contend with the production DesktopPet instance.
    /// </summary>
    internal SingleInstanceGuard(string mutexName)
    {
        _mutexName = mutexName;
    }

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns true if this is the first instance; false if another is running.
    /// </summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: _mutexName, createdNew: out _ownsMutex);
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
