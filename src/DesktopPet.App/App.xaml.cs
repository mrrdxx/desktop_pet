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
using DesktopPet.Core.Social;

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

    // P3-001-S2: Multi-window management. Each pet has its own MainWindow,
    // keyed by PetId. The old single _mainWindow field is replaced.
    private readonly Dictionary<string, MainWindow> _petWindows = new();

    /// <summary>
    /// P4-002 R2 F-01 收尾: The Dispatcher that owns the pet-window
    /// collection. In production every pet window — and every
    /// _petWindows mutation (CreatePetWindow / OnPetWindowClosed /
    /// RemovePet) — lives on the single App UI thread, so this IS the
    /// production UI Dispatcher. App.Dispatcher is not used directly
    /// because a test may reuse an App instance created on a different
    /// thread; the dispatcher is captured from the windows themselves.
    /// </summary>
    private System.Windows.Threading.Dispatcher? _petWindowsDispatcher;
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

    // P3-002: Social coordinator for mutual-notice between pets.
    // Global — manages the single shared social session.
    internal SocialCoordinator? SocialCoordinator { get; set; }

    // P4-001: Global activity session service for rest-reminder timing.
    // Independent of rare-event timing (RuntimeTracker). Single shared
    // session across all pets. App samples input idle and system lifecycle;
    // Core (ActivitySession) handles timer and threshold logic.
    internal ActivitySessionService? ActivitySessionService { get; private set; }

    // P6-001: App-level service for local usage statistics (foreground
    // activity approximation + normalized app base name). Independently
    // stored in stats.json; sampled on its own 5 s timer; never touches the
    // pet archive and never mixes with the rest-reminder timing.
    internal UsageStatsService? UsageStatsService { get; private set; }

    // P6-004（第一小步）：主动气泡与只读查看窗。气泡实例复用（隐藏而非销毁），
    // 展示判定全部交给 Core 的 UsageStatsBubblePlanner；查看窗为用户主动打开。
    private StatsBubbleWindow? _statsBubble;
    private StatsViewerWindow? _statsViewer;
    private DispatcherTimer? _statsBubbleTimer;

    /// <summary>
    /// P6-004（第一小步）：主动气泡判定使用的本地时间源注入点。生产为 null
    /// （用 <see cref="DateTimeOffset.Now"/>），测试/隔离预览可固定时段。
    /// </summary>
    internal Func<DateTimeOffset>? TestStatsBubbleLocalNow;

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
        // P3-002: Create a fresh social coordinator for each isolated test.
        SocialCoordinator = new SocialCoordinator();

        // P4-001: Create a fresh activity session service for each isolated
        // test. Tests that need to verify the service can inject a custom
        // one via the ActivitySessionService property setter.
        ActivitySessionService ??= new ActivitySessionService();

        // P6-001: Create a fresh usage-stats service rooted at the isolated
        // directory (independent stats.json, never the user archive). The
        // sampling timer is not auto-started here — stats-specific tests
        // inject a service with controlled samplers/clock or call
        // Service.Start() to exercise the restore path.
        UsageStatsService ??= new UsageStatsService(isolatedDir, autoStartTimer: false);

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

        // P3-003 R5 F-04: Reset WPF internal lifecycle fields so the
        // reused App instance behaves like a fresh App for each test.
        // When tests run in batch, a previous test's TestCleanup may
        // have closed the last window while _shutdownMode was still
        // OnLastWindowClose (the WPF default), triggering WPF
        // auto-shutdown and setting _appIsShutdown=true. Subsequent
        // CreatePetWindow + Show() will not receive SourceInitialized
        // because the App thinks it is already shutting down.
        // We use reflection (same approach as ShutdownMainWindowForTest)
        // to set both fields without triggering VerifyAccess.
        ResetWpfLifecycleForTest();
    }

    /// <summary>
    /// P3-003 R5 F-04: Reset WPF-internal lifecycle fields via reflection
    /// so a reused App instance behaves like a fresh App. This bypasses
    /// the VerifyAccess check in the ShutdownMode property setter and
    /// clears the _appIsShutdown flag that WPF sets during auto-shutdown.
    /// </summary>
    private void ResetWpfLifecycleForTest()
    {
        var appType = typeof(System.Windows.Application);
        try
        {
            var modeField = appType.GetField(
                "_shutdownMode",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            modeField?.SetValue(this, System.Windows.ShutdownMode.OnExplicitShutdown);
        }
        catch { /* best-effort; field name stable since .NET 3.0 */ }

        try
        {
            var shutdownField = appType.GetField(
                "_appIsShutdown",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            shutdownField?.SetValue(this, false);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// F-08: Test-only cleanup — disposes SaveCoordinator and clears
    /// singleton state so subsequent tests can create a fresh App.
    /// </summary>
    internal void TestCleanup()
    {
        // P3-001-S2 R7 F-04: Close and unregister all pet windows created
        // during the test, including windows that were never Show()n.
        // CreatePetWindow constructs MainWindow which starts _updateTimer
        // in the constructor; Clear() alone does not trigger OnClosed, so
        // the timer would keep running. We must explicitly Close() every
        // window on the current Dispatcher — IsVisible is NOT a guard.
        //
        // OnPetWindowClosed is detached first to prevent
        // OnPetWindowClosed → ShutdownMainWindow from disrupting the test
        // session. Windows on other Dispatchers are left for their owning
        // test's finally block; we only drop the dictionary references.
        //
        // P3-003 R5 F-04: _shutdownMode is set to OnExplicitShutdown by
        // TestSetupIsolatedSave at the start of each test, so closing
        // windows here should not trigger auto-shutdown in normal cases.
        // We do NOT call ResetWpfLifecycleForTest here because resetting
        // _appIsShutdown=false while the Dispatcher may be in a stale
        // state can cause testhost to crash when Close() interacts with
        // WPF internals. If auto-shutdown does occur, the next test's
        // TestSetupIsolatedSave will reset _appIsShutdown=false.
        foreach (var w in _petWindows.Values.ToList())
        {
            if (w.Dispatcher.CheckAccess())
            {
                w.Closed -= OnPetWindowClosed;
                // Close even if not visible — OnClosed stops _updateTimer.
                // Close() is safe on an already-closed window (no-op).
                w.Close();
            }
        }
        _petWindows.Clear();

        _saveCoordinator?.Dispose();
        _saveCoordinator = null;
        _archive = null;
        _tray?.Dispose();
        _tray = null;
        _instanceGuard?.Dispose();
        _instanceGuard = null;
        // P4-001: Dispose the activity session service.
        ActivitySessionService?.Dispose();
        ActivitySessionService = null;
        // P6-001: Dispose the usage-stats service (Dispose → Stop → persist
        // pending + retention prune before the isolated dir is discarded).
        UsageStatsService?.Dispose();
        UsageStatsService = null;
        // PREP-001: Clean up diagnostic logger.
        DiagnosticLogger?.Dispose();
        DiagnosticLogger = null;
    }

    /// <summary>
    /// P2-003 S3 R6 F-08: Test-only injection of a MainWindow so tests can
    /// exercise the real FlushFinalState → MarkStateDirty → SaveCoordinator
    /// chain and the real RestoreWindowPosition recovery entry point,
    /// without re-implementing save/restore logic.
    /// P3-001-S2: Now registers the window in the multi-pet dictionary
    /// using the window's StateContext.PetId (or "bear-1" default).
    /// </summary>
    internal void TestSetMainWindow(MainWindow w)
    {
        var petId = w.StateContext?.PetId ?? "bear-1";
        _petWindows[petId] = w;
        _petWindowsDispatcher = w.Dispatcher;
    }

    /// <summary>
    /// P3-001-S2: Find a PetInstanceArchive by PetId. Returns null if not
    /// found or state is null.
    /// </summary>
    internal PetInstanceArchive? FindPetById(string? petId)
    {
        if (_state?.Pets is null || string.IsNullOrEmpty(petId))
            return null;
        foreach (var p in _state.Pets)
        {
            if (p is not null && p.PetId == petId)
                return p;
        }
        return null;
    }

    /// <summary>
    /// P3-001-S2: Find the MainWindow associated with a PetId.
    /// Returns null if not found.
    /// </summary>
    internal MainWindow? FindWindowByPetId(string? petId)
    {
        if (string.IsNullOrEmpty(petId))
            return _petWindows.Count > 0 ? _petWindows.First().Value : null;
        return _petWindows.TryGetValue(petId, out var w) ? w : null;
    }

    /// <summary>
    /// P3-001-S2: Enumerate all active pet windows. Used by pause/recover/
    /// exit/flush to iterate over every pet.
    /// </summary>
    internal IEnumerable<MainWindow> AllPetWindows => _petWindows.Values;

    /// <summary>
    /// P3-001-S2: Compatibility accessor for tests that expect a single
    /// MainWindow. Returns the first window or null.
    /// </summary>
    internal new MainWindow? MainWindow => _petWindows.Count > 0 ? _petWindows.First().Value : null;

    /// <summary>
    /// P2-003 S3 R6 F-08: Test-only accessor for the isolated state object
    /// so tests can verify the saved archive after FlushFinalState.
    /// </summary>
    internal PetStateArchive? TestState => _state;

    /// <summary>
    /// P3-001 S1: Test/probe-only — reloads _state from the isolated
    /// ArchiveService, triggering v3→v4 migration if the on-disk file
    /// is a pre-v4 archive.  This allows probes to pre-seed a state
    /// file and then load it through the production deserialization
    /// path before modifying in-memory fields.
    /// </summary>
    internal void TestReloadStateFromArchive()
    {
        if (_archive is null) return;
        var (state, _, _) = _archive.LoadState();
        _state = state;
    }

    /// <summary>
    /// F-05: Production mapping from PetStateArchive to emotion objects.
    /// This is the same logic used in OnStartup, extracted so tests can
    /// verify the real load→map→save→reload chain without copying code.
    /// P3-001-S2 R5 F-03: Now delegates to the shared per-pet mapping so
    /// there is exactly one restore algorithm, not two.
    /// </summary>
    internal static (EmotionDecayAccumulator acc, GrievanceExpressionState expr, CornerTracker tracker)
        MapArchiveToEmotionObjects(PetStateArchive state, DateTime? restoreUtcNow = null)
    {
        // v4: per-pet emotion fields live on pets[0] (single-pet S1 scope).
        // F-02 R1: Defensive null check — EnsureDefaultPetList in ArchiveService
        // filters null entries, but mapping should not crash if called with
        // a state that bypassed that path (e.g. test construction).
        var pet = state.Pets is not null && state.Pets.Count > 0 && state.Pets[0] is not null
            ? state.Pets[0]
            : new PetInstanceArchive();

        return MapArchiveToEmotionObjectsForPet(pet, state.IsPaused, restoreUtcNow);
    }

    /// <summary>
    /// F-05: Production mapping from emotion objects to PetStateArchive.
    /// This is the same logic used in MarkStateDirty, extracted so tests
    /// can verify the save path without a full App instance.
    /// P3-001-S2: Now accepts the target PetInstanceArchive directly so
    /// multi-pet callers can route to the correct pet. The old single-pet
    /// overload delegates to pets[0] for backward compatibility.
    /// </summary>
    internal static void MapEmotionObjectsToArchive(
        PetStateArchive state, EmotionDecayAccumulator acc, GrievanceExpressionState expr)
    {
        // v4: per-pet emotion fields live on pets[0] (single-pet S1 scope).
        // F-02 R1: Filter null entries defensively.
        if (state.Pets is null || state.Pets.Count == 0)
            state.Pets = new List<PetInstanceArchive> { new PetInstanceArchive() };
        else
            state.Pets.RemoveAll(p => p is null);
        if (state.Pets.Count == 0)
            state.Pets = new List<PetInstanceArchive> { new PetInstanceArchive() };
        MapEmotionObjectsToArchive(state, state.Pets[0], acc, expr);
    }

    /// <summary>
    /// P3-001-S2: Per-pet emotion mapping. Writes emotion fields directly
    /// to the specified PetInstanceArchive without touching the Pets list.
    /// </summary>
    internal static void MapEmotionObjectsToArchive(
        PetStateArchive state, PetInstanceArchive pet,
        EmotionDecayAccumulator acc, GrievanceExpressionState expr)
    {
        pet.Grievance = acc.GrievanceInt;
        pet.LastDecayUtc = acc.LastDecayUtc.ToString("o");
        pet.PendingGrievanceExpression = expr.IsPending;
        pet.GrievanceExpressionDate = expr.MarkedDate;
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

        // P3-002: Create the social coordinator for mutual-notice sessions.
        SocialCoordinator = new SocialCoordinator();

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

        // P3-001-S2: Create one MainWindow per pet in the archive.
        // Each pet gets its own emotion objects (EmotionDecayAccumulator,
        // GrievanceExpressionState, CornerTracker) so that dragging,
        // throwing, or changing grievance on one pet does not affect the
        // other. Rare-event objects (RuntimeTracker, RareEventScheduler)
        // are global/shared — same instance passed to every window.
        //
        // S2 scope: we create windows for all pets in the archive but do
        // NOT implement add/remove UI or social behavior. The archive may
        // contain 1 or 2 pets (max 2 enforced by S1 migration).
        EnsurePetListForWindows();
        foreach (var pet in _state.Pets)
        {
            if (pet is null) continue;
            CreatePetWindow(pet);
        }

        // Set up tray.
        _tray = new TrayService();
        _tray.SetPaused(_isPaused);
        _tray.PauseToggled += OnPauseToggled;
        _tray.RecoverRequested += OnRecoverRequested;
        _tray.ExitRequested += OnExitRequested;
        _tray.AddPetRequested += OnAddPetRequested;

        foreach (var w in _petWindows.Values)
            w.Show();
        if (stateUnknown || settingsUnknown)
            _tray.ShowReadOnlyArchiveNotice();

        // P4-001: Start the global activity session service after all pet
        // windows are shown. The service samples input idle and system
        // lifecycle events, driving the Core ActivitySession for rest-
        // reminder timing. Independent of rare-event timing.
        // F-04 R4: Use the shared initialization method so production
        // and isolated probe entry points share identical wiring.
        InitializeActivitySession();

        // P6-001: Start the independent usage-stats service. Per R-09 the
        // collector stays disabled unless the persisted settings toggle
        // (UsageStatsEnabled, default false) says otherwise; only then is
        // the app-start observation registered (once per process startup).
        InitializeUsageStats();

        // P6-004（第一小步）：低频主动气泡判定。只在有达标且匹配当前时段
        // 候选、且无抑制条件时展示，其余情况保持安静。
        InitializeUsageStatsBubbleTimer();
    }

    /// <summary>
    /// P4-001: Subscribes to system-level session switch (lock/unlock) and
    /// power mode (sleep/resume) events. These are Microsoft.Win32.SystemEvents
    /// which fire on a system thread.
    /// <para>
    /// F-02 R2: The handlers call ActivitySessionService.OnSessionSwitch /
    /// OnPowerModeChanged, which marshal to the Dispatcher thread via
    /// BeginInvoke. This ensures lifecycle flag changes, suspension updates,
    /// and timer ticks are all serialized on a single thread — no
    /// unsynchronized cross-thread access to the service state.
    /// </para>
    /// </summary>
    private void SubscribeSystemEvents()
    {
        if (ActivitySessionService is null) return;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSystemSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;
        _systemEventsSubscribed = true;
    }

    /// <summary>
    /// P4-001: Unsubscribes system event handlers. Called on shutdown/exit
    /// to release the system event subscriptions.
    /// </summary>
    private void UnsubscribeSystemEvents()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSystemSessionSwitch;
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnSystemPowerModeChanged;
        _systemEventsSubscribed = false;
    }

    // P4-001 F-04 R3: Track whether system event subscriptions are active.
    // Used by the isolation probe to verify that ShutdownMainWindow
    // releases the system event subscriptions.
    private bool _systemEventsSubscribed;

    /// <summary>
    /// P4-001 F-04 R3: Exposes whether system event subscriptions are
    /// currently active. For isolation probe verification of exit cleanup.
    /// </summary>
    internal bool TestSystemEventsSubscribed => _systemEventsSubscribed;

    /// <summary>
    /// P4-001 F-04 R4: Shared activity-session initialization used by
    /// both the production <see cref="OnStartup"/> entry point and the
    /// isolated probe entry point (<see cref="StartForIsolatedProbe"/>).
    /// This ensures both paths use identical wiring:
    /// 1. Create the service (same default constructor).
    /// 2. Subscribe system events (same method).
    /// 3. Start with current pause state (same call).
    /// F-02 R1: Pass the current pause state so a paused archive
    /// does not start the activity timer.
    /// </summary>
    private void InitializeActivitySession()
    {
        ActivitySessionService = new ActivitySessionService();
        // P4-002: Subscribe to the rest-reminder event to trigger the
        // rest-reminder performance on an available pet window.
        ActivitySessionService.RestReminderRequested += OnRestReminderRequested;
        SubscribeSystemEvents();
        ActivitySessionService.Start(_isPaused);
    }

    /// <summary>
    /// P6-001: Creates and starts the usage-stats service. R-09: statistics
    /// are disabled by default — the collector only turns on when the
    /// persisted toggle (<see cref="PetSettingsArchive.UsageStatsEnabled"/>)
    /// is true, and only then is the app-start observation registered
    /// (once per process startup). Existing in-retention statistics are
    /// restored and kept even while disabled.
    /// </summary>
    private void InitializeUsageStats(string? saveDirectory = null)
    {
        UsageStatsService = new UsageStatsService(saveDirectory);
        UsageStatsService.Start();

        // F-01 R2: sync the initial pause state UNCONDITIONALLY — also when
        // the toggle is off (statistics are disabled by default). _isPaused
        // is initialized from the persisted pause toggle before this point;
        // the collector stays suspended until the user unpauses, so enabling
        // statistics later while the pet is globally paused keeps sampling
        // suspended instead of starting immediately.
        if (_isPaused)
        {
            UsageStatsService.OnPauseToggled(true);
        }

        if (_settings is not null && _settings.UsageStatsEnabled)
        {
            UsageStatsService.Enable();
            UsageStatsService.OnAppStart();
        }
    }

    /// <summary>
    /// P6-001 R4: Isolated-probe entry into the SAME production
    /// initialization used by <see cref="OnStartup"/> (real 5-second
    /// DispatcherTimer, real idle/foreground samplers, pause sync, settings
    /// gate), with a caller-chosen storage directory — the probe's isolated
    /// run directory, never the user archive. Production logic is unchanged;
    /// this only exposes the private <see cref="InitializeUsageStats"/> to
    /// the diagnostic probe (InternalsVisibleTo).
    /// </summary>
    internal void InitializeUsageStatsForIsolatedProbe(string saveDirectory)
    {
        InitializeUsageStats(saveDirectory);
    }

    /// <summary>
    /// P6-001 R5 F-05: Loads the isolated directory's persisted settings/state
    /// through the SAME production startup path (LoadState → LoadSettings →
    /// <c>_isPaused = _state.IsPaused</c>), without seed-overwriting what is
    /// already on disk. A first-run directory (no files) yields the defaults;
    /// a restart loads the existing independent settings/state so the toggle
    /// and pet state survive — never the user archive (the caller's isolated
    /// <see cref="ArchiveService"/> is used).
    /// </summary>
    internal void LoadArchivesForIsolatedProbe()
    {
        if (_archive is null) return;
        var (state, _, _) = _archive.LoadState();
        var (settings, _, _) = _archive.LoadSettings();
        _state = state;
        _settings = settings;
        _isPaused = _state.IsPaused;
    }

    /// <summary>
    /// P6-001 R5 F-05: Read-only accessor so the probe can assert what was
    /// actually loaded from disk (e.g. <c>UsageStatsEnabled</c> persisted by
    /// the previous run).
    /// </summary>
    internal PetSettingsArchive? TestSettings => _settings;

    /// <summary>
    /// P6-001 R5 F-05: Fills the runtime helper objects with the SAME
    /// defaults <see cref="TestSetupIsolatedSave"/> uses, WITHOUT overwriting
    /// <c>_state</c>/<c>_settings</c>/<c>_isPaused</c> that were just loaded
    /// from disk. Intended for the restart branch of isolated probes: first
    /// <see cref="LoadArchivesForIsolatedProbe"/> restores the persisted
    /// settings/state, then this call provides the side objects the pet
    /// window needs (rare-event trackers, social coordinator, activity
    /// session, emotion objects) without losing the loaded archives.
    /// </summary>
    internal void TestEnsureProbeRuntimeObjects()
    {
        RuntimeTracker ??= new EffectiveRuntimeTracker();
        RareEventScheduler ??= new RareEventScheduler();
        SocialCoordinator ??= new SocialCoordinator();
        ActivitySessionService ??= new ActivitySessionService();
        EmotionAccumulator ??= new EmotionDecayAccumulator(50, DateTime.UtcNow);
        GrievanceExpression ??= new GrievanceExpressionState();
        CornerTracking ??= new CornerTracker();
    }

    /// <summary>
    /// P4-001 F-04 R3: Starts the activity session through the same
    /// production wiring used by OnStartup, but without creating windows,
    /// tray, or single-instance guard. F-04 R4: delegates to the shared
    /// <see cref="InitializeActivitySession"/> so probe and production
    /// use identical wiring.
    /// </summary>
    internal void StartForIsolatedProbe(string isolatedDir)
    {
        InitializeActivitySession();
    }

    /// <summary>
    /// F-04 R4: Overload that accepts a pre-created ActivitySessionService
    /// with injected dependencies (e.g. a controllable InputIdleSampler).
    /// The service is wired through the same SubscribeSystemEvents +
    /// Start path as production, but the caller controls the service
    /// instance. This lets the probe use controlled input without
    /// duplicating the subscription/start wiring.
    /// </summary>
    internal void StartForIsolatedProbe(string isolatedDir, ActivitySessionService service)
    {
        ActivitySessionService = service;
        SubscribeSystemEvents();
        ActivitySessionService.Start(_isPaused);
    }

    internal void OnSystemSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        // R2 F-01: Microsoft.Win32.SystemEvents fire on a system thread.
        // The activity-session timing notification marshals to its
        // Dispatcher inside ActivitySessionService. The performance
        // cancellation below is marshalled to each pet window's
        // Dispatcher — in production that IS the App Dispatcher (all
        // windows live on the single UI thread), so the notification and
        // the cancellation are serialized on one thread and
        // CancelRestReminder → UpdateFrame never crosses threads.
        ActivitySessionService?.OnSessionSwitch((int)e.Reason);

        // P6-001: Same lifecycle feed for usage stats — the stats service
        // marshals internally to its Dispatcher (lock/sleep suspend the
        // current segment; unlock/resume record their own event).
        UsageStatsService?.OnSessionSwitch((int)e.Reason);

        // P4-002 R1 F-01: Lock screen cancels rest-reminder performances.
        // SessionSwitchReason.SessionLock = 7. Unlock (8) does NOT replay
        // — the performance was already cancelled when lock started.
        if ((int)e.Reason == 7)
        {
            // P6-004（第一小步）：锁屏期间不展示主动气泡，立即撤销已有的；
            // 解锁后不补播积压。
            HideStatsBubbleOnUiDispatcher();

            // R2 F-01 收尾: the collection read, the snapshot and the
            // cancels all happen on the UI Dispatcher (see
            // CancelRestRemindersOnUiDispatcher).
            CancelRestRemindersOnUiDispatcher();
        }
    }

    internal void OnSystemPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        // R2 F-01: System events fire on a system thread. Same
        // per-window-Dispatcher marshalling as OnSystemSessionSwitch
        // (see above).
        ActivitySessionService?.OnPowerModeChanged((int)e.Mode);

        // P6-001: Same power-mode feed for usage stats (sleep suspends the
        // current segment; resume records its own event and rebuilds the
        // baseline). Marshaled internally to the stats service Dispatcher.
        UsageStatsService?.OnPowerModeChanged((int)e.Mode);

        // P4-002 R1 F-01: Sleep (Suspend) cancels rest-reminder
        // performances. PowerModes.Suspend = 3. Resume (1) does NOT
        // replay — the performance was already cancelled when sleep
        // started.
        if ((int)e.Mode == (int)Microsoft.Win32.PowerModes.Suspend)
        {
            // P6-004（第一小步）：睡眠期间不展示主动气泡，立即撤销已有的；
            // 唤醒后不补播积压。
            HideStatsBubbleOnUiDispatcher();

            // R2 F-01 收尾: the collection read, the snapshot and the
            // cancels all happen on the UI Dispatcher (see
            // CancelRestRemindersOnUiDispatcher).
            CancelRestRemindersOnUiDispatcher();
        }
    }

    /// <summary>
    /// P4-002 R2 F-01 收尾: Cancel every active rest-reminder performance
    /// on the UI Dispatcher that owns the pet-window collection. The
    /// collection read, the snapshot (<c>ToList</c>) and the cancels are
    /// one unit marshalled to that Dispatcher, so they are serialized
    /// with window creation/removal (CreatePetWindow /
    /// OnPetWindowClosed / RemovePet): a window removed or closed while
    /// the notification is queued is simply absent from the snapshot —
    /// no cross-thread enumeration race and no cancel on a closed
    /// window.
    /// <para>
    /// The system SessionSwitch/PowerModeChanged events fire on a system
    /// thread; in production this method queues the whole unit onto the
    /// single App UI thread (the same thread the windows live on, so the
    /// notification and the cancellation stay serialized).
    /// </para>
    /// </summary>
    internal void CancelRestRemindersOnUiDispatcher()
    {
        var d = _petWindowsDispatcher;
        if (d is null || d.CheckAccess())
        {
            // Already on the UI Dispatcher (or no window was ever
            // created — the core iterates an empty collection).
            CancelRestRemindersCore();
        }
        else
        {
            // Background/system thread: marshal the WHOLE unit —
            // collection read, snapshot and cancel — onto the UI
            // Dispatcher. The snapshot is taken at execution time, not
            // at queue time.
            d.BeginInvoke(new Action(CancelRestRemindersCore));
        }
    }

    private void CancelRestRemindersCore()
    {
        // R2 F-01 收尾: executed on the UI Dispatcher. The collection is
        // read here, at execution time, so windows closed/removed while
        // the cancellation was queued are absent, and windows created
        // since then are included.
        foreach (var w in _petWindows.Values.ToList())
            w.CancelRestReminder();
    }

    /// <summary>
    /// P4-002: Handle the rest-reminder event from ActivitySessionService.
    /// Selects an available (idle, visible, not busy) pet window and starts
    /// the rest-reminder performance. If no suitable window is found, the
    /// reminder is silently skipped (R-06: "没有合适的宠物时略过本次").
    /// <para>
    /// Window selection priority (R-06 P4-002):
    /// 1. First idle, visible, non-busy window (prefer the first pet).
    /// 2. If the first pet is busy, try the second pet.
    /// 3. If no window is available, skip.
    /// </para>
    /// <para>
    /// "Busy" means: dragging, petting, physics active, grievance expression,
    /// probe active, social active, app paused, or already performing a
    /// rest reminder.
    /// </para>
    /// </summary>
    internal void OnRestReminderRequested(object? sender, EventArgs e)
    {
        // Find an available window for the performance.
        MainWindow? selected = null;
        foreach (var w in _petWindows.Values)
        {
            if (w.CanStartRestReminder())
            {
                selected = w;
                break;
            }
        }

        if (selected is null)
        {
            // No suitable window — silently skip this reminder.
            System.Diagnostics.Trace.TraceInformation(
                "P4-002: Rest reminder skipped — no available pet window.");
            return;
        }

        selected.StartRestReminder(MonotonicClock.Instance.GetElapsedMs());
    }

    /// <summary>
    /// P3-001-S2: Ensure the state has a valid pet list for window creation.
    /// Filters nulls and ensures at least one pet exists.
    /// </summary>
    private void EnsurePetListForWindows()
    {
        if (_state is null) return;
        if (_state.Pets is null || _state.Pets.Count == 0)
            _state.Pets = new List<PetInstanceArchive> { new PetInstanceArchive() };
        else
            _state.Pets.RemoveAll(p => p is null);
        if (_state.Pets.Count == 0)
            _state.Pets = new List<PetInstanceArchive> { new PetInstanceArchive() };
    }

    /// <summary>
    /// P3-001-S2: Create a MainWindow for a single pet, with independent
    /// emotion objects and the shared rare-event/global objects.
    /// The window is registered in _petWindows keyed by PetId.
    /// P3-001-S2 R6 F-04: Exposed as internal so tests can verify the
    /// production factory correctly injects/registers two instances,
    /// rather than hand-writing substitute contexts.
    /// </summary>
    internal void CreatePetWindow(PetInstanceArchive pet)
    {
        var window = new MainWindow();

        // P3-001 F-08 R14: Set personality for the visual identification badge.
        // Proactive (first pet) = blue circle, SlowPaced (second pet) = orange triangle.
        // Must be set before Show() so the badge appears on the first frame.
        window.Personality = pet.Personality;

        // P3-001-S2: Each pet gets independent emotion objects restored
        // from its own archive fields. This is the key isolation point:
        // pet A's grievance/drag/throw must not affect pet B.
        // P3-001-S2 R5 F-03: Pass _isPaused so the per-pet mapping applies
        // the same pause-exit freeze semantics as the legacy entry point.
        var (emotionAcc, grievanceExpr, cornerTrack) = MapArchiveToEmotionObjectsForPet(pet, _isPaused);

        window.StateContext = new PetStateContext
        {
            PetId = pet.PetId,
            EmotionAccumulator = emotionAcc,
            CornerTracking = cornerTrack,
            GrievanceExpression = grievanceExpr,
            // Rare events are global — shared across all pets.
            RuntimeTracker = RuntimeTracker,
            RareEventScheduler = RareEventScheduler,
            // P3-002: Social coordinator is global — shared across all pets.
            SocialCoordinator = SocialCoordinator,
            MarkStateDirty = MarkStateDirty,
        };

        if (_isPaused)
            window.PauseAnimation();

        // HWND/DPI are available here, before the first visible frame.
        // Never assign an unvalidated saved position directly to the window.
        window.SourceInitialized += (_, _) => RestoreWindowPosition(pet.PetId);
        window.Closed += OnPetWindowClosed;

        _petWindows[pet.PetId] = window;
        _petWindowsDispatcher = window.Dispatcher;
    }

    /// <summary>
    /// P3-001-S3: Add a second pet. Enforces max-2 limit, generates a
    /// stable PetId that doesn't collide with existing pets, assigns
    /// SlowPaced personality (the first pet defaults to Proactive), and
    /// places the new pet at a legal position that doesn't fully overlap
    /// the existing pet. The new window is shown immediately.
    /// Returns true if a pet was added, false if already at max or state
    /// is unavailable.
    /// </summary>
    internal bool AddPet()
    {
        if (_state is null)
            return false;
        if (_state.Pets is null)
            _state.Pets = new List<PetInstanceArchive>();
        // R-05: max 2 pets.
        if (_state.Pets.Count >= 2)
            return false;

        // F-06 R9: Generate a unique PetId that is never reused.
        // New pets get a GUID-based ID to avoid reusing IDs of
        // previously removed pets. The original first pet keeps its
        // existing ID (e.g. "bear-1" from archive migration).
        var newPetId = $"bear-{Guid.NewGuid():N}";

        // F-07 R10: Determine a legal initial position using the real
        // monitor work area (DIP), not a hardcoded 1800 assumption.
        // Offset by 200 DIP to the right; if the full window (Left+Width)
        // would exceed the work area right edge, offset to the left.
        // Also verify Top+Height fits within the work area.
        double newLeft = 100;
        double newTop = 100;
        if (_state.Pets.Count == 1 && _state.Pets[0] is { } first)
        {
            newLeft = first.WindowLeft + 200;
            newTop = first.WindowTop;
            // Use the primary monitor work area to validate.
            var monitors = MonitorInterop.EnumerateMonitors();
            if (monitors.Count > 0)
            {
                var primary = DesktopPet.Core.Geometry.MonitorLocator.FindPrimaryMonitor(monitors);
                var wa = primary.WorkArea.Bounds;
                var waRightDip = wa.Right / primary.DpiScaleX;
                var waLeftDip = wa.Left / primary.DpiScaleX;
                var waBottomDip = wa.Bottom / primary.DpiScaleY;
                var waTopDip = wa.Top / primary.DpiScaleY;
                const double petWinWidth = 120.0;
                const double petWinHeight = 120.0;
                // If right offset goes off-screen, try left offset.
                if (newLeft + petWinWidth > waRightDip)
                    newLeft = first.WindowLeft - 200;
                // Clamp left edge to work area.
                if (newLeft < waLeftDip)
                    newLeft = waLeftDip + 10;
                // Clamp top so the full window fits.
                if (newTop + petWinHeight > waBottomDip)
                    newTop = waBottomDip - petWinHeight - 10;
                if (newTop < waTopDip)
                    newTop = waTopDip + 10;
            }
            else
            {
                // Fallback: if no monitors enumerated, use a simple bound.
                if (newLeft > 1800)
                    newLeft = first.WindowLeft - 200;
            }
        }

        var newPet = new PetInstanceArchive
        {
            PetId = newPetId,
            Personality = Personality.SlowPaced,
            WindowLeft = newLeft,
            WindowTop = newTop,
            Grievance = 50,
            LastDecayUtc = DateTime.UtcNow.ToString("o"),
        };

        _state.Pets.Add(newPet);
        CreatePetWindow(newPet);

        // Show the new window (OnStartup shows all windows, but AddPet
        // is called at runtime after startup).
        if (_petWindows.TryGetValue(newPetId, out var w))
        {
            w.Show();
            if (_isPaused)
                w.PauseAnimation();
        }

        MarkStateDirty(newPetId);
        return true;
    }

    /// <summary>
    /// P3-001-S3: Remove a pet by PetId. Closes and releases the window,
    /// removes it from the state archive, and saves.
    /// F-05 R9: Only the second pet (non-original) can be removed.
    /// The original first pet (Pets[0]) is always protected (R-05:
    /// at least one pet must remain; the original pet is never removed
    /// via this method). Removing the last pet is handled by global
    /// exit (Exit menu / tray Exit), not by RemovePet.
    /// Returns true if a pet was removed, false if not found or protected.
    /// </summary>
    internal bool RemovePet(string petId)
    {
        if (_state is null || string.IsNullOrEmpty(petId))
            return false;
        if (!_petWindows.TryGetValue(petId, out var window))
            return false;

        // F-05 R9: Protect the original first pet. Only the second pet
        // (added via AddPet) can be removed.
        if (_state.Pets is not null && _state.Pets.Count > 0 &&
            _state.Pets[0] is { } firstPet && firstPet.PetId == petId)
        {
            return false;
        }

        // P4-002 R1 F-02: Cancel any active rest-reminder performance
        // before closing the window, so the animation player is released.
        window.CancelRestReminder();

        // Detach the Closed handler so closing this window doesn't
        // trigger OnPetWindowClosed → removal again (re-entrancy).
        window.Closed -= OnPetWindowClosed;
        window.Close();
        _petWindows.Remove(petId);

        // P3-002: Cancel any active social session since a participant was removed.
        // F-02 R1: Route through the surviving window's CancelSocialImmediately
        // so that BOTH the coordinator ForceCancel AND both windows' local
        // _isSocialActive/_curiosity are synchronously cleared — no tick needed.
        // F-03 R1: Use the surviving window's clock (TestGetElapsedMs in tests,
        // GetElapsedMs in production) so the cooldown base time is in the SAME
        // monotonic domain as the coordinator's Tick calls. Using
        // MonotonicClock.Instance directly would create a mismatch when tests
        // inject a controlled clock, causing the cooldown to expire instantly.
        foreach (var surviving in _petWindows.Values)
        {
            if (surviving.StateContext?.PetId != petId)
                surviving.CancelSocialImmediately(CancelReason.PetRemoved,
                    surviving.TestGetElapsedMs?.Invoke() ?? MonotonicClock.Instance.GetElapsedMs());
        }

        // Remove from the archive state.
        _state.Pets?.RemoveAll(p => p is not null && p.PetId == petId);

        // Save the updated state (without the removed pet).
        // F-05 R9: No shutdown here — removing the last pet is only
        // possible via global exit, which is a separate path.
        MarkStateDirty();
        return true;
    }

    /// <summary>
    /// P3-001-S2: Per-pet emotion mapping. Creates independent emotion
    /// objects from a single PetInstanceArchive's fields.
    /// P3-001-S2 R5 F-03: This is the single shared restore algorithm.
    /// Both the legacy MapArchiveToEmotionObjects and CreatePetWindow
    /// delegate here, so pause-exit freeze semantics and callback
    /// protection cannot drift between the two entry points.
    /// </summary>
    internal static (EmotionDecayAccumulator acc, GrievanceExpressionState expr, CornerTracker tracker)
        MapArchiveToEmotionObjectsForPet(PetInstanceArchive pet, bool isPaused = false, DateTime? restoreUtcNow = null)
    {
        var utcNow = restoreUtcNow ?? DateTime.UtcNow;

        var lastDecayUtc = DateTime.TryParse(pet.LastDecayUtc, null,
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
        if (isPaused && utcNow > lastDecayUtc)
        {
            lastDecayUtc = utcNow;
        }

        var acc = new EmotionDecayAccumulator(pet.Grievance, lastDecayUtc);
        var expr = new GrievanceExpressionState();
        expr.Restore(pet.PendingGrievanceExpression, pet.GrievanceExpressionDate);
        var tracker = new CornerTracker();

        return (acc, expr, tracker);
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
    // P3-001-S2: Now accepts petId to route to the correct pet's window.
    // The parameterless overload restores the first pet (backward compat).
    internal void RestoreWindowPosition() => RestoreWindowPosition(null);

    internal void RestoreWindowPosition(string? petId)
    {
        if (_state is null) return;

        var pet = FindPetById(petId);
        if (pet is null && _state.Pets is not null && _state.Pets.Count > 0)
            pet = _state.Pets[0];
        if (pet is null) return;

        var window = FindWindowByPetId(pet.PetId);
        if (window is null) return;

        // P3-003 R3 F-04: Use TestMonitorList when available (same as
        // BuildPetSocialState and UpdatePhysics) so test-injected monitors
        // with controlled DPI are used for position recovery, keeping
        // restore consistent with the test environment.
        var monitors = window.TestMonitorList ?? MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return; // Keep the constructor's visible default, not stale archive coordinates.
        var sourceMonitor = monitors.FirstOrDefault(m => m.DeviceName == pet.SavedDeviceName);
        if (sourceMonitor.DeviceName is null)
            sourceMonitor = DesktopPet.Core.Geometry.MonitorLocator.FindPrimaryMonitor(monitors);
        var left = double.IsFinite(pet.WindowLeft) ? pet.WindowLeft : 100;
        var top = double.IsFinite(pet.WindowTop) ? pet.WindowTop : 100;
        var restored = WindowPositionAdapter.RecoverPosition(monitors, left, top,
            window.Width, window.Height, sourceMonitor.DpiScaleX,
            sourceMonitor.DpiScaleY, pet.SavedDeviceName);
        window.Left = restored.LeftDip;
        window.Top = restored.TopDip;
    }

    /// <summary>
    /// P4-002 R2 M-12: Read-only accessor for the global pause state.
    /// Used by the window right-click menu to label its Pause/Resume
    /// entry (the tray is not available in the isolated probe).
    /// </summary>
    internal bool IsGloballyPaused => _isPaused;

    /// <summary>
    /// P6-001: Whether usage-statistics collection is currently enabled.
    /// Mirrors the persisted settings toggle; used by the window menu to
    /// label the Statistics toggle entry.
    /// </summary>
    internal bool IsUsageStatsEnabled => UsageStatsService?.IsEnabled ?? false;

    /// <summary>
    /// P6-001: Enables or disables local usage statistics. The toggle is
    /// persisted in settings (kept by Clear) and applied to the stats
    /// service. Statistics are OFF by default — only this explicit user
    /// action turns collection on.
    /// </summary>
    internal void SetUsageStatsEnabled(bool enabled)
    {
        if (_settings is not null)
        {
            _settings.UsageStatsEnabled = enabled;
            _saveCoordinator?.MarkSettingsDirty(_settings);
        }

        var service = UsageStatsService;
        if (service is null) return;
        if (enabled)
        {
            service.Enable();
            // F-01 R2: the pet may already be globally paused while the user
            // turns statistics on (e.g. pause-persisted start, or pause
            // before this call) — keep sampling suspended until unpaused.
            // Idempotent: InitializeUsageStats already synced a paused start.
            if (_isPaused)
            {
                service.OnPauseToggled(true);
            }
        }
        else
        {
            service.Disable();
            // P6-004（第一小步）：统计关闭后立即撤销相关主动气泡与待展示
            // 内容（判定每次即时计算，不存在排队）；查看窗如打开则刷新为
            // 关闭状态。
            HideUsageStatsBubble();
            if (_statsViewer is { IsVisible: true })
                _statsViewer.Refresh();
        }
    }

    /// <summary>
    /// P6-001: Clears all collected statistics (segments, events, pending
    /// writes, coverage-mismatch count) and immediately persists the cleared
    /// state. The enable toggle and settings are preserved — only the
    /// statistics are removed. Old asynchronous writes / backups cannot
    /// resurrect cleared data (collector generation guard).
    /// </summary>
    internal void ClearUsageStats()
    {
        UsageStatsService?.ClearAll();
        // P6-004（第一小步）：清除后立即撤销气泡（当天已展示标记由服务
        // 一并重置），查看窗如打开则立即刷新为新记录状态。
        HideUsageStatsBubble();
        if (_statsViewer is { IsVisible: true })
            _statsViewer.Refresh();
    }

    /// <summary>
    /// P6-001: Test-only injection of the usage-stats service so App-level
    /// tests can wire controlled samplers/clock and an isolated stats file.
    /// Disposes any previously created service.
    /// </summary>
    internal void SetUsageStatsServiceForTest(UsageStatsService? service)
    {
        UsageStatsService?.Dispose();
        UsageStatsService = service;
    }

    // ── P6-004（第一小步）：主动气泡与查看入口 ──────────────────────────

    /// <summary>
    /// P6-004（第一小步）：启动主动气泡判定定时器。频率很低（30 秒），每次
    /// 只做一次纯判定；没有达标候选或处于抑制状态时保持安静。生产在
    /// <see cref="OnStartup"/> 中调用；隔离预览可自行触发
    /// <see cref="TryShowUsageStatsBubble"/>。
    /// </summary>
    private void InitializeUsageStatsBubbleTimer()
    {
        if (_statsBubbleTimer is not null) return;
        _statsBubbleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _statsBubbleTimer.Tick += (_, _) => TryShowUsageStatsBubble();
        _statsBubbleTimer.Start();
    }

    /// <summary>
    /// P6-004（第一小步）：一次主动气泡判定与展示。规则全部在 Core：
    /// 只使用 P6-003 达标且匹配当前半小时时段的候选；无候选保持安静（不提示
    /// “样本不足”）；暂停/锁屏/睡眠、无可接受展示的空闲宠物、当天已展示过
    /// 都不展示，也不消费当天额度。
    /// </summary>
    internal void TryShowUsageStatsBubble()
    {
        if (_isShuttingDown) return;
        var service = UsageStatsService;
        if (service is null) return;

        var pet = SelectStatsBubblePet();
        var now = (TestStatsBubbleLocalNow ?? (() => DateTimeOffset.Now))();
        var decision = UsageStatsBubblePlanner.Decide(
            service.GetFrequencyPredictions(),
            now,
            service.BubbleLastShownLocalDate,
            service.IsSuspended,
            pet is not null);

        if (!decision.ShouldShow || pet is null)
            return;

        var bubble = _statsBubble ??= CreateStatsBubbleWindow();
        bubble.ShowNear(
            decision.Message,
            pet.GetPetRectDipForStatsBubble(),
            pet.GetWorkAreaDipForStatsBubble());

        // 仅在真正展示后记录当天额度（持久化，跨重启同样去重）。
        service.RecordStatsBubbleShown(decision.LocalDate);
    }

    /// <summary>
    /// P6-004（第一小步）：多宠时只选一只“可接受展示”的空闲宠物，按
    /// PetId 排序保证确定性；其余宠物不展示。没有空闲宠物时返回 null，
    /// 且不消费当天额度。
    /// </summary>
    private MainWindow? SelectStatsBubblePet()
    {
        foreach (var pair in _petWindows.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var window = pair.Value;
            if (window is not null && window.IsAvailableForStatsBubble)
                return window;
        }
        return null;
    }

    private StatsBubbleWindow CreateStatsBubbleWindow()
    {
        var bubble = new StatsBubbleWindow();
        bubble.Closed += (_, _) =>
        {
            if (ReferenceEquals(_statsBubble, bubble))
                _statsBubble = null;
        };
        return bubble;
    }

    /// <summary>
    /// P6-004（第一小步）：立即隐藏当前主动气泡（暂停/锁屏/睡眠/统计关闭或
    /// 清除）。恢复后不补播积压——后续展示只由下一次判定按当时状态决定。
    /// </summary>
    internal void HideUsageStatsBubble()
    {
        _statsBubble?.HideBubble();
    }

    /// <summary>在 UI Dispatcher 上隐藏气泡（系统事件从系统线程触发）。</summary>
    private void HideStatsBubbleOnUiDispatcher()
    {
        var d = _petWindowsDispatcher;
        if (d is null || d.CheckAccess())
        {
            HideUsageStatsBubble();
        }
        else
        {
            d.BeginInvoke(new Action(HideUsageStatsBubble));
        }
    }

    /// <summary>
    /// P6-004（第一小步）：打开只读统计查看窗（右键菜单入口）。窗口分两段：
    /// 已记录的统计（R-09 准确名称）与有样本支持的时间偏好（标注为统计依据）。
    /// 统计关闭时仍可打开，如实显示未启用与已有记录。
    /// </summary>
    internal void ShowUsageStatsViewer()
    {
        if (_isShuttingDown) return;

        if (_statsViewer is { IsVisible: true })
        {
            _statsViewer.Refresh();
            _statsViewer.Activate();
            return;
        }

        var viewer = new StatsViewerWindow();
        viewer.SetProvider(BuildStatsViewSnapshot);
        viewer.Closed += (_, _) =>
        {
            if (ReferenceEquals(_statsViewer, viewer))
                _statsViewer = null;
        };
        _statsViewer = viewer;
        viewer.Show();
    }

    private StatsViewSnapshot BuildStatsViewSnapshot()
    {
        var service = UsageStatsService;
        if (service is null)
            return new StatsViewSnapshot { IsServiceAvailable = false };

        var preferenceLines = new List<string>();
        foreach (var candidate in service.GetFrequencyPredictions().Candidates)
            preferenceLines.Add(UsageStatsBubblePlanner.FormatPreferenceLine(candidate));

        return new StatsViewSnapshot
        {
            IsServiceAvailable = true,
            IsEnabled = service.IsEnabled,
            RecordedLines = UsageStatsBubblePlanner.FormatRecordedSummaryLines(service.GetRecordedSummary()),
            PreferenceLines = preferenceLines
        };
    }

    /// <summary>当前气泡是否可见（测试/隔离预览断言）。</summary>
    internal bool TestStatsBubbleVisible => _statsBubble?.IsVisible == true;

    /// <summary>当前气泡文案（测试/隔离预览断言）。</summary>
    internal string TestStatsBubbleMessage => _statsBubble?.CurrentMessage ?? "";

    /// <summary>当前气泡窗口实例（测试/隔离预览断言；未展示时为 null）。</summary>
    internal StatsBubbleWindow? TestStatsBubbleWindow => _statsBubble;

    /// <summary>只读查看窗实例（测试/隔离预览断言；未打开时为 null）。</summary>
    internal StatsViewerWindow? TestStatsViewerWindow => _statsViewer;

    internal void OnPauseToggled(object? sender, bool paused)
    {
        _isPaused = paused;
        if (_state is not null)
            _state.IsPaused = paused;
        _tray?.SetPaused(paused);

        // P4-001: Notify the activity session service so pause stops
        // accumulating active time. Resume starts from a new baseline.
        ActivitySessionService?.OnPauseToggled(paused);

        // P6-001: Pausing also suspends the usage-stats current segment
        // (the paused interval is never attributed); resume rebuilds the
        // baseline from the next sample without back-fill.
        UsageStatsService?.OnPauseToggled(paused);

        if (paused)
        {
            // P6-004（第一小步）：暂停期间不展示主动气泡，立即撤销已有的。
            HideUsageStatsBubble();

            // P4-002: Cancel any active rest-reminder performance on pause.
            // Per R-06: "暂停...时结束正在播放的提醒，恢复后不续播。"
            foreach (var w in _petWindows.Values)
                w.CancelRestReminder();

            // P3-001-S2: Pause all pet windows.
            foreach (var w in _petWindows.Values)
                w.PauseAnimation();
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
            // P3-001-S2: Resume all pet windows.
            foreach (var w in _petWindows.Values)
                w.ResumeAnimation();
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

    internal void OnRecoverRequested(object? sender, EventArgs e)
    {
        // P3-001-S2: Recover all pet windows.
        // P3-001-S3: When multiple pets exist, offset the second pet so
        // recovered positions do not fully overlap (R-05 requirement).
        // F-07 R9: After all position changes are complete, mark state
        // dirty so the final positions (including offsets) are saved.
        var windows = _petWindows.Values.ToList();
        for (int i = 0; i < windows.Count; i++)
        {
            windows[i].RecoverPet();
            if (i > 0)
            {
                // F-07 R10: Offset subsequent pets to the right by 200 DIP.
                // Check the FULL window boundary (Left+Width, Top+Height),
                // not just Left. If right offset goes off-screen, try left.
                // Then clamp both edges to the work area.
                var monitors = MonitorInterop.EnumerateMonitors();
                if (monitors.Count > 0)
                {
                    var primary = DesktopPet.Core.Geometry.MonitorLocator.FindPrimaryMonitor(monitors);
                    var wa = primary.WorkArea.Bounds;
                    // WorkArea.Bounds are in physical pixels; convert to DIP
                    // for comparison with window Left/Top (which are in DIP).
                    var waRightDip = wa.Right / primary.DpiScaleX;
                    var waLeftDip = wa.Left / primary.DpiScaleX;
                    var waBottomDip = wa.Bottom / primary.DpiScaleY;
                    var waTopDip = wa.Top / primary.DpiScaleY;
                    var newLeft = windows[i].Left + 200;
                    // Check full window: Left + Width must fit.
                    if (newLeft + windows[i].Width > waRightDip)
                        newLeft = windows[i].Left - 200;
                    // Clamp Left so the full window is within the work area.
                    if (newLeft + windows[i].Width > waRightDip)
                        newLeft = waRightDip - windows[i].Width - 10;
                    if (newLeft < waLeftDip)
                        newLeft = waLeftDip + 10;
                    windows[i].Left = newLeft;
                    // Also clamp Top so the full window height fits.
                    if (windows[i].Top + windows[i].Height > waBottomDip)
                        windows[i].Top = waBottomDip - windows[i].Height - 10;
                    if (windows[i].Top < waTopDip)
                        windows[i].Top = waTopDip + 10;
                }
            }
        }
        // F-07 R9: Save the final positions (including offsets) so they
        // are persisted. This must happen AFTER all position changes.
        MarkStateDirty();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        ShutdownMainWindow();
    }

    private void OnAddPetRequested(object? sender, EventArgs e)
    {
        AddPet();
    }

    private void OnPetWindowClosed(object? sender, EventArgs e)
    {
        // F-05 R9: During global shutdown (Exit/tray Exit), windows are
        // closed by ShutdownMainWindow. This is NOT a pet removal —
        // the state archive must be preserved for the next startup.
        if (_isShuttingDown)
            return;

        // P3-001-S3: Per-pet removal. When a pet window closes (e.g. via
        // right-click → Remove Pet on the second pet's window), remove
        // just that pet from the archive.
        if (sender is MainWindow window)
        {
            var petId = window.StateContext?.PetId;
            if (!string.IsNullOrEmpty(petId))
            {
                // F-05 R9: Only remove the second pet (non-original).
                // The original first pet is protected.
                if (_state?.Pets is not null && _state.Pets.Count > 0 &&
                    _state.Pets[0] is { } firstPet && firstPet.PetId == petId)
                {
                    // Original pet's window closed unexpectedly (e.g. alt-F4).
                    // Treat as global exit, not removal.
                    ShutdownMainWindow();
                    return;
                }

                // P4-002 R1 F-02: Cancel any active rest-reminder on the
                // closed window before removing it from the dictionary.
                window.CancelRestReminder();

                _petWindows.Remove(petId);
                _state?.Pets?.RemoveAll(p => p is not null && p.PetId == petId);
                MarkStateDirty();
                return;
            }
        }
        // Fallback: if we can't identify the pet, shut down everything.
        ShutdownMainWindow();
    }

    internal void ShutdownMainWindow(string? cleanupSidecarPath = null)
    {
        if (_isShuttingDown)
            return;
        _isShuttingDown = true;

        // P6-004（第一小步）：停止主动气泡判定并关闭气泡/查看窗。
        _statsBubbleTimer?.Stop();
        _statsBubbleTimer = null;
        _statsBubble?.Close();
        _statsBubble = null;
        _statsViewer?.Close();
        _statsViewer = null;

        // P4-002: Cancel any active rest-reminder performance before flushing.
        foreach (var w in _petWindows.Values)
            w.CancelRestReminder();

        FlushFinalState();

        // P4-001: Stop the activity session and release system event
        // subscriptions before closing windows.
        UnsubscribeSystemEvents();
        ActivitySessionService?.Stop();
        ActivitySessionService?.Dispose();
        ActivitySessionService = null;

        // P6-001: Stop the usage-stats sampling and persist anything pending
        // (with retention pruning) before the process exits.
        UsageStatsService?.Stop();
        UsageStatsService?.Dispose();
        UsageStatsService = null;

        // F-04 R4: After cleanup, write the post-cleanup state to the
        // sidecar file so the probe can verify subscriptions were
        // released and the service was nulled — BEFORE Shutdown(0)
        // terminates the process.
        // The sidecar also records that Shutdown(0) was called, so the
        // test can distinguish a clean WPF shutdown from a bare
        // Environment.Exit fallback.
        if (cleanupSidecarPath is not null)
        {
            try
            {
                var cleanupState = new
                {
                    subscribedAfterCleanup = _systemEventsSubscribed,
                    serviceNullAfterCleanup = ActivitySessionService is null,
                    shutdownCalled = true
                };
                System.IO.File.WriteAllText(cleanupSidecarPath,
                    System.Text.Json.JsonSerializer.Serialize(cleanupState,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best effort */ }
        }

        // Clean up tray.
        _tray?.Dispose();
        _tray = null;

        // Clean up single-instance guard.
        _instanceGuard?.Dispose();
        _instanceGuard = null;

        // P3-001-S2: Close all pet windows.
        foreach (var w in _petWindows.Values)
            w.Close();
        _petWindows.Clear();

        // F-04 R4: Ensure ShutdownMode is OnExplicitShutdown so that
        // Shutdown(0) actually terminates the process. When the App is
        // constructed without loading App.xaml (e.g. in the diagnostic
        // probe), the default ShutdownMode is OnLastWindowClose. With
        // no windows open, Shutdown(0) may not terminate Dispatcher.Run.
        // Setting OnExplicitShutdown ensures Shutdown(0) is definitive.
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        Shutdown(0);
    }

    /// <summary>
    /// F-08 R9: Test-only hook that performs the same save + cleanup as
    /// ShutdownMainWindow but does NOT call Shutdown(0), which would
    /// terminate the test process. Used by isolation tests to verify
    /// the exit save chain without actually shutting down the application.
    /// </summary>
    internal void ShutdownMainWindowForTest()
    {
        if (_isShuttingDown)
            return;
        _isShuttingDown = true;

        // P4-002: Cancel any active rest-reminder performance before flushing,
        // matching the production ShutdownMainWindow path.
        foreach (var w in _petWindows.Values)
            w.CancelRestReminder();

        FlushFinalState();

        _tray?.Dispose();
        _tray = null;

        _instanceGuard?.Dispose();
        _instanceGuard = null;

        // P3-003 R5 F-04: Align ShutdownMode to the production App.xaml
        // configuration (OnExplicitShutdown) BEFORE closing windows.
        // If ShutdownMode is OnLastWindowClose (the WPF default when App
        // is constructed without loading App.xaml, or when a previous test
        // left it at default), closing the last window triggers WPF
        // auto-shutdown, which terminates the App lifecycle. Subsequent
        // CreatePetWindow + Show() will not receive SourceInitialized,
        // so RestoreWindowPosition never runs and windows stay at
        // constructor defaults. Setting OnExplicitShutdown here ensures
        // closing windows only closes them — the App stays alive for
        // the restart phase of the test.
        //
        // Uses ResetWpfLifecycleForTest (reflection) to bypass the
        // set_ShutdownMode property's VerifyAccess() call and also
        // clear _appIsShutdown if a previous test's TestCleanup
        // triggered auto-shutdown.
        ResetWpfLifecycleForTest();

        foreach (var w in _petWindows.Values.ToList())
        {
            try { w.Close(); } catch { /* window may already be closing */ }
        }
        _petWindows.Clear();
    }

    /// <summary>
    /// Mark state as dirty for debounced save. Called by MainWindow on
    /// position/state changes.
    /// P3-001-S2: The petId parameter routes the save to the correct
    /// PetInstanceArchive. Null petId saves all pets (backward compat).
    /// </summary>
    internal void MarkStateDirty(string? petId = null)
    {
        if (_state is not null && _saveCoordinator is not null)
        {
            // v4: per-pet position fields. Route by petId when available;
            // fall back to pets[0] for single-pet / null-petId callers.
            if (_state.Pets is null || _state.Pets.Count == 0)
                _state.Pets = new List<PetInstanceArchive> { new PetInstanceArchive() };
            else
                _state.Pets.RemoveAll(p => p is null);

            // P3-001-S3: When petId is null (called by OnPauseToggled,
            // FlushFinalState, etc.), refresh positions for ALL pets,
            // not just pets[0]. This ensures multi-pet positions are
            // correctly saved on pause and exit.
            if (string.IsNullOrEmpty(petId))
            {
                foreach (var p in _state.Pets)
                {
                    if (p is null) continue;
                    RefreshPetPositionFromWindow(p);
                }
                // Sync rare-event state (global, once is enough).
                if (RuntimeTracker is not null && RareEventScheduler is not null)
                    MapRareEventObjectsToArchive(_state, RuntimeTracker, RareEventScheduler);
                _saveCoordinator.MarkStateDirty(_state);
                return;
            }

            var pet = FindPetById(petId) ?? _state.Pets[0];

            RefreshPetPositionFromWindow(pet);

            // P1-004: Sync rare-event state to archive via the shared production
            // mapping (F-06: same path used by tests). Rare events are global.
            if (RuntimeTracker is not null && RareEventScheduler is not null)
            {
                MapRareEventObjectsToArchive(_state, RuntimeTracker, RareEventScheduler);
            }
            _saveCoordinator.MarkStateDirty(_state);
        }
    }

    /// <summary>
    /// P3-001-S3: Read the current window position and emotion state for
    /// a single pet, writing them into the PetInstanceArchive. Handles
    /// cross-thread access safely.
    /// </summary>
    private void RefreshPetPositionFromWindow(PetInstanceArchive pet)
    {
        var window = FindWindowByPetId(pet.PetId);
        if (window is not null && window.Dispatcher.CheckAccess())
        {
            pet.WindowLeft = window.Left;
            pet.WindowTop = window.Top;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            pet.SavedDeviceName = MonitorInterop.GetWindowMonitorDeviceName(hwnd) ?? pet.SavedDeviceName;
        }
        // P1-003: Sync emotion state to archive via the shared production
        // mapping. P3-001-S2: Use per-pet emotion objects from the window's context.
        if (window?.StateContext is { } ctx
            && ctx.EmotionAccumulator is not null
            && ctx.GrievanceExpression is not null)
        {
            MapEmotionObjectsToArchive(_state!, pet, ctx.EmotionAccumulator, ctx.GrievanceExpression);
        }
        else if (EmotionAccumulator is not null && GrievanceExpression is not null)
        {
            // Fallback: single-pet legacy path (tests, probe mode).
            MapEmotionObjectsToArchive(_state!, pet, EmotionAccumulator, GrievanceExpression);
        }
    }

    internal void FlushFinalState()
    {
        if (_saveCoordinator is null)
            return;
        // P1-004: If a probe is active, restore the original position so
        // the saved state reflects the pre-probe location, not the edge.
        // P3-001-S2: Restore probe position for all pet windows.
        foreach (var w in _petWindows.Values)
            w.RestoreProbePositionForExit();
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
        // P4-001: Release system event subscriptions and stop the activity
        // session service.
        UnsubscribeSystemEvents();
        ActivitySessionService?.Dispose();
        ActivitySessionService = null;
        // P6-001: Dispose the usage-stats service — Dispose → Stop → persist
        // pending stats + retention pruning.
        UsageStatsService?.Dispose();
        UsageStatsService = null;
        _tray?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}
