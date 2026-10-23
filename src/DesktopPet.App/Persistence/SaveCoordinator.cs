using System.Threading;
using System.Threading.Tasks;
using DesktopPet.Core.Persistence;

namespace DesktopPet.App.Persistence;

/// <summary>
/// Debounced save coordinator per ARCHITECTURE §7:
/// "脏状态按 5 秒防抖保存并在正常退出时落盘，不逐帧写磁盘".
/// Marks state as dirty, then saves after 5 seconds of no further marks.
/// On explicit flush (e.g. app exit), saves immediately.
/// </summary>
public sealed class SaveCoordinator : IDisposable
{
    private readonly ArchiveService _archive;
    private readonly TimeSpan _debounceInterval;
    private readonly System.Threading.Timer _timer;
    private readonly object _lock = new();

    private PetStateArchive? _pendingState;
    private PetSettingsArchive? _pendingSettings;
    private bool _stateDirty;
    private bool _settingsDirty;
    private bool _disposed;

    /// <summary>Default debounce delay: 5 seconds.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(5);

    public SaveCoordinator(ArchiveService archive, TimeSpan? debounceInterval = null)
    {
        _archive = archive;
        _debounceInterval = debounceInterval ?? DefaultDebounce;
        _timer = new System.Threading.Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Mark state as dirty; will save after debounce interval.</summary>
    public void MarkStateDirty(PetStateArchive state)
    {
        lock (_lock)
        {
            _pendingState = state;
            _stateDirty = true;
            _timer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Mark settings as dirty; will save after debounce interval.</summary>
    public void MarkSettingsDirty(PetSettingsArchive settings)
    {
        lock (_lock)
        {
            _pendingSettings = settings;
            _settingsDirty = true;
            _timer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Immediately save any pending dirty state. Called on app exit.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            if (_stateDirty && _pendingState is not null)
            {
                _archive.SaveState(_pendingState);
                _stateDirty = false;
            }

            if (_settingsDirty && _pendingSettings is not null)
            {
                _archive.SaveSettings(_pendingSettings);
                _settingsDirty = false;
            }
        }
    }

    private void OnTimerElapsed(object? state)
    {
        try
        {
            Flush();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Debounced save failed: {0}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Flush();
        _timer.Dispose();
        _disposed = true;
    }
}
