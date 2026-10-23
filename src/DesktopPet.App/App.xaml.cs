using System.Windows;
using DesktopPet.App.Platform;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Emotion;
using DesktopPet.Core.Persistence;

namespace DesktopPet.App;

/// <summary>
/// WPF application entry point.
/// Manages single-instance guard, tray icon, archive load/save, and
/// coordinated shutdown per P0-004.
/// </summary>
public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _instanceGuard;
    private TrayService? _tray;
    private ArchiveService? _archive;
    private SaveCoordinator? _saveCoordinator;
    private MainWindow? _mainWindow;
    private PetStateArchive? _state;
    private PetSettingsArchive? _settings;
    private bool _isPaused;
    private bool _isShuttingDown;

    // P1-003: Emotion objects held at App level so they survive window recreation
    // and are accessible from MainWindow via App.Current.
    internal EmotionDecayAccumulator? EmotionAccumulator { get; private set; }
    internal GrievanceExpressionState? GrievanceExpression { get; private set; }
    internal CornerTracker? CornerTracking { get; private set; }

    /// <summary>
    /// F-05: Production mapping from PetStateArchive to emotion objects.
    /// This is the same logic used in OnStartup, extracted so tests can
    /// verify the real load→map→save→reload chain without copying code.
    /// </summary>
    internal static (EmotionDecayAccumulator acc, GrievanceExpressionState expr, CornerTracker tracker)
        MapArchiveToEmotionObjects(PetStateArchive state)
    {
        var lastDecayUtc = DateTime.TryParse(state.LastDecayUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDecay)
            ? DateTime.SpecifyKind(parsedDecay, DateTimeKind.Utc)
            : DateTime.UtcNow;

        var acc = new EmotionDecayAccumulator(state.Affinity, state.Grievance, lastDecayUtc);
        var expr = new GrievanceExpressionState();
        expr.Restore(state.PendingGrievanceExpression, state.GrievanceExpressionDate);
        var tracker = new CornerTracker();

        return (acc, expr, tracker);
    }

    /// <summary>
    /// F-05: Production mapping from emotion objects to PetStateArchive.
    /// This is the same logic used in MarkStateDirty, extracted so tests
    /// can verify the save path without a full App instance.
    /// </summary>
    internal static void MapEmotionObjectsToArchive(
        PetStateArchive state, EmotionDecayAccumulator acc, GrievanceExpressionState expr)
    {
        state.Affinity = acc.AffinityInt;
        state.Grievance = acc.GrievanceInt;
        state.LastDecayUtc = acc.LastDecayUtc.ToString("o");
        state.PendingGrievanceExpression = expr.IsPending;
        state.GrievanceExpressionDate = expr.MarkedDate;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance check.
        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.TryAcquire())
        {
            System.Windows.MessageBox.Show("DesktopPet is already running.", "DesktopPet",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        // Load archives.
        _archive = new ArchiveService();
        _saveCoordinator = new SaveCoordinator(_archive);

        var (state, stateFromBackup, stateUnknown) = _archive.LoadState();
        var (settings, settingsFromBackup, settingsUnknown) = _archive.LoadSettings();

        _state = state;
        _settings = settings;

        // P1-003: Initialize emotion objects from archived state via the
        // shared production mapping (F-05 R4: same path used by tests).
        var (emotionAcc, grievanceExpr, cornerTrack) = MapArchiveToEmotionObjects(_state);
        EmotionAccumulator = emotionAcc;
        GrievanceExpression = grievanceExpr;
        CornerTracking = cornerTrack;

        if (stateFromBackup)
            System.Diagnostics.Trace.TraceInformation("State restored from backup.");
        if (stateUnknown)
            System.Diagnostics.Trace.TraceWarning("State archive has unknown higher schemaVersion; will not overwrite.");
        if (settingsFromBackup)
            System.Diagnostics.Trace.TraceInformation("Settings restored from backup.");
        if (settingsUnknown)
            System.Diagnostics.Trace.TraceWarning("Settings archive has unknown higher schemaVersion; will not overwrite.");

        _isPaused = _state.IsPaused;

        // Apply the paused behavior before the window can start rendering.
        _mainWindow = new MainWindow();
        if (_isPaused)
            _mainWindow.PauseAnimation();
        // HWND/DPI are available here, before the first visible frame. Never
        // assign an unvalidated saved position directly to the window.
        _mainWindow.SourceInitialized += (_, _) => RestoreWindowPosition();
        _mainWindow.Closed += OnMainWindowClosed;

        // Set up tray.
        _tray = new TrayService();
        _tray.SetPaused(_isPaused);
        _tray.PauseToggled += OnPauseToggled;
        _tray.RecoverRequested += OnRecoverRequested;
        _tray.ExitRequested += OnExitRequested;

        _mainWindow.Show();
        if (stateUnknown || settingsUnknown)
            _tray.ShowReadOnlyArchiveNotice();
    }

    private void RestoreWindowPosition()
    {
        if (_state is null || _mainWindow is null)
            return;
        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return; // Keep the constructor's visible default, not stale archive coordinates.
        var sourceMonitor = monitors.FirstOrDefault(m => m.DeviceName == _state.SavedDeviceName);
        if (sourceMonitor.DeviceName is null)
            sourceMonitor = DesktopPet.Core.Geometry.MonitorLocator.FindPrimaryMonitor(monitors);
        var left = double.IsFinite(_state.WindowLeft) ? _state.WindowLeft : 100;
        var top = double.IsFinite(_state.WindowTop) ? _state.WindowTop : 100;
        var restored = WindowPositionAdapter.RecoverPosition(monitors, left, top,
            _mainWindow.Width, _mainWindow.Height, sourceMonitor.DpiScaleX,
            sourceMonitor.DpiScaleY, _state.SavedDeviceName);
        _mainWindow.Left = restored.LeftDip;
        _mainWindow.Top = restored.TopDip;
    }

    private void OnPauseToggled(object? sender, bool paused)
    {
        _isPaused = paused;
        if (_state is not null)
            _state.IsPaused = paused;
        _tray?.SetPaused(paused);

        if (paused)
        {
            // Stop active behavior (animation timer).
            _mainWindow?.PauseAnimation();
        }
        else
        {
            _mainWindow?.ResumeAnimation();
        }

        MarkStateDirty();
    }

    private void OnRecoverRequested(object? sender, EventArgs e)
    {
        _mainWindow?.RecoverPet();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        ShutdownMainWindow();
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        ShutdownMainWindow();
    }

    private void ShutdownMainWindow()
    {
        if (_isShuttingDown)
            return;
        _isShuttingDown = true;
        FlushFinalState();

        // Clean up tray.
        _tray?.Dispose();
        _tray = null;

        // Clean up single-instance guard.
        _instanceGuard?.Dispose();
        _instanceGuard = null;

        _mainWindow?.Close();
        Shutdown(0);
    }

    /// <summary>Mark state as dirty for debounced save. Called by MainWindow on position changes.</summary>
    internal void MarkStateDirty()
    {
        if (_state is not null && _saveCoordinator is not null)
        {
            _state.WindowLeft = _mainWindow?.Left ?? _state.WindowLeft;
            _state.WindowTop = _mainWindow?.Top ?? _state.WindowTop;
            _state.IsPaused = _isPaused;
            if (_mainWindow is not null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
                _state.SavedDeviceName = MonitorInterop.GetWindowMonitorDeviceName(hwnd) ?? _state.SavedDeviceName;
            }
            // P1-003: Sync emotion state to archive via the shared production
            // mapping (F-05 R4: same path used by tests).
            if (EmotionAccumulator is not null && GrievanceExpression is not null)
            {
                MapEmotionObjectsToArchive(_state, EmotionAccumulator, GrievanceExpression);
            }
            _saveCoordinator.MarkStateDirty(_state);
        }
    }

    private void FlushFinalState()
    {
        if (_saveCoordinator is null)
            return;
        // Capture and mark the final snapshot even if the previous debounce has
        // already completed. File-level protection still prevents downgrades.
        MarkStateDirty();
        var coordinator = _saveCoordinator;
        _saveCoordinator = null;
        coordinator.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Ensure save is flushed even if shutdown came from an unexpected path.
        FlushFinalState();
        _tray?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
