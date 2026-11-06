using System.Windows;
using DesktopPet.App.Platform;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Emotion;
using DesktopPet.Core.Events;
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

    // P1-004: Rare-event objects held at App level for the same reason.
    // RuntimeTracker accumulates effective (non-paused) monotonic minutes.
    // RareEventScheduler manages the daily plan and trigger evaluation.
    internal EffectiveRuntimeTracker? RuntimeTracker { get; private set; }
    internal RareEventScheduler? RareEventScheduler { get; private set; }

    /// <summary>
    /// F-08: Test-only setup method that configures the App with isolated
    /// archive/save infrastructure and runtime objects, bypassing the full
    /// OnStartup (single-instance guard, tray, window). This allows tests to
    /// verify the real MarkStateDirty → SaveCoordinator → file path without
    /// manually calling Map/Save.
    /// </summary>
    internal void TestSetupIsolatedSave(
        string isolatedDir,
        PetStateArchive? initialState = null,
        EffectiveRuntimeTracker? tracker = null,
        RareEventScheduler? scheduler = null)
    {
        _archive = new ArchiveService(isolatedDir);
        _saveCoordinator = new SaveCoordinator(_archive);
        _state = initialState ?? new PetStateArchive();
        _settings = new PetSettingsArchive();
        _isPaused = false;
        _isShuttingDown = false;
        RuntimeTracker = tracker ?? new EffectiveRuntimeTracker();
        RareEventScheduler = scheduler ?? new RareEventScheduler();
        EmotionAccumulator ??= new EmotionDecayAccumulator(50, DateTime.UtcNow);
        GrievanceExpression ??= new GrievanceExpressionState();
        CornerTracking ??= new CornerTracker();
    }

    /// <summary>
    /// F-08: Test-only cleanup — disposes SaveCoordinator and clears
    /// singleton state so subsequent tests can create a fresh App.
    /// </summary>
    internal void TestCleanup()
    {
        _saveCoordinator?.Dispose();
        _saveCoordinator = null;
        _archive = null;
        _tray?.Dispose();
        _tray = null;
        _instanceGuard?.Dispose();
        _instanceGuard = null;
    }

    /// <summary>
    /// F-05: Production mapping from PetStateArchive to emotion objects.
    /// This is the same logic used in OnStartup, extracted so tests can
    /// verify the real load→map→save→reload chain without copying code.
    /// </summary>
    internal static (EmotionDecayAccumulator acc, GrievanceExpressionState expr, CornerTracker tracker)
        MapArchiveToEmotionObjects(PetStateArchive state, DateTime? restoreUtcNow = null)
    {
        var utcNow = restoreUtcNow ?? DateTime.UtcNow;

        var lastDecayUtc = DateTime.TryParse(state.LastDecayUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDecay)
            ? DateTime.SpecifyKind(parsedDecay, DateTimeKind.Utc)
            : utcNow;

        // F-06: When the app was paused when it exited, the offline interval
        // between save and restart is a frozen period — "暂停时段不计入衰减"
        // applies to the exit-while-paused scenario. Shift the decay reference
        // to now so the offline gap is not counted as decay time on the first
        // AdvanceTo call. When the app was NOT paused, the offline interval
        // is normal offline decay (per PRODUCT R-02) and the reference is kept.
        //
        // F-06 R7: The shift must never move the reference backward. If the
        // clock was rolled back (utcNow earlier than the saved lastDecayUtc),
        // keep the saved reference so that F-02's non-regression protection
        // in AdvanceTo is preserved. Only shift forward.
        if (state.IsPaused && utcNow > lastDecayUtc)
        {
            lastDecayUtc = utcNow;
        }

        var acc = new EmotionDecayAccumulator(state.Grievance, lastDecayUtc);
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
        state.Grievance = acc.GrievanceInt;
        state.LastDecayUtc = acc.LastDecayUtc.ToString("o");
        state.PendingGrievanceExpression = expr.IsPending;
        state.GrievanceExpressionDate = expr.MarkedDate;
    }

    /// <summary>
    /// F-06: Production mapping from PetStateArchive to rare-event objects.
    /// Restores EffectiveRuntimeTracker accumulated minutes and RareEventScheduler
    /// plan from the same archive used by the real load path. Tests verify this
    /// is the single production conversion — no parallel test-only mapping.
    /// </summary>
    internal static (EffectiveRuntimeTracker tracker, RareEventScheduler scheduler)
        MapArchiveToRareEventObjects(PetStateArchive state)
    {
        var tracker = new EffectiveRuntimeTracker();
        tracker.Restore(state.AccumulatedRuntimeMinutes);

        var scheduler = new RareEventScheduler();
        if (state.RareEventPlan is not null)
        {
            var plan = new RareEventPlan
            {
                LocalDate = state.RareEventPlan.LocalDate ?? string.Empty,
                TriggerDelayMinutes = state.RareEventPlan.TriggerDelayMinutes,
                TriggerRoll = state.RareEventPlan.TriggerRoll,
                Consumed = state.RareEventPlan.Consumed,
                AccumulatedBaselineMinutes = state.RareEventPlan.AccumulatedBaselineMinutes
            };
            scheduler.RestorePlan(plan);
        }

        return (tracker, scheduler);
    }

    /// <summary>
    /// F-06: Production mapping from rare-event objects to PetStateArchive.
    /// Saves EffectiveRuntimeTracker accumulated minutes and RareEventScheduler
    /// plan snapshot into the same archive used by the real save path.
    /// </summary>
    internal static void MapRareEventObjectsToArchive(
        PetStateArchive state, EffectiveRuntimeTracker tracker, RareEventScheduler scheduler)
    {
        state.AccumulatedRuntimeMinutes = tracker.AccumulatedMinutes;
        state.RareEventPlan = scheduler.GetSnapshot() is { } plan
            ? new RareEventPlanSnapshot
            {
                LocalDate = plan.LocalDate,
                TriggerDelayMinutes = plan.TriggerDelayMinutes,
                TriggerRoll = plan.TriggerRoll,
                Consumed = plan.Consumed,
                AccumulatedBaselineMinutes = plan.AccumulatedBaselineMinutes
            }
            : null;
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

        // P1-004: Initialize rare-event objects from archived state via the
        // shared production mapping (F-06: same path used by tests).
        var (runtimeTracker, rareScheduler) = MapArchiveToRareEventObjects(_state);
        RuntimeTracker = runtimeTracker;
        RareEventScheduler = rareScheduler;

        if (stateFromBackup)
            System.Diagnostics.Trace.TraceInformation("State restored from backup.");
        if (stateUnknown)
            System.Diagnostics.Trace.TraceWarning("State archive has unknown higher schemaVersion; will not overwrite.");
        if (settingsFromBackup)
            System.Diagnostics.Trace.TraceInformation("Settings restored from backup.");
        if (settingsUnknown)
            System.Diagnostics.Trace.TraceWarning("Settings archive has unknown higher schemaVersion; will not overwrite.");

        _isPaused = _state.IsPaused;

        // P1-004: Start the effective runtime active period on startup when
        // not paused. When paused at exit, the tracker was ended at pause
        // time and will be resumed by OnPauseToggled(false) or ResumeAnimation.
        if (!_isPaused && RuntimeTracker is not null)
        {
            var nowMs = DesktopPet.App.MainWindow.GetElapsedMsPublic();
            RuntimeTracker.StartActivePeriod(nowMs / 60000.0);
        }

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
            // P1-004: End the active period so paused time is not counted
            // as effective runtime for rare-event scheduling.
            if (RuntimeTracker is not null)
            {
                var nowMs = DesktopPet.App.MainWindow.GetElapsedMsPublic();
                RuntimeTracker.EndActivePeriod(nowMs / 60000.0);
            }
        }
        else
        {
            _mainWindow?.ResumeAnimation();
            // P1-004: Start a fresh active period on resume.
            if (RuntimeTracker is not null)
            {
                var nowMs = DesktopPet.App.MainWindow.GetElapsedMsPublic();
                RuntimeTracker.StartActivePeriod(nowMs / 60000.0);
            }
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
            // P1-004: Sync rare-event state to archive via the shared production
            // mapping (F-06: same path used by tests).
            if (RuntimeTracker is not null && RareEventScheduler is not null)
            {
                MapRareEventObjectsToArchive(_state, RuntimeTracker, RareEventScheduler);
            }
            _saveCoordinator.MarkStateDirty(_state);
        }
    }

    internal void FlushFinalState()
    {
        if (_saveCoordinator is null)
            return;
        // P1-004: If a probe is active, restore the original position so
        // the saved state reflects the pre-probe location, not the edge.
        _mainWindow?.RestoreProbePositionForExit();
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
