using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.App.Diagnostics;
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

    // PREP-001 R3 F-02: When true, OnStartup skips production initialization
    // (SingleInstanceGuard, ArchiveService, MainWindow, TrayService) and
    // preserves the isolated dependencies injected by ProbeSetupIsolated.
    // This prevents the WPF startup pipeline from overwriting isolated
    // archive/log/lock configuration with production defaults.
    internal bool _probeIsolatedMode;

    // PREP-001: Diagnostic logger for unhandled exception notifications.
    // Registered at the very start of OnStartup, before any other initialization.
    internal DiagnosticLogger? DiagnosticLogger { get; private set; }

    /// <summary>
    /// PREP-001 R1 F-02: Allows the isolation probe to inject an isolated
    /// DiagnosticLogger before calling RegisterDiagnosticHandlers, ensuring
    /// the probe uses the production registration path with a test logger.
    /// </summary>
    internal void SetDiagnosticLogger(DiagnosticLogger logger)
    {
        DiagnosticLogger = logger;
    }

    /// <summary>
    /// PREP-001: Application version/build identifier for diagnostic logs.
    /// Uses assembly version; can be overridden in tests.
    /// </summary>
    internal string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

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
    /// PREP-001 R2 F-02: Isolated startup for diagnostic probes.
    /// Sets up isolated archive/save infrastructure (like TestSetupIsolatedSave)
    /// but also creates an isolated SingleInstanceGuard with a unique mutex name,
    /// so the probe does NOT access the production mutex or user archive.
    /// 
    /// This method does NOT call the production OnStartup, does NOT create
    /// a MainWindow, does NOT create a TrayService, and does NOT access the
    /// user's default archive directory. The probe can then call
    /// RegisterDiagnosticHandlers() to wire the real exception handlers.
    /// </summary>
    internal void ProbeSetupIsolated(string isolatedDir, string isolatedMutexName)
    {
        // Isolated archive — uses the provided temp directory, not the user's
        // default %LOCALAPPDATA%\DesktopPet\ path.
        _archive = new ArchiveService(isolatedDir);
        _saveCoordinator = new SaveCoordinator(_archive);
        _state = new PetStateArchive();
        _settings = new PetSettingsArchive();
        _isPaused = false;
        _isShuttingDown = false;

        // Isolated single-instance guard — uses a unique mutex name so the
        // probe does NOT contend with the production DesktopPet instance.
        _instanceGuard = new SingleInstanceGuard(isolatedMutexName);

        // PREP-001 R3 F-02: Set the isolated-mode flag so that if the WPF
        // startup pipeline invokes OnStartup (e.g. via Dispatcher pumping),
        // it will skip production initialization and preserve the isolated
        // dependencies set above.
        _probeIsolatedMode = true;

        // Isolated diagnostic logger — uses a subdirectory of the temp dir.
        if (DiagnosticLogger is null)
        {
            var logDir = Path.Combine(isolatedDir, "logs");
            DiagnosticLogger = new DiagnosticLogger(logDir);
        }
    }

    /// <summary>
    /// PREP-001 R2 F-02: Exposes the save coordinator for probe/test
    /// verification of normal/abnormal save snapshots.
    /// </summary>
    internal SaveCoordinator? TestSaveCoordinator => _saveCoordinator;

    /// <summary>
    /// PREP-001 R3 F-02: Exposes the instance guard for probe verification
    /// that the isolated mutex is still in effect after Dispatcher pumping.
    /// </summary>
    internal SingleInstanceGuard? TestInstanceGuard => _instanceGuard;

    /// <summary>
    /// PREP-001 R2 F-02: Exposes the archive service for probe/test
    /// verification of save snapshots.
    /// </summary>
    internal ArchiveService? TestArchive => _archive;

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

        // PREP-001: Allow tests to inject an isolated log directory.
        // If not provided, uses a subdirectory of isolatedDir.
        if (DiagnosticLogger is null)
        {
            var logDir = Path.Combine(isolatedDir, "logs");
            DiagnosticLogger = new DiagnosticLogger(logDir);
        }
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
        // PREP-001: Clean up diagnostic logger.
        DiagnosticLogger?.Dispose();
        DiagnosticLogger = null;
    }

    /// <summary>
    /// P2-003 S3 R6 F-08: Test-only injection of a MainWindow so tests can
    /// exercise the real FlushFinalState → MarkStateDirty → SaveCoordinator
    /// chain and the real RestoreWindowPosition recovery entry point,
    /// without re-implementing save/restore logic.
    /// </summary>
    internal void TestSetMainWindow(MainWindow w) => _mainWindow = w;

    /// <summary>
    /// P2-003 S3 R6 F-08: Test-only accessor for the isolated state object
    /// so tests can verify the saved archive after FlushFinalState.
    /// </summary>
    internal PetStateArchive? TestState => _state;

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

        // PREP-001 R3 F-02: If the probe has already injected isolated
        // dependencies via ProbeSetupIsolated, skip production initialization
        // entirely. This prevents OnStartup from overwriting the isolated
        // ArchiveService, SingleInstanceGuard, DiagnosticLogger, and from
        // creating a production MainWindow/TrayService.
        if (_probeIsolatedMode)
        {
            RegisterDiagnosticHandlers();
            return;
        }

        // PREP-001: Register diagnostic exception notifications as early as
        // possible, before any other initialization. These are diagnostic-only:
        // they record to a local bounded log and do NOT swallow exceptions,
        // force-save archives, or auto-restart.
        RegisterDiagnosticHandlers();

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
            // PREP-002: Use shared MonotonicClock instead of the static
            // MainWindow.GetElapsedMsPublic() accessor, eliminating the
            // App→MainWindow static dependency for time retrieval.
            var nowMs = MonotonicClock.Instance.GetElapsedMs();
            RuntimeTracker.StartActivePeriod(nowMs / 60000.0);
        }

        // Apply the paused behavior before the window can start rendering.
        _mainWindow = new MainWindow();

        // PREP-002: Pass the shared state objects and save-dirty notification
        // to MainWindow via an explicit context, so that MainWindow no longer
        // needs to reach back through Application.Current to access them.
        // The App.Current fallback in MainWindow remains only for tests that
        // use the parameterless constructor without setting StateContext.
        _mainWindow.StateContext = new PetStateContext
        {
            EmotionAccumulator = EmotionAccumulator,
            CornerTracking = CornerTracking,
            GrievanceExpression = GrievanceExpression,
            RuntimeTracker = RuntimeTracker,
            RareEventScheduler = RareEventScheduler,
            MarkStateDirty = MarkStateDirty,
        };

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

    /// <summary>
    /// PREP-001: Registers diagnostic notifications for the three WPF/.NET
    /// unhandled exception channels. Per ARCHITECTURE §10:
    /// - DispatcherUnhandledException: record, then keep default handling
    ///   (do NOT set Handled=true — unknown UI exceptions should not be
    ///   globally swallowed).
    /// - AppDomain.UnhandledException: best-effort record only; no recovery
    ///   promise.
    /// - TaskScheduler.UnobservedTaskException: record the real source; this
    ///   is a notification, not an immediate/inevitable crash. Normal
    ///   await/local catch still owns known async errors.
    /// </summary>
    internal void RegisterDiagnosticHandlers()
    {
        // Create the logger with default settings (%LOCALAPPDATA%/DesktopPet/logs/).
        // Tests can inject a custom logger via TestSetupIsolatedSave.
        DiagnosticLogger ??= new DiagnosticLogger();

        var version = AppVersion;

        // 1. WPF Dispatcher unhandled exception — record but do NOT swallow.
        //    Setting e.Handled = false (the default) lets the framework's
        //    normal exception handling proceed.
        DispatcherUnhandledException += (sender, e) =>
        {
            try
            {
                DiagnosticLogger?.Record(
                    "DispatcherUnhandled",
                    isTerminating: false,
                    e.Exception,
                    version);
            }
            catch
            {
                // Log failure must not cause a second exception.
            }
            // Explicitly do NOT set e.Handled = true.
            // Unknown UI exceptions keep framework default handling.
        };

        // 2. AppDomain terminal notification — best-effort record only.
        System.AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try
            {
                var isTerminating = e.IsTerminating;
                var ex = e.ExceptionObject as Exception;
                DiagnosticLogger?.Record(
                    "AppDomainUnhandled",
                    isTerminating,
                    ex,
                    version);
            }
            catch
            {
                // Best-effort only; no recovery promise.
            }
        };

        // 3. TaskScheduler unobserved task exception — notification, not
        //    immediate crash. Record the real source.
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            try
            {
                DiagnosticLogger?.Record(
                    "TaskUnobserved",
                    isTerminating: false,
                    e.Exception,
                    version);
                // Do NOT set e.SetObserved() — we are diagnosing, not hiding.
            }
            catch
            {
                // Log failure must not cause a second exception.
            }
        };
    }

    // P2-003 S3 R6 F-08: Made internal so tests can call the real
    // production recovery entry point instead of re-implementing it.
    internal void RestoreWindowPosition()
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
                // PREP-002: Shared MonotonicClock — no more MainWindow static call.
                var nowMs = MonotonicClock.Instance.GetElapsedMs();
                RuntimeTracker.EndActivePeriod(nowMs / 60000.0);
            }
        }
        else
        {
            _mainWindow?.ResumeAnimation();
            // P1-004: Start a fresh active period on resume.
            if (RuntimeTracker is not null)
            {
                // PREP-002: Shared MonotonicClock — no more MainWindow static call.
                var nowMs = MonotonicClock.Instance.GetElapsedMs();
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
