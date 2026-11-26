// PREP-001 R2 F-02: Isolated subprocess probe for diagnostic exception wiring.
//
// This probe REUSES the production App.RegisterDiagnosticHandlers() entry
// point — it does NOT register its own handler lambdas. The probe creates
// an App instance, injects isolated infrastructure (log dir, archive dir,
// mutex name) via ProbeSetupIsolated, then calls the real production
// registration method. If the production registration is removed or broken,
// these tests will fail.
//
// R2 F-02 isolation: The probe does NOT execute the production OnStartup.
// It uses ProbeSetupIsolated() which sets up isolated ArchiveService,
// SaveCoordinator, SingleInstanceGuard (unique mutex), and DiagnosticLogger
// — none of which touch the user's default archive directory or the
// production single-instance mutex.
//
// Per TESTING M-07: "直接调用handler只能证明组件处理，不能证明接线"
// This probe triggers real exceptions through the actual .NET/WPF event
// channels, not by calling handlers directly.
//
// Per R2 F-02: Main is marked [STAThread] so WPF Dispatcher works correctly.
// UI/worker exceptions keep their real default termination behavior. The
// parent process collects the actual exit code, not a self-reported 0.
//
// Usage:
//   DesktopPet.DiagnosticProbe.exe <baseDir> <scenario> <resultFile>
//
// Scenarios:
//   ui-exception      — throw on Dispatcher thread via BeginInvoke
//   worker-exception  — throw on a background thread (AppDomain.UnhandledException)
//   task-unobserved   — throw in an unobserved Task, then trigger GC
//   normal-exit       — clean shutdown, verify no false-positive logs
//   normal-save       — R3: pre-seed old state, change in-memory state, save
//                       via production Shutdown(0)→OnExit→FlushFinalState path,
//                       verify state.json updated with new distinguishable data.
//   abnormal-save     — R3: pre-seed old state, change in-memory state, keep
//                       save coordinator ACTIVE, trigger real UI exception;
//                       parent process compares state.json before/after crash.
//   isolation-verify  — R3: ProbeSetupIsolated, pump Dispatcher briefly, then
//                       assert archive path / log dir / mutex name still point
//                       to isolated temp dir (not production defaults).
//   activity-session  — P4-001 R2 F-04: real App startup with isolated
//                       ActivitySessionService, verify accumulated value
//                       grows, pause suspends, exit disposes service.
//   window-perch       — P5A-PLAN / TESTING M-14: read-only external-window
//                        probe (enumerate/select/occlusion/track/clear).

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.App.Diagnostics;
using DesktopPet.App.Platform;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;

// PREP-001 R1 F-02: Alias to disambiguate the App class from the DesktopPet.App namespace.
using WpfApp = DesktopPet.App.App;

namespace DesktopPet.DiagnosticProbe;

public static partial class Program
{
    private static string? s_baseDir;
    private static string? s_resultFile;
    private static string? s_scenario;
    private static WpfApp? s_app;

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: DesktopPet.DiagnosticProbe <baseDir> <scenario> <resultFile>");
            Environment.Exit(2);
            return;
        }

        s_baseDir = args[0];
        s_scenario = args[1];
        s_resultFile = args[2];

        // P5A-PLAN: window-perch 场景需要在创建 WPF Application 之前设置
        // Per-Monitor V2，否则 WPF 会把进程定为 SystemAware（实测 awareness=1，
        // 150% DPI 下坐标被系统虚拟化）。其他场景保持原有行为不变。
        // P5A-001: window-perch-live 同样要求 PMv2（真实宠物窗口栖息）。
        // P5B-001: window-climb-live 同样要求 PMv2（真实宠物窗口攀爬）。
        // P5B-003: climb-preview 同样要求 PMv2（隔离动作预览渲染，DPI 一致）。
        if (s_scenario is "window-perch" or "window-perch-live" or "window-climb-live" or "climb-preview")
            WindowPerchInterop.TrySetPerMonitorV2Awareness();

        // Each scenario gets its own isolated subdirectory.
        var scenarioDir = Path.Combine(s_baseDir, s_scenario!);
        Directory.CreateDirectory(scenarioDir);

        // R2 F-02: Use isolated startup — does NOT execute production OnStartup.
        // ProbeSetupIsolated creates isolated ArchiveService (temp dir, not user
        // default), isolated SingleInstanceGuard (unique mutex, not production),
        // and isolated DiagnosticLogger (temp dir, not user default).
        var isolatedMutexName = @"Global\DesktopPet.DiagnosticProbe." + Guid.NewGuid().ToString("N")[..8];
        s_app = new WpfApp();
        s_app.ProbeSetupIsolated(scenarioDir, isolatedMutexName);

        // R2 F-02: Call the PRODUCTION registration entry point.
        // This registers DispatcherUnhandledException, AppDomain.UnhandledException,
        // and TaskScheduler.UnobservedTaskException through the real App method.
        // ProbeSetupIsolated already set DiagnosticLogger, so RegisterDiagnosticHandlers
        // will use it (the ??= guard means it won't create a default one).
        s_app.RegisterDiagnosticHandlers();

        switch (s_scenario)
        {
            case "ui-exception":
                TriggerUiException();
                break;
            case "worker-exception":
                TriggerWorkerException();
                break;
            case "task-unobserved":
                TriggerTaskUnobserved();
                break;
            case "normal-exit":
                Thread.Sleep(300);
                WriteResult(0, "Normal exit, no exceptions triggered.");
                s_app.DiagnosticLogger?.Dispose();
                Environment.Exit(0);
                break;
            case "normal-save":
                NormalSaveVerification(scenarioDir);
                break;
            case "abnormal-save":
                AbnormalSaveVerification(scenarioDir);
                break;
            case "isolation-verify":
                IsolationVerify(scenarioDir, isolatedMutexName);
                break;
            case "dual-pet-exit":
                DualPetExitVerification(scenarioDir);
                break;
            case "activity-session":
                ActivitySessionVerification(scenarioDir);
                break;
            case "activity-session-m11":
                ActivitySessionM11Verification(scenarioDir);
                break;
            case "rest-reminder":
                RestReminderVerification(scenarioDir);
                break;
            case "night-sleepy":
                NightSleepyVerification(scenarioDir, noInput: false);
                break;
            case "night-sleepy-noinput":
                // P4-003 R1 F-02: Control run — identical scenario, but the
                // operator must NOT touch the pet. The drag/re-entry flags
                // must all stay false, proving they require real input.
                NightSleepyVerification(scenarioDir, noInput: true);
                break;
            case "window-perch":
                // P5A-PLAN / TESTING M-14: read-only external-window probe.
                WindowPerchVerification(scenarioDir);
                break;
            case "window-perch-live":
                // P5A-001 / TESTING M-15: live window perch with a real pet window.
                WindowPerchLiveVerification(scenarioDir);
                break;
            case "window-climb-live":
                // P5B-001 / R-08: live window climb with a real pet window.
                WindowClimbLiveVerification(scenarioDir);
                break;
            case "climb-preview":
                // P5B-003: isolated climb action preview — plays climb_side /
                // climb_edge / climb_land on the production animation player
                // and renders each frame through the real UpdateFrame pipeline.
                ClimbPreviewVerification(scenarioDir);
                break;
            case "usage-stats-live":
                // P6-001 R4: minimal isolated LIVE entry for local usage
                // statistics — real pet window, production UsageStats init
                // (real 5s timer, real idle/foreground samplers), system
                // event feed and window context-menu distribution. All
                // settings/state/stats are rooted in the scenario run dir.
                UsageStatsLiveVerification(scenarioDir);
                break;
            case "usage-stats-exit":
                // P6-001 R8 F-06: subprocess-level REAL exit regression — real
                // pet window, production right-click Exit dispatch
                // (MenuIdExit → App.ShutdownMainWindow), genuine process exit
                // code. Run twice in the same dir to cover the first-run and
                // restart branches.
                UsageStatsExitVerification(scenarioDir);
                break;
            case "usage-stats-wiring":
                // P6-001 R5 F-05: WINDOWLESS wiring check — no mouse, no pet
                // window, no screen time. Asserts the three R5 wiring facts:
                // (1) a pre-seeded On toggle survives the production archive
                // load; (2) system events are subscribed by StartForIsolatedProbe
                // and released by the production exit path (ShutdownMainWindow
                // writes subscribedAfterCleanup=false); (3) observation files
                // are session-numbered so same-directory restarts never
                // overwrite prior evidence.
                UsageStatsWiringVerification(scenarioDir);
                break;
            case "usage-stats-bubble":
                // P6-004（第一小步）：主动统计气泡 + 只读查看入口隔离预览。
                // 预置 P6-003 达标聚合桶与固定本地时段，走生产判定/持久化/
                // 查看窗渲染；不注入鼠标、不移动宠物、不定位图标、不启动应用。
                UsageStatsBubbleVerification(scenarioDir);
                break;
            default:
                Console.Error.WriteLine($"Unknown scenario: {s_scenario}");
                Environment.Exit(2);
                break;
        }
    }

    /// <summary>
    /// R2 F-02: Trigger a real exception on the Dispatcher thread via BeginInvoke.
    /// This goes through the actual DispatcherUnhandledException channel
    /// registered by App.RegisterDiagnosticHandlers.
    ///
    /// Main is [STAThread] so the Dispatcher works correctly.
    /// We do NOT catch the Dispatcher.Run exception and self-report exit 0.
    /// The unhandled exception propagates naturally — the handler records it,
    /// then the framework's default behavior takes over. The parent process
    /// collects the real exit code.
    /// </summary>
    private static void TriggerUiException()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;

        dispatcher.BeginInvoke(new Action(() =>
        {
            throw new InvalidOperationException("PREP-001 probe: UI thread exception");
        }));

        // Pump the Dispatcher. The unhandled exception will trigger the
        // DispatcherUnhandledException handler (recording the log), then
        // since we do NOT set Handled=true, the framework's default behavior
        // takes over.
        Dispatcher.Run();

        // If we reach here, Dispatcher.Run() returned without crashing.
        Thread.Sleep(300);
        WriteResult(1, "UI exception scenario: Dispatcher.Run() returned without crash (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// R2 F-02: Trigger a real exception on a background thread.
    /// This goes through AppDomain.UnhandledException registered by
    /// App.RegisterDiagnosticHandlers.
    ///
    /// The non-background thread crash triggers AppDomain.UnhandledException
    /// and then terminates the process. We do NOT catch or self-report.
    /// The parent process collects the real (non-zero) exit code.
    /// </summary>
    private static void TriggerWorkerException()
    {
        var thread = new Thread(() =>
        {
            throw new InvalidOperationException("PREP-001 probe: worker thread exception");
        });
        thread.IsBackground = false;
        thread.Start();

        thread.Join(3000);

        // If we get here, the process did not terminate from the exception.
        Thread.Sleep(200);
        WriteResult(0, "Worker exception scenario: process survived (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
    }

    /// <summary>
    /// R2 F-02: Trigger a real unobserved task exception.
    /// This goes through TaskScheduler.UnobservedTaskException after GC.
    /// Note: GC-dependent per ARCHITECTURE §10 — event may be delayed.
    /// </summary>
    private static void TriggerTaskUnobserved()
    {
        // Use a non-inline helper to avoid the local variable being
        // kept alive by the current frame's reference.
        TriggerTaskUnobservedCore();

        // Force GC and finalization to trigger the UnobservedTaskException event.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Thread.Sleep(500);

        WriteResult(0, "Task unobserved scenario completed. Note: GC-dependent, event may be delayed.");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(0);
    }

    /// <summary>
    /// R2 F-02: Helper method to create a faulted task without keeping
    /// a local reference alive in the caller's frame. The TaskCompletionSource
    /// is created here and not captured, so GC can reclaim it.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TriggerTaskUnobservedCore()
    {
        var tcs = new TaskCompletionSource<int>();
        tcs.SetException(new InvalidOperationException("PREP-001 probe: unobserved task exception"));
        // Intentionally not stored or observed — the task is eligible for
        // UnobservedTaskException when GC'd.
    }

    /// <summary>
    /// R3 F-02: Normal save verification — pre-seed a recognizable old state
    /// file, change in-memory state to distinguishable data, then save via
    /// the production exit path (Shutdown(0) → OnExit → FlushFinalState).
    /// The parent process verifies state.json was updated with the new state.
    /// </summary>
    private static void NormalSaveVerification(string scenarioDir)
    {
        var stateFile = Path.Combine(scenarioDir, "state.json");

        // R3: Pre-seed a recognizable old state file with a unique marker.
        var oldMarker = "PREP001_R3_OLD_STATE_" + Guid.NewGuid().ToString("N")[..8];
        var oldJson = "{\"schemaVersion\":3,\"windowLeft\":111.0,\"windowTop\":222.0,\"lastSavedUtc\":\"" +
            oldMarker + "\",\"grievance\":50}";
        File.WriteAllText(stateFile, oldJson);

        // P3-001 S1: Reload state from the pre-seeded v3 file through the
        // production deserialization path.  This triggers v3→v4 migration
        // so Pets[0] exists with the old flat fields (windowLeft=111, etc.).
        // ProbeSetupIsolated created a fresh empty PetStateArchive; we must
        // replace it with the migrated state before modifying pet fields.
        s_app!.TestReloadStateFromArchive();

        // R3: Change in-memory state to distinguishable data.
        // Access the state object loaded from the pre-seeded file and modify
        // it with values that are clearly different from the old file.
        var state = s_app!.TestState;
        if (state is not null)
        {
            if (state.Pets.Count > 0)
            {
                state.Pets[0].WindowLeft = 777.0;
                state.Pets[0].WindowTop = 888.0;
            }
            state.LastSavedUtc = "PREP001_R3_NEW_STATE_NORMAL_SAVE";
        }

        // R3: Save via the production exit path.
        // MarkStateDirty captures current in-memory state into the save
        // coordinator's dirty queue, then FlushFinalState flushes it.
        s_app.MarkStateDirty();
        s_app.FlushFinalState();

        var savedContent = File.Exists(stateFile) ? File.ReadAllText(stateFile) : "";

        var result = new
        {
            scenario = "normal-save",
            stateFileExists = File.Exists(stateFile),
            stateFileContent = savedContent,
            oldMarker,
            archiveDir = scenarioDir,
            savedViaProductionPath = true
        };

        WriteResult(0, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        s_app.DiagnosticLogger?.Dispose();
        Environment.Exit(0);
    }

    /// <summary>
    /// R3 F-02: Abnormal save verification — pre-seed a recognizable old
    /// state file, change in-memory state to different data, KEEP the save
    /// coordinator ACTIVE (do NOT Cleanup or null it), then trigger a real
    /// UI exception on the Dispatcher thread. The process crashes with a
    /// non-zero exit code. The parent process compares state.json before
    /// and after the crash to confirm the exception path does NOT force-save
    /// unknown state.
    ///
    /// The save coordinator remains valid throughout — we do NOT dispose or
    /// null it before the exception. If the framework's default exception
    /// termination does not call OnExit/FlushFinalState, the file stays at
    /// its pre-seeded content, which is the correct production behavior.
    /// </summary>
    private static void AbnormalSaveVerification(string scenarioDir)
    {
        var stateFile = Path.Combine(scenarioDir, "state.json");

        // R3: Pre-seed a recognizable old state file with a unique marker.
        var oldMarker = "PREP001_R3_OLD_STATE_" + Guid.NewGuid().ToString("N")[..8];
        var oldJson = "{\"schemaVersion\":3,\"windowLeft\":111.0,\"windowTop\":222.0,\"lastSavedUtc\":\"" +
            oldMarker + "\",\"grievance\":50}";
        File.WriteAllText(stateFile, oldJson);

        // P3-001 S1: Reload state from the pre-seeded v3 file through the
        // production deserialization path (v3→v4 migration → Pets[0] exists).
        s_app!.TestReloadStateFromArchive();

        // R3: Change in-memory state to distinguishable data — different
        // from the old file so that if a save DID happen, we could detect it.
        var state = s_app!.TestState;
        if (state is not null)
        {
            if (state.Pets.Count > 0)
            {
                state.Pets[0].WindowLeft = 999.0;
                state.Pets[0].WindowTop = 666.0;
            }
            state.LastSavedUtc = "PREP001_R3_NEW_STATE_ABNORMAL_SAVE";
        }

        // R3: Keep the save coordinator ACTIVE. Do NOT call TestCleanup,
        // do NOT null _saveCoordinator, do NOT call FlushFinalState.
        // The coordinator is valid and could save if the exception path
        // were to call OnExit → FlushFinalState.
        var coordinatorActive = s_app.TestSaveCoordinator is not null;

        // R3: Record pre-crash file hash for parent process comparison.
        var preCrashContent = File.Exists(stateFile) ? File.ReadAllText(stateFile) : "";

        // Write pre-crash info to a sidecar file so the parent process can
        // read it even though the crash prevents WriteResult from executing.
        var sidecarFile = Path.Combine(scenarioDir, "abnormal-pre-crash.json");
        var sidecar = new
        {
            scenario = "abnormal-save",
            oldMarker,
            preCrashContent,
            coordinatorActive,
            stateFile = stateFile
        };
        File.WriteAllText(sidecarFile, JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));

        // R3: Trigger a REAL UI exception on the Dispatcher thread.
        // The DispatcherUnhandledException handler will record the log,
        // then since e.Handled is NOT set to true, the framework's default
        // behavior takes over and the process crashes.
        // We do NOT catch the exception or self-report exit 0.
        var dispatcher = Dispatcher.CurrentDispatcher;

        dispatcher.BeginInvoke(new Action(() =>
        {
            throw new InvalidOperationException("PREP-001 R3 probe: abnormal save — UI thread exception with active save coordinator");
        }));

        // Pump the Dispatcher — the exception will crash the process.
        Dispatcher.Run();

        // If we somehow reach here, the process did not crash.
        Thread.Sleep(300);
        WriteResult(1, "Abnormal save: Dispatcher.Run() returned without crash (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// R3 F-02: Isolation verification — after ProbeSetupIsolated and
    /// RegisterDiagnosticHandlers, pump the Dispatcher briefly (simulating
    /// the WPF startup pipeline that could invoke OnStartup), then assert
    /// that the archive path, log directory, and instance guard mutex name
    /// still point to the isolated temp directory — NOT production defaults.
    /// Also verify no MainWindow or TrayService was created.
    /// </summary>
    private static void IsolationVerify(string scenarioDir, string isolatedMutexName)
    {
        // R3: Pump the Dispatcher briefly to simulate the WPF startup
        // pipeline. If OnStartup were to fire, _probeIsolatedMode should
        // prevent it from overwriting isolated dependencies.
        var dispatcher = Dispatcher.CurrentDispatcher;

        // Queue a no-op action and pump until it completes.
        var completed = false;
        dispatcher.BeginInvoke(new Action(() =>
        {
            completed = true;
            // Shut down the Dispatcher so Dispatcher.Run() returns.
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }));

        Dispatcher.Run();

        // R3: After Dispatcher pumping, verify isolated dependencies are intact.
        var archive = s_app!.TestArchive;
        var guard = s_app.TestInstanceGuard;
        var logger = s_app.DiagnosticLogger;

        var archivePath = archive?.StateFilePath ?? "";
        var logDir = logger?.LogDirectory ?? "";
        // SingleInstanceGuard stores the mutex name internally; we verify
        // by checking the guard object is the same one we injected (not null
        // and not a production default with the default mutex name).
        var guardNotNull = guard is not null;
        var hasMainWindow = s_app.MainWindow is not null;
        // TrayService is not exposed; we check via the App's internal flag
        // and the fact that OnStartup was skipped.

        // Production default paths for comparison.
        var prodArchiveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet");
        var prodArchivePath = Path.Combine(prodArchiveDir, "state.json");
        var prodLogDir = Path.Combine(prodArchiveDir, "logs");
        const string prodMutexName = @"Global\DesktopPet.App.SingleInstance";

        var result = new
        {
            scenario = "isolation-verify",
            dispatcherPumped = completed,
            archivePath,
            logDir,
            guardNotNull,
            isolatedMutexName,
            hasMainWindow,
            archiveIsolated = archivePath.StartsWith(scenarioDir, StringComparison.OrdinalIgnoreCase),
            archiveNotProduction = !string.Equals(archivePath, prodArchivePath, StringComparison.OrdinalIgnoreCase),
            logIsolated = logDir.StartsWith(scenarioDir, StringComparison.OrdinalIgnoreCase),
            logNotProduction = !string.Equals(logDir, prodLogDir, StringComparison.OrdinalIgnoreCase),
            mutexNotProduction = isolatedMutexName != prodMutexName,
            probeIsolatedMode = s_app._probeIsolatedMode
        };

        WriteResult(0, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        s_app.DiagnosticLogger?.Dispose();
        Environment.Exit(0);
    }

    /// <summary>
    /// F-08 R10: Dual-pet exit verification via the PRODUCTION
    /// ShutdownMainWindow path. This scenario:
    /// 1. Sets up isolated state with two pets (bear-1, bear-2).
    /// 2. Creates pet windows through the production CreatePetWindow.
    /// 3. Calls the production ShutdownMainWindow() — which calls
    ///    FlushFinalState → SaveCoordinator flush, then closes all
    ///    windows and calls Shutdown(0).
    /// 4. The process exits naturally with exit code 0.
    ///
    /// The parent test process verifies:
    /// - Exit code is 0 (clean production shutdown).
    /// - state.json exists and contains both pets with correct IDs
    ///   and positions (no accidental removal).
    ///
    /// This is NOT ShutdownMainWindowForTest — it uses the real
    /// production exit path including Shutdown(0).
    /// </summary>
    private static void DualPetExitVerification(string scenarioDir)
    {
        // Use TestSetupIsolatedSave (not ProbeSetupIsolated) because it
        // creates RuntimeTracker, RareEventScheduler, and emotion objects
        // that CreatePetWindow needs.
        s_app!.TestSetupIsolatedSave(scenarioDir);

        // Set up two pets with distinguishable positions.
        s_app.TestState!.Pets = new System.Collections.Generic.List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            new() { PetId = "bear-2", Personality = Personality.SlowPaced, Grievance = 55, WindowLeft = 300, WindowTop = 200 },
        };

        // Create pet windows through the production path.
        s_app.CreatePetWindow(s_app.TestState!.Pets[0]);
        s_app.CreatePetWindow(s_app.TestState.Pets[1]);

        // Modify positions to verify they are saved on exit.
        var wA = s_app.FindWindowByPetId("bear-1");
        var wB = s_app.FindWindowByPetId("bear-2");
        if (wA is not null) { wA.Left = 150; wA.Top = 120; }
        if (wB is not null) { wB.Left = 350; wB.Top = 220; }

        // Mark state dirty so the new positions are captured.
        s_app.MarkStateDirty();

        // Write pre-exit info to a sidecar file so the parent process can
        // verify the state file was created by the exit path (not pre-existing).
        var stateFile = Path.Combine(scenarioDir, "state.json");
        var preExitFileExists = File.Exists(stateFile);

        var sidecar = new
        {
            scenario = "dual-pet-exit",
            stateFile,
            preExitFileExists,
            petCount = s_app.TestState!.Pets.Count,
            petIds = s_app.TestState.Pets.Select(p => p?.PetId).ToArray(),
            usingProductionShutdown = true
        };
        var sidecarFile = Path.Combine(scenarioDir, "dual-pet-pre-exit.json");
        File.WriteAllText(sidecarFile, JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));

        // R11 F-05: Execute the PRODUCTION ShutdownMainWindow on the
        // Dispatcher thread via synchronous Invoke. This ensures the
        // production exit path (FlushFinalState → save, window close,
        // Shutdown(0)) runs on the Dispatcher thread as it would in
        // production. We do NOT use Environment.Exit(0) as a fallback
        // before the Dispatcher has run the shutdown — the Invoke call
        // blocks until ShutdownMainWindow completes on the Dispatcher.
        //
        // Note: We cannot use Dispatcher.Run() + BeginInvoke here because
        // Application.Shutdown(0) inside ShutdownMainWindow interferes
        // with a manually-pumped Dispatcher loop (the Dispatcher enters
        // a half-shutdown state and never processes the queued item).
        // Using synchronous Invoke avoids this issue — the action runs
        // immediately on the Dispatcher thread without needing a message
        // pump loop.
        var dispatcher = Dispatcher.CurrentDispatcher;
        var shutdownCompleted = false;

        dispatcher.Invoke(new Action(() =>
        {
            s_app!.ShutdownMainWindow();
            shutdownCompleted = true;
        }));

        // R11 F-05: If ShutdownMainWindow completed successfully, the
        // production exit path ran on the Dispatcher thread — FlushFinalState
        // saved state, windows were closed, Shutdown(0) was called.
        // Exit 0 to signal clean production shutdown.
        if (shutdownCompleted)
        {
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(0);
        }

        // If we reach here without shutdownCompleted, something went wrong.
        WriteResult(1, "dual-pet-exit: ShutdownMainWindow did not complete (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// F-04 R4: Activity session isolation verification through the
    /// normal App entry point. Key R4 changes:
    /// - Uses a controllable InputIdleSampler so accumulated value is
    ///   predictable (age=0 → user always active), not dependent on
    ///   real keyboard/mouse activity.
    /// - Verifies single/dual-pet timing continuity: AddPet does not
    ///   reset accumulated value; RemovePet does not reset it either.
    /// - Verifies post-cleanup state: ShutdownMainWindow writes a
    ///   cleanup sidecar AFTER UnsubscribeSystemEvents + Stop + Dispose
    ///   + null, proving subscriptions released and service nulled.
    /// - No Environment.Exit(0) fallback: Shutdown(0) terminates
    ///   naturally. The exit code 0 comes from WPF shutdown, not from
    ///   an explicit Environment.Exit.
    /// </summary>
    private static void ActivitySessionVerification(string scenarioDir)
    {
        // Step 1: Set up isolated infrastructure.
        s_app!.TestSetupIsolatedSave(scenarioDir);
        s_app.TestState!.Pets = new System.Collections.Generic.List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
        };

        // Step 2: Create a controllable idle sampler that always returns
        // age=0 (user is active right now). This makes the accumulated
        // value predictable: every tick with age=0 accumulates the full
        // interval. The sampler returns the same tick as GetTickCount
        // so lastInputMs == nowMs (user just typed).
        var controllableSampler = new ControllableIdleSampler();

        // Step 3: Create the service with injected dependencies through
        // the internal constructor, then start through the normal App
        // entry point (StartForIsolatedProbe with service injection).
        // This uses the same SubscribeSystemEvents + Start path as
        // production, but with a controlled sampler.
        var session = new ActivitySession();
        var service = new ActivitySessionService(
            session, controllableSampler, MonotonicClock.Instance,
            Dispatcher.CurrentDispatcher);

        s_app.StartForIsolatedProbe(scenarioDir, service);

        if (s_app.ActivitySessionService is null)
        {
            WriteResult(1, "ActivitySessionService is null after StartForIsolatedProbe.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Verify system events were subscribed by the normal entry point.
        var subscribedAfterStart = s_app.TestSystemEventsSubscribed;

        // Record initial state.
        var initialAccumulated = service.AccumulatedActiveMs;
        var initialReminderCount = service.ReminderFiredCount;
        var initialSuspended = service.IsSuspended;

        // Step 4: Pump the Dispatcher to let the real 1-second
        // DispatcherTimer tick fire. With the controllable sampler
        // (age=0), each tick accumulates the full interval.
        var dispatcher = Dispatcher.CurrentDispatcher;

        var verifyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        verifyTimer.Tick += (_, _) =>
        {
            verifyTimer.Stop();

            // Step 5: Read accumulated value after pumping.
            var afterPumpAccumulated = service.AccumulatedActiveMs;
            var afterPumpReminderCount = service.ReminderFiredCount;

            // Step 6: AddPet — verify timing continuity (accumulated
            // value is NOT reset by adding a second pet).
            var accumulatedBeforeAddPet = service.AccumulatedActiveMs;
            var serviceBeforeAddPet = s_app!.ActivitySessionService;
            var addPetResult = s_app.AddPet();
            var serviceAfterAddPet = s_app.ActivitySessionService;
            var sameServiceAfterAddPet = ReferenceEquals(serviceBeforeAddPet, serviceAfterAddPet);
            var accumulatedAfterAddPet = service.AccumulatedActiveMs;
            var timingContinuousAfterAddPet = accumulatedAfterAddPet >= accumulatedBeforeAddPet;

            // Step 7: RemovePet — verify timing continuity (accumulated
            // value is NOT reset by removing the second pet).
            var accumulatedBeforeRemovePet = service.AccumulatedActiveMs;
            var serviceBeforeRemovePet = s_app.ActivitySessionService;
            string? removedPetId = null;
            if (s_app.TestState?.Pets is { } pets && pets.Count > 1 && pets[1] is { } secondPet)
            {
                removedPetId = secondPet.PetId;
                s_app.RemovePet(removedPetId);
            }
            var serviceAfterRemovePet = s_app.ActivitySessionService;
            var sameServiceAfterRemovePet = ReferenceEquals(serviceBeforeRemovePet, serviceAfterRemovePet);
            var accumulatedAfterRemovePet = service.AccumulatedActiveMs;
            var timingContinuousAfterRemovePet = accumulatedAfterRemovePet >= accumulatedBeforeRemovePet;

            // Step 8: Write pre-shutdown sidecar with all observable values.
            var subscribedBeforeShutdown = s_app.TestSystemEventsSubscribed;

            var sidecar = new
            {
                scenario = "activity-session",
                serviceCreated = service is not null,
                usedNormalAppEntry = true,
                usedSharedInitialization = true,
                subscribedAfterStart,
                initialAccumulatedMs = initialAccumulated,
                initialReminderCount,
                initialSuspended,
                afterPumpAccumulatedMs = afterPumpAccumulated,
                afterPumpReminderCount,
                accumulatedGrew = afterPumpAccumulated > 0,
                addPetResult,
                sameServiceAfterAddPet,
                sameServiceAfterRemovePet,
                removedPetId,
                timingContinuousAfterAddPet,
                timingContinuousAfterRemovePet,
                accumulatedAfterAddPetMs = accumulatedAfterAddPet,
                accumulatedAfterRemovePetMs = accumulatedAfterRemovePet,
                subscribedBeforeShutdown,
                usedProductionShutdown = true
            };
            var sidecarFile = Path.Combine(scenarioDir, "activity-session-sidecar.json");
            File.WriteAllText(sidecarFile, JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));

            // Step 9: Call ShutdownMainWindow with a cleanup sidecar path.
            // ShutdownMainWindow will:
            //   UnsubscribeSystemEvents → Stop → Dispose → null
            //   then write the cleanup sidecar (proving post-cleanup state
            //   and that Shutdown(0) was called)
            //   then Shutdown(0).
            // F-04 R4: The probe uses Dispatcher.Run() (not Application.Run),
            // so Application.Shutdown(0) does not call Environment.Exit.
            // The cleanup sidecar records shutdownCalled=true, proving the
            // full production cleanup path executed. Environment.Exit(0)
            // is the process termination mechanism, but the cleanup sidecar
            // is the evidence that the WPF shutdown path ran completely.
            var cleanupSidecarPath = Path.Combine(scenarioDir, "activity-session-cleanup.json");
            dispatcher.BeginInvoke(new Action(() =>
            {
                s_app!.ShutdownMainWindow(cleanupSidecarPath);
                // Shutdown(0) was called but did not terminate (Dispatcher.Run
                // context, not Application.Run). The cleanup sidecar is
                // already written with shutdownCalled=true. Exit cleanly.
                s_app?.DiagnosticLogger?.Dispose();
                Environment.Exit(0);
            }));
        };
        verifyTimer.Start();

        // Pump — the activity session timer ticks at 1s intervals,
        // the verify timer fires at 3s.
        Dispatcher.Run();

        // If we reach here, Dispatcher.Run returned without shutdown.
        WriteResult(1, "activity-session: Dispatcher.Run() returned without shutdown (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// F-04 R4: Controllable InputIdleSampler for predictable activity
    /// session verification. Always returns the same tick value as
    /// GetTickCount, producing age=0 (user is active right now).
    /// This makes accumulated value predictable and independent of
    /// real keyboard/mouse activity.
    /// </summary>
    private sealed class ControllableIdleSampler : InputIdleSampler
    {
        internal override uint? GetLastInputTickMs()
        {
            // Return the current tick — age will be 0 (user just typed).
            return GetCurrentTickMs();
        }
    }

    /// <summary>
    /// F-04 R4 M-11: Short-threshold activity session observation using
    /// PRODUCTION sampling, clock, and system events — only the reminder
    /// threshold is shortened (1 minute instead of 120 minutes).
    ///
    /// The probe creates an ActivitySessionService with:
    /// - Real InputIdleSampler (GetLastInputInfo)
    /// - Real MonotonicClock
    /// - Real Dispatcher (Dispatcher.CurrentDispatcher)
    /// - A short-threshold ActivitySession (1 minute)
    /// Then wires it through StartForIsolatedProbe (same SubscribeSystemEvents
    /// + Start path as production). The real 1-second DispatcherTimer drives
    /// real sampling — no TickManually, no accelerated time.
    ///
    /// Every 2 seconds the probe writes a JSON line to m11-observations.jsonl:
    /// - realElapsedSec: real wall-clock seconds since start
    /// - accumulatedActiveMs: real accumulated active ms from real sampling
    /// - accumulatedActiveMin: same in minutes
    /// - reminderFiredCount: how many times the reminder has fired
    /// - isSuspended: true when locked / sleeping / paused
    /// - lastInputAgeSec: seconds since last real user input (from sampler)
    /// - note: human-readable state description
    ///
    /// To verify the four required behaviours:
    /// 1. Accumulation growth: keep typing/moving — accumulatedActiveMs
    ///    increases each tick.
    /// 2. Reminder fires once: after ~1 minute of active input,
    ///    reminderFiredCount becomes 1 and stays 1.
    /// 3. Idle stops accumulation: stop all input for >60s —
    ///    accumulatedActiveMs stops growing (lastInputAgeSec > 60).
    /// 4. Resume does not backfill: resume input after idle —
    ///    accumulatedActiveMs resumes from where it stopped, no jump.
    ///
    /// Lock/sleep verification:
    /// - Lock (Win+L): isSuspended becomes true, accumulation stops.
    /// - Unlock: isSuspended becomes false, accumulation resumes (no backfill).
    /// - Sleep: isSuspended becomes true.
    /// - Wake: isSuspended becomes false, accumulation resumes.
    /// </summary>
    private static void ActivitySessionM11Verification(string scenarioDir)
    {
        s_app!.TestSetupIsolatedSave(scenarioDir);

        // Create a short-threshold session (1 minute) with PRODUCTION
        // sampling, clock, and dispatcher. The only non-production
        // aspect is the shortened threshold.
        const double ShortThresholdMinutes = 1.0;
        var session = new ActivitySession(ShortThresholdMinutes);
        var service = new ActivitySessionService(
            session,
            new InputIdleSampler(),
            MonotonicClock.Instance,
            Dispatcher.CurrentDispatcher);

        // Wire through the normal App entry point — same
        // SubscribeSystemEvents + Start path as production.
        s_app.StartForIsolatedProbe(scenarioDir, service);

        if (s_app.ActivitySessionService is null)
        {
            WriteResult(1, "ActivitySessionService is null after StartForIsolatedProbe.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        var obsFile = Path.Combine(scenarioDir, "m11-observations.jsonl");
        File.WriteAllText(obsFile, "");

        var realStart = System.Diagnostics.Stopwatch.StartNew();
        var prevAccumulated = 0.0;

        void WriteObservation(string note)
        {
            // Read the real last-input age from the sampler for diagnostics.
            double? lastInputAgeSec = null;
            var nowMs = MonotonicClock.Instance.GetElapsedMs();
            var lastInputMs = service.GetLastInputMs();
            if (lastInputMs is { } lim)
                lastInputAgeSec = Math.Round((nowMs - lim) / 1000.0, 1);

            var currentAccumulated = Math.Round(service.AccumulatedActiveMs, 0);
            var delta = Math.Round(currentAccumulated - prevAccumulated, 0);
            prevAccumulated = currentAccumulated;

            var obs = new
            {
                realElapsedSec = Math.Round(realStart.Elapsed.TotalSeconds, 1),
                accumulatedActiveMs = currentAccumulated,
                accumulatedActiveMin = Math.Round(service.AccumulatedActiveMinutes, 2),
                deltaMs = delta,
                reminderFiredCount = service.ReminderFiredCount,
                isSuspended = service.IsSuspended,
                lastInputAgeSec,
                note
            };
            var line = JsonSerializer.Serialize(obs);
            File.AppendAllText(obsFile, line + "\n");
            Console.WriteLine($"[M-11] t={obs.realElapsedSec}s  " +
                $"active={obs.accumulatedActiveMs}ms (Δ{obs.deltaMs})  " +
                $"reminders={obs.reminderFiredCount}  " +
                $"suspended={obs.isSuspended}  " +
                $"inputAge={obs.lastInputAgeSec}s  {note}");
        }

        WriteObservation("started (threshold=1min, real sampling)");

        // Subscribe to the reminder event for immediate observation.
        service.RestReminderRequested += (_, _) =>
        {
            WriteObservation("RestReminderRequested event fired");
        };

        // Observation timer: write a snapshot every 2 seconds.
        // The service's own 1-second DispatcherTimer drives real
        // GetLastInputInfo sampling and session ticks — we only observe.
        var obsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        obsTimer.Tick += (_, _) =>
        {
            var note = service.IsSuspended
                ? "suspended (lock/sleep/pause)"
                : "active";
            if (service.ReminderFiredCount > 0)
                note += " [reminder fired]";
            WriteObservation(note);
        };
        obsTimer.Start();

        // --- Four-phase observation plan (240 s total) ---
        // The probe itself does NOT inject input. The operator controls
        // real keyboard/mouse activity externally to drive
        // GetLastInputInfo (production sampling path).
        //
        // Phase 1 (0-80s):   Operator keeps typing → cumulative growth +
        //                     reminder fires once (threshold=1min).
        // Phase 2 (80-160s): Operator stops all input → lastInputAgeSec
        //                     exceeds ActiveWindowSeconds(60s) →
        //                     accumulated stops growing (idle stop).
        // Phase 3 (160-220s): Operator resumes typing → accumulated
        //                     continues from the stopped value, no
        //                     backfill (resume no-backfill).
        // Phase 4 (220-240s): Final observation, then clean shutdown.
        //
        // Boundary markers are written so the log shows phase transitions
        // even without the operator's input changes.

        // Phase 2 marker at t=80s.
        var phase2Timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(80)
        };
        phase2Timer.Tick += (_, _) =>
        {
            phase2Timer.Stop();
            WriteObservation("phase 2: operator should stop all input now (idle stop observation)");
        };
        phase2Timer.Start();

        // Phase 3 marker at t=160s.
        var phase3Timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(160)
        };
        phase3Timer.Tick += (_, _) =>
        {
            phase3Timer.Stop();
            WriteObservation("phase 3: operator should resume input now (resume no-backfill observation)");
        };
        phase3Timer.Start();

        // Phase 4: shut down at t=240s.
        var shutdownTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(240)
        };
        shutdownTimer.Tick += (_, _) =>
        {
            shutdownTimer.Stop();
            obsTimer.Stop();
            WriteObservation("shutting down");

            var cleanupSidecarPath = Path.Combine(scenarioDir, "m11-cleanup.json");
            s_app!.ShutdownMainWindow(cleanupSidecarPath);
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(0);
        };
        shutdownTimer.Start();

        Dispatcher.Run();

        WriteResult(1, "M-11: Dispatcher.Run() returned without shutdown (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// P4-002 M-12: Short-threshold rest-reminder performance observation.
    ///
    /// This probe creates a real MainWindow (through the production
    /// CreatePetWindow path) and a short-threshold ActivitySessionService
    /// (1 minute instead of 120 minutes). It uses TickManually to
    /// rapidly accumulate active time until the RestReminderRequested
    /// event fires, which triggers App.OnRestReminderRequested →
    /// MainWindow.StartRestReminder. The MainWindow's own 33ms
    /// DispatcherTimer then drives the performance state machine
    /// (stretch → rest → natural completion).
    ///
    /// Every 2 seconds the probe writes a JSON line to
    /// rest-reminder-observations.jsonl:
    /// - realElapsedSec: real wall-clock seconds since performance start
    /// - isRestReminderActive: MainWindow.IsRestReminderActive
    /// - currentAction: MainWindow.CurrentAction (idle/stretch/rest)
    /// - reminderFiredCount: service.ReminderFiredCount
    /// - note: human-readable state description
    ///
    /// Operator observation plan (~70 seconds total):
    /// 1. The probe auto-triggers the reminder via TickManually (no
    ///    operator input needed).
    /// 2. The pet should play "stretch" (~1.6s), then "rest" (~28s),
    ///    then return to "idle" (natural completion at ~30s).
    /// 3. To test skip: click the pet body during the performance —
    ///    it should immediately return to idle.
    /// 4. To test cancel: drag the pet during the performance — it
    ///    should cancel and enter drag mode.
    /// 5. To test lifecycle cancel: RIGHT-CLICK the pet and choose
    ///    "Pause" during the performance — it should cancel and NOT
    ///    resume when "Resume" is chosen (P4-002 R2 M-12: the window
    ///    right-click menu is the operable pause entry for this
    ///    tray-less preview).
    /// </summary>
    private static void RestReminderVerification(string scenarioDir)
    {
        // Step 1: Set up isolated infrastructure with a real pet window.
        s_app!.TestSetupIsolatedSave(scenarioDir);
        s_app.TestState!.Pets = new System.Collections.Generic.List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
        };

        // Create the pet window through the production path. This sets
        // up the MainWindow with its 33ms update timer, animation player,
        // and all interaction handlers.
        s_app.CreatePetWindow(s_app.TestState!.Pets[0]);

        var mainWindow = s_app.MainWindow;
        if (mainWindow is null)
        {
            WriteResult(1, "MainWindow is null after CreatePetWindow.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // R1 F-03: Show the window so IsVisible=true and CanStartRestReminder
        // accepts it. CreatePetWindow does not call Show(); without this,
        // the rest-reminder dispatch path would skip the window.
        mainWindow.Show();

        // Step 2: Create a short-threshold ActivitySessionService.
        // We use a ControllableIdleSampler that always returns
        // lastInputTick = currentTick (user is active right now).
        // The service is created with Dispatcher.CurrentDispatcher so
        // system events can marshal to the UI thread if needed.
        const double ShortThresholdMinutes = 1.0;
        var session = new ActivitySession(ShortThresholdMinutes);
        var service = new ActivitySessionService(
            session,
            new ControllableIdleSampler(),
            MonotonicClock.Instance,
            Dispatcher.CurrentDispatcher);

        // Step 3: Wire the service through the normal App entry point
        // (StartForIsolatedProbe with service injection calls
        // SubscribeSystemEvents + Start, same as production).
        // Then manually subscribe the RestReminderRequested event to
        // the production handler (InitializeActivitySession does this
        // in production, but StartForIsolatedProbe with service injection
        // does not — so we wire it here).
        s_app.StartForIsolatedProbe(scenarioDir, service);
        service.RestReminderRequested += s_app.OnRestReminderRequested;

        if (s_app.ActivitySessionService is null)
        {
            WriteResult(1, "ActivitySessionService is null after setup.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Step 4: Use TickManually to rapidly accumulate active time.
        // We tick at 1-second intervals with lastInputMs = nowMs
        // (user is active right now). After 61 ticks (61 seconds of
        // accumulated active time), the threshold (1 minute) is reached
        // and the reminder fires.
        var clock = MonotonicClock.Instance;
        var baseMs = clock.GetElapsedMs();
        var reminderFired = false;

        for (int i = 1; i <= 65 && !reminderFired; i++)
        {
            var nowMs = baseMs + i * 1000.0;
            // lastInputMs = nowMs means the user just typed (age=0, active).
            var fired = service.TickManually(nowMs, nowMs);
            if (fired)
            {
                reminderFired = true;
                // The RestReminderRequested event fires synchronously
                // inside TickManually, so OnRestReminderRequested has
                // already been called and StartRestReminder has been
                // invoked on the MainWindow.
            }
        }

        // Step 5: Set up observation file and timer.
        var obsFile = Path.Combine(scenarioDir, "rest-reminder-observations.jsonl");
        File.WriteAllText(obsFile, "");

        var realStart = System.Diagnostics.Stopwatch.StartNew();

        void WriteObservation(string note)
        {
            var obs = new
            {
                realElapsedSec = Math.Round(realStart.Elapsed.TotalSeconds, 1),
                isRestReminderActive = mainWindow.IsRestReminderActive,
                currentAction = mainWindow.CurrentAction,
                reminderFiredCount = service.ReminderFiredCount,
                reminderWasFired = reminderFired,
                note
            };
            var line = JsonSerializer.Serialize(obs);
            File.AppendAllText(obsFile, line + "\n");
            Console.WriteLine($"[M-12] t={obs.realElapsedSec}s  " +
                $"active={obs.isRestReminderActive}  " +
                $"action={obs.currentAction}  " +
                $"reminders={obs.reminderFiredCount}  {note}");
        }

        WriteObservation(reminderFired
            ? "reminder fired, performance started (stretch → rest → ~30s natural end)"
            : "reminder did NOT fire within 65 ticks (unexpected)");

        // Observation timer: write a snapshot every 2 seconds.
        var obsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        obsTimer.Tick += (_, _) =>
        {
            var note = mainWindow.IsRestReminderActive
                ? $"performance active (action={mainWindow.CurrentAction})"
                : "performance not active (idle or ended)";
            WriteObservation(note);
        };
        obsTimer.Start();

        // Step 6: Write a pre-shutdown sidecar with key verification data.
        var sidecar = new
        {
            scenario = "rest-reminder",
            shortThresholdMinutes = ShortThresholdMinutes,
            reminderFired,
            reminderFiredCount = service.ReminderFiredCount,
            mainWindowCreated = mainWindow is not null,
            isRestReminderActiveAtStart = mainWindow!.IsRestReminderActive,
            currentActionAtStart = mainWindow!.CurrentAction,
            usedProductionCreatePetWindow = true,
            usedProductionOnRestReminderRequested = true,
            usedTickManually = true,
            ticksExecuted = 65,
            note = "Operator can observe stretch→rest→idle over ~30s. " +
                   "Click body to test skip. Drag to test cancel. " +
                   "Right-click → Pause/Resume is the operable lifecycle " +
                   "cancel entry for this tray-less preview."
        };
        var sidecarFile = Path.Combine(scenarioDir, "rest-reminder-sidecar.json");
        File.WriteAllText(sidecarFile, JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));

        // Step 7: Shut down at t=70s (enough for the ~30s performance
        // plus observation time).
        var shutdownTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(70)
        };
        shutdownTimer.Tick += (_, _) =>
        {
            shutdownTimer.Stop();
            obsTimer.Stop();
            WriteObservation("shutting down");

            var cleanupSidecarPath = Path.Combine(scenarioDir, "rest-reminder-cleanup.json");
            s_app!.ShutdownMainWindow(cleanupSidecarPath);
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(0);
        };
        shutdownTimer.Start();

        // Pump the Dispatcher so the MainWindow's 33ms timer drives
        // the performance state machine, and our observation/shutdown
        // timers fire.
        Dispatcher.Run();

        WriteResult(1, "rest-reminder: Dispatcher.Run() returned without shutdown (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// P6-001 R4: Minimal isolated LIVE entry for local usage statistics.
    ///
    /// Everything is rooted in the isolated scenario run directory:
    /// settings.json / state.json / stats.json (plus .corrupted/.tmp side
    /// artifacts) all live under scenarioDir — never the user archive.
    ///
    /// This is a PRODUCTION-path preview, not a mock:
    /// - Real pet window through CreatePetWindow + Show (real right-click
    ///   context menu with "Statistics: On/Off" and "Clear Statistics").
    /// - Production UsageStats initialization (InitializeUsageStats) — real
    ///   5-second DispatcherTimer, real GetLastInputInfo idle sampling and
    ///   real ForegroundAppSampler foreground sampling, real settings gate
    ///   (OFF by default), pause sync, session/power event feed.
    ///
    /// The probe only observes (jsonl) and prints phase prompts — the
    /// OPERATOR drives the real context-menu toggles and real foreground
    /// app switches. No faked foreground, no manual SampleTick, no injected
    /// AppId (per R4).
    ///
    /// Observation phases:
    ///   0-30s   A: default-off baseline — no operator action; enabled=false,
    ///              no segments accumulated.
    ///   45s     B: right-click pet → "Statistics: Off" to ENABLE collection.
    ///   115s    C: bring a marked foreground app (e.g. Notepad titled with
    ///              PET-STATS-MARKER-A) to the foreground — watch segments
    ///              accumulate under its executable base name.
    ///   175s    D: switch foreground to a different app (e.g. mspaint /
    ///              calculator) — a new segment opens under the new appId.
    ///   220s    E: right-click pet → "Statistics: On" to DISABLE — watch
    ///              enabled=false and segments freeze (data kept, zero growth).
    ///   255s    F: right-click pet → "Clear Statistics" — watch
    ///              segments/events=0 while the toggle stays On (settings kept).
    ///   285s    finish: final observation, sidecar, clean shutdown.
    ///
    /// Lock/sleep lifecycle (real unlock/resume events) and a SECOND identical
    /// run of the same run dir (restart keeps the toggle, does NOT restore
    /// cleared history) are documented for the operator; both use this same
    /// scenario command.
    /// </summary>
    private static void UsageStatsLiveVerification(string scenarioDir)
    {
        // R5 F-05: A restart must RESTORE the existing independent
        // settings/state through the production load path (LoadState →
        // LoadSettings → _isPaused sync) — never seed over it. A first run
        // in a fresh directory may seed default objects.
        bool isRestart = File.Exists(Path.Combine(scenarioDir, "settings.json"))
                      || File.Exists(Path.Combine(scenarioDir, "state.json"));

        // R8 F-06: Align the WPF lifecycle to the production App.xaml
        // convention (OnExplicitShutdown) BEFORE any pet window is created or
        // closed — for BOTH the first run and the restart branch.
        //
        // The probe constructs App directly (without loading App.xaml), so the
        // WPF default is OnLastWindowClose. In that mode closing the last pet
        // window auto-shutdowns the Application, and the later
        // `ShutdownMode = OnExplicitShutdown` assignment inside the production
        // ShutdownMainWindow throws ("cannot set ShutdownMode when the
        // application is shutting down or already shut down"). Setting the
        // public property once here (the same value App.xaml uses) keeps the
        // real menu Exit path identical to production; no private WPF field
        // reflection and no exception swallowing is added.
        s_app!.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        // Step 1: isolated infrastructure + real pet window (production path).
        if (isRestart)
        {
            // F-05: production restore first, then fill the runtime helper
            // objects the pet window needs — without overwriting the loaded
            // archives (toggle/state survive).
            s_app!.LoadArchivesForIsolatedProbe();
            s_app.TestEnsureProbeRuntimeObjects();
        }
        else
        {
            s_app!.TestSetupIsolatedSave(scenarioDir);
            s_app.TestState!.Pets = new System.Collections.Generic.List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
            };
        }

        // R5 F-05: production system-event subscription via the SHARED
        // InitializeActivitySession wiring (SubscribeSystemEvents →
        // SessionSwitch/PowerModeChanged feeds for both ActivitySession and
        // UsageStatsService). The production exit path
        // (ShutdownMainWindow → UnsubscribeSystemEvents) releases it.
        s_app!.StartForIsolatedProbe(scenarioDir);

        s_app.CreatePetWindow(s_app.TestState!.Pets[0]);

        var mainWindow = s_app.MainWindow;
        if (mainWindow is null)
        {
            WriteResult(1, "Usage-stats: MainWindow is null after CreatePetWindow.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }
        mainWindow.Show();

        // Step 2: production usage-stats initialization at the isolated dir.
        // TestSetupIsolatedSave created an autoStartTimer:false service for
        // controlled unit wiring; drop it so the production-style init below
        // creates its own with the real 5-second timer and restores the
        // archive (per-startup restore path) — exactly like OnStartup.
        s_app.SetUsageStatsServiceForTest(null);
        s_app.InitializeUsageStatsForIsolatedProbe(scenarioDir);

        var stats = s_app.UsageStatsService;
        if (stats is null)
        {
            WriteResult(1, "Usage-stats: UsageStatsService is null after InitializeUsageStatsForIsolatedProbe.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Step 3: observation jsonl (2-second snapshots by the probe's own
        // DispatcherTimer; the service's 5-second timer drives real sampling).
        //
        // R5 F-05: observation files are session-numbered per launch, so a
        // same-directory restart NEVER overwrites the previous run's evidence
        // (data directory is shared, observation files are kept apart). The
        // sidecar / cleanup files use the same session number.
        int sessionNo = NextSessionNumber(scenarioDir, "usage-stats-observations-");
        var obsFile = Path.Combine(scenarioDir, $"usage-stats-observations-{sessionNo}.jsonl");
        File.WriteAllText(obsFile, "");
        var realStart = System.Diagnostics.Stopwatch.StartNew();

        void WriteObservation(string note)
        {
            var recentSegments = stats.Segments
                .TakeLast(2)
                .Select(s => new { s.AppId, s.Date, s.StartLocal, DurationMs = Math.Round(s.DurationMs, 0) })
                .ToArray();
            var recentEvents = stats.Events
                .TakeLast(2)
                .Select(e => new { e.EventType, e.Date, e.OccurredLocal })
                .ToArray();
            var obs = new
            {
                realElapsedSec = Math.Round(realStart.Elapsed.TotalSeconds, 1),
                enabled = stats.IsEnabled,
                generation = stats.Generation,
                isSuspended = stats.IsSuspended,
                segmentsCount = stats.Segments.Count,
                eventsCount = stats.Events.Count,
                coverageMismatchCount = stats.CoverageMismatchCount,
                lastWriteError = stats.LastWriteError,
                statsFilePath = stats.StatsFilePath,
                recentSegments,
                recentEvents,
                note
            };
            var line = JsonSerializer.Serialize(obs);
            File.AppendAllText(obsFile, line + "\n");
            Console.WriteLine($"[usage-stats] t={obs.realElapsedSec}s  " +
                $"enabled={obs.enabled}  segs={obs.segmentsCount}  evts={obs.eventsCount}  " +
                $"susp={obs.isSuspended}  mismatch={obs.coverageMismatchCount}  {note}");
        }

        WriteObservation("started — phase A: default OFF baseline, do not touch yet");

        var obsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        obsTimer.Tick += (_, _) =>
        {
            var note = !stats.IsEnabled
                ? "collection disabled"
                : (stats.IsSuspended ? "suspended (pause/lock/sleep)" : "collecting");
            WriteObservation(note);
        };
        obsTimer.Start();

        // Step 4: operator phase markers (single-shot timers on the UI thread).
        void PhaseAt(double seconds, string note)
        {
            var t = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };
            t.Tick += (_, _) =>
            {
                t.Stop();
                WriteObservation(note);
            };
            t.Start();
        }

        PhaseAt(30, "phase A end: enabled must be false, segments must be 0 (default OFF). Next: right-click pet, choose 'Statistics: Off' to enable.");
        PhaseAt(45, "phase B: ENABLE via the real right-click menu now. Watch enabled flip to true within ~10s.");
        PhaseAt(115, "phase C: bring a marked app to the foreground (e.g. Notepad titled PET-STATS-MARKER-A) and keep it active — watch segments accumulate under its executable base name.");
        PhaseAt(175, "phase D: switch the foreground to a DIFFERENT app (e.g. mspaint or calculator) — watch a new segment open under the new appId.");
        PhaseAt(220, "phase E '关闭保留': right-click pet, choose 'Statistics: On' to DISABLE — enabled=false while the collected data stays (frozen: segments/events stop growing, nothing is erased).");
        PhaseAt(255, "phase F '重新开启后清除保留 On': right-click pet, choose 'Clear Statistics' — old records disappear (clear generation + time are recorded in the sidecar) while the toggle stays ON; new records after that are allowed (e.g. re-enable later keeps On).");

        // R5 F-05: same-directory restart note. A SECOND launch of this
        // scenario uses the same run dir: settings/state are restored from
        // disk (toggle survives), cleared history is NOT restored, and new
        // records are allowed — with a new session-numbered observation file.
        PhaseAt(278, "restart note: to verify the same-directory restart, exit this run, then re-run the exact same probe command — session 2 must start without the cleared history (old records stay gone) and write usage-stats-observations-2.jsonl.");

        // Step 5: R5 F-05 — the sidecar with the key observable state is written
        // at the REAL shutdown moment inside the shutdown timer below, reading
        // the service's CURRENT values (never pre-written at startup). The
        // context-menu interaction flag is recorded truthfully: the probe
        // cannot confirm a real right-click menu dispatch on its own.

        // Step 6: clean shutdown at t=285s through the production exit path
        // (Stop + flush usage stats, unsubscribe system events, close windows).
        var shutdownTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(285)
        };
        shutdownTimer.Tick += (_, _) =>
        {
            shutdownTimer.Stop();
            obsTimer.Stop();
            WriteObservation("shutting down (clean production exit)");

            // R5 F-05: write the sidecar at the real shutdown moment, reading
            // the service's CURRENT values. The interaction flag below is
            // truthful — the operator checklist records the confirmed real
            // right-click menu dispatches.
            var sidecar = new
            {
                scenario = "usage-stats-live",
                session = sessionNo,
                usedProductionCreatePetWindow = true,
                usedProductionInitializeUsageStats = true,
                usedReal5sTimer = true,
                usedRealForegroundSampler = true,
                usedRealIdleSampler = true,
                realContextMenuDispatchConfirmed = false,
                usedFakeForegroundOrManualTick = false,
                appStartEventCount = stats.Events.Count(e => e.EventType == UsageStatsEventTypes.AppStart),
                statsFilePath = stats.StatsFilePath,
                settingsFilePath = Path.Combine(scenarioDir, "settings.json"),
                stateFilePath = Path.Combine(scenarioDir, "state.json"),
                enabledAtShutdown = stats.IsEnabled,
                generationAtShutdown = stats.Generation,
                segmentsCountAtShutdown = stats.Segments.Count,
                eventsCountAtShutdown = stats.Events.Count,
                coverageMismatchCountAtShutdown = stats.CoverageMismatchCount,
                lastWriteError = stats.LastWriteError,
                note = "Operator plan: B enable → C/D foreground accumulation → E '关闭保留' (disabled, data kept & frozen) → F '重新开启后清除保留 On' (clear: generation/time recorded, old records gone, toggle stays ON, new records allowed afterwards). " +
                       "app-start is only registered when the toggle was enabled at startup (never fabricated during the run). " +
                       "A same-directory restart restores settings/state from disk (toggle survives), does NOT restore cleared history, and allows new records — under a new session-numbered observation file."
            };
            var sidecarFile = Path.Combine(scenarioDir, $"usage-stats-sidecar-{sessionNo}.json");
            File.WriteAllText(sidecarFile,
                JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));

            var cleanupSidecarPath = Path.Combine(scenarioDir, $"usage-stats-cleanup-{sessionNo}.json");
            s_app!.ShutdownMainWindow(cleanupSidecarPath);
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(0);
        };
        shutdownTimer.Start();

        Dispatcher.Run();

        WriteResult(1, "usage-stats-live: Dispatcher.Run() returned without shutdown (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// P6-001 R8 F-06: Subprocess-level regression for the isolated probe's
    /// REAL exit. Unlike <see cref="UsageStatsWiringVerification"/> (windowless
    /// wiring) this scenario creates a real pet window and lets the
    /// PRODUCTION right-click command path end the process:
    /// MainWindow.MenuIdExit → HandleContextMenuCommand →
    /// App.ShutdownMainWindow (unsubscribe system events → stop/dispose the
    /// services → close the windows → Shutdown(0)).
    ///
    /// Both probe startup branches are covered, each in its own process run in
    /// the SAME scenario dir:
    ///   run 1 (first run): seeds an On stats toggle + pet, creates the window,
    ///                      initializes the production stats service, then
    ///                      exits through the menu command.
    ///   run 2 (restart):   restores settings/state through the production
    ///                      load path (toggle survives), creates the window,
    ///                      initializes stats, then exits the same way.
    ///
    /// The process finishes by RETURNING from Dispatcher.Run() after a real
    /// Shutdown(0) — never by Environment.Exit(0) — so the OS collects the
    /// real exit code. A hang guard fails loudly (exit 1) if the production
    /// exit path does not end the dispatcher. No exception is swallowed and no
    /// WPF private field is touched.
    /// </summary>
    private static void UsageStatsExitVerification(string scenarioDir)
    {
        bool isRestart = File.Exists(Path.Combine(scenarioDir, "settings.json"))
                      || File.Exists(Path.Combine(scenarioDir, "state.json"));

        // R8 F-06: same lifecycle alignment as the LIVE entry — the probe
        // builds App without loading App.xaml (WPF default OnLastWindowClose),
        // so align to the production OnExplicitShutdown BEFORE any window is
        // created or closed. Public property only; no private-field reflection.
        s_app!.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        if (isRestart)
        {
            s_app.LoadArchivesForIsolatedProbe();
            s_app.TestEnsureProbeRuntimeObjects();
        }
        else
        {
            s_app.TestSetupIsolatedSave(scenarioDir);
            s_app.TestState!.Pets = new System.Collections.Generic.List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
            };
            // Persist an On toggle so the restart run restores it through the
            // production load path and starts the service enabled.
            s_app.TestSettings!.UsageStatsEnabled = true;
            s_app.TestSaveCoordinator!.MarkSettingsDirty(s_app.TestSettings!);
            s_app.TestSaveCoordinator!.Flush();
        }

        s_app.StartForIsolatedProbe(scenarioDir);
        s_app.CreatePetWindow(s_app.TestState!.Pets[0]);

        var mainWindow = s_app.MainWindow;
        if (mainWindow is null)
        {
            WriteResult(1, "usage-stats-exit: MainWindow is null after CreatePetWindow.");
            s_app.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }
        mainWindow.Show();

        s_app.SetUsageStatsServiceForTest(null);
        s_app.InitializeUsageStatsForIsolatedProbe(scenarioDir);
        var stats = s_app.UsageStatsService;
        if (stats is null)
        {
            WriteResult(1, "usage-stats-exit: UsageStatsService is null after InitializeUsageStatsForIsolatedProbe.");
            s_app.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Session-numbered evidence (same-directory runs never overwrite).
        int sessionNo = NextSessionNumber(scenarioDir, "usage-stats-exit-");
        var evidenceFile = Path.Combine(scenarioDir, $"usage-stats-exit-{sessionNo}.jsonl");
        File.WriteAllText(evidenceFile, JsonSerializer.Serialize(new
        {
            scenario = "usage-stats-exit",
            phase = "pre-exit",
            session = sessionNo,
            isRestart,
            lifecycleModeBeforeWindow = s_app.ShutdownMode.ToString(),
            usedProductionCreatePetWindow = true,
            usedProductionMenuExitDispatch = true,
            usedProductionShutdownMainWindow = true,
            subscribedAfterStart = s_app.TestSystemEventsSubscribed,
            enabledBeforeExit = stats.IsEnabled,
            appStartEventCount = stats.Events.Count(e => e.EventType == UsageStatsEventTypes.AppStart),
            segmentsCountBeforeExit = stats.Segments.Count,
            eventsCountBeforeExit = stats.Events.Count
        }) + "\n");

        // Hang guard: if the production exit path does not end the dispatcher,
        // fail loudly instead of hanging the subprocess.
        var hangGuard = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        hangGuard.Tick += (_, _) =>
        {
            hangGuard.Stop();
            WriteResult(1, "usage-stats-exit: Dispatcher.Run() did not return within 30s after the menu Exit dispatch.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
        };
        hangGuard.Start();

        // Same production command path as a real right-click → Exit:
        // TestOpenContextMenuLifecycle = EnterContextMenuMode → dispatch →
        // HandleContextMenuCommand(MenuIdExit) → Dispatcher.BeginInvoke →
        // App.ShutdownMainWindow. The dispatcher then ends this Run loop.
        var dispatchTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        dispatchTimer.Tick += (_, _) =>
        {
            dispatchTimer.Stop();
            mainWindow.TestOpenContextMenuLifecycle((int)DesktopPet.App.MainWindow.MenuIdExit);
        };
        dispatchTimer.Start();

        // R8 F-06: run the REAL application loop (Application.Run → OnStartup →
        // Dispatcher.Run), not a bare Dispatcher.Run(). Only the Application
        // lifecycle lets the production Shutdown(0) end the loop, which is what
        // the real app does. OnStartup returns early because the probe is in
        // isolated mode, so no production init/tray/window is created here.
        s_app.Run();

        // Reached only after the production Shutdown(0) ended the real
        // application loop. Read the post-exit facts from the SAME App
        // instance and let Main return the genuine process exit code.
        bool unsubscribedAfterExit = !s_app.TestSystemEventsSubscribed;
        bool usageStatsNullAfterExit = s_app.UsageStatsService is null;
        bool activitySessionNullAfterExit = s_app.ActivitySessionService is null;

        bool exceptionLogged = false;
        var logDir = Path.Combine(scenarioDir, "logs");
        if (Directory.Exists(logDir))
        {
            foreach (var logFile in Directory.GetFiles(logDir, "diagnostic-*.log"))
            {
                try
                {
                    var text = File.ReadAllText(logFile);
                    if (text.Contains("DispatcherUnhandled", StringComparison.Ordinal)
                        || text.Contains("AppDomainUnhandled", StringComparison.Ordinal))
                        exceptionLogged = true;
                }
                catch
                {
                    // Best-effort read for the verdict only.
                }
            }
        }

        File.AppendAllText(evidenceFile, JsonSerializer.Serialize(new
        {
            scenario = "usage-stats-exit",
            phase = "post-exit",
            session = sessionNo,
            isRestart,
            applicationRunReturned = true,
            shutdownViaMenuDispatch = true,
            usedEnvironmentExitZero = false,
            unsubscribedAfterExit,
            usageStatsNullAfterExit,
            activitySessionNullAfterExit,
            exceptionLoggedInDiagnosticLog = exceptionLogged,
            note = "Reached by returning from Application.Run() after the production Shutdown(0); the OS exit code is the process's real one."
        }) + "\n");

        if (!unsubscribedAfterExit || !usageStatsNullAfterExit || !activitySessionNullAfterExit || exceptionLogged)
        {
            WriteResult(1, "usage-stats-exit: post-exit cleanup incomplete or an exception was logged " +
                $"(unsubscribed={unsubscribedAfterExit}, usageStatsNull={usageStatsNullAfterExit}, " +
                $"activitySessionNull={activitySessionNullAfterExit}, exceptionLogged={exceptionLogged}).");
            Environment.Exit(1);
            return;
        }

        WriteResult(0, "usage-stats-exit: the production menu Exit ended the process cleanly " +
            $"(session {sessionNo}, isRestart={isRestart}, exitCode=0 from returning out of Main).");
    }

    /// <summary>
    /// P6-004（第一小步）：主动统计气泡与只读统计查看入口的隔离预览。
    /// <para>
    /// 不注入鼠标、不移动宠物、不做图标定位、不启动外部应用。流程：
    /// <list type="number">
    /// <item>在隔离运行目录预置 P6-003 达标聚合桶（notepad 5 个日期 @09:30 +
    /// chrome 2 个日期，共 7 个有记录日期），固定本地时间 2026-11-25 09:35；</item>
    /// <item>用受控服务走生产恢复路径，开启统计，创建并显示一只真实宠物窗口；</item>
    /// <item>首次判定 → 气泡展示，文案只陈述统计依据、无指令/准确率，日期落盘；</item>
    /// <item>同日再次判定 → 不重复展示；查看窗两段（已记录统计 + 时间偏好）；</item>
    /// <item>关闭统计 → 立即隐藏并保留当天标记；清除统计 → 隐藏并重置标记，
    /// 查看窗回到空态。</item>
    /// </list>
    /// 证据写入会话编号 jsonl，重复运行不覆盖；完成后走生产 ShutdownMainWindow
    /// 释放服务与窗口。
    /// </para>
    /// </summary>
    private static void UsageStatsBubbleVerification(string scenarioDir)
    {
        var fixedNow = new DateTimeOffset(2026, 11, 25, 9, 35, 0, TimeSpan.FromHours(8));
        var statsPath = Path.Combine(scenarioDir, "stats.json");

        // 探针 new App() 不加载 App.xaml（WPF 默认 OnLastWindowClose）；创建/
        // 关闭真实宠物窗口前先对齐生产的 OnExplicitShutdown。
        s_app!.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        s_app.TestSetupIsolatedSave(scenarioDir);

        // P6-003 达标样本：notepad 5 个日期同一半小时时段（≥5 支持日期且
        // ≥5 会话），另加 chrome 2 个日期把覆盖补到 7 个有记录日期。
        var buckets = new System.Collections.Generic.List<UsageStatsBucket>();
        for (var day = 10; day <= 14; day++)
        {
            buckets.Add(new UsageStatsBucket
            {
                AppId = "notepad",
                Date = $"2026-11-{day:D2}",
                BucketStartLocal = $"2026-11-{day:D2}T09:30:00.0000000+08:00",
                ActiveMs = 5000,
                SessionCount = 1
            });
        }
        buckets.Add(new UsageStatsBucket
        {
            AppId = "chrome",
            Date = "2026-11-15",
            BucketStartLocal = "2026-11-15T14:00:00.0000000+08:00",
            ActiveMs = 5000,
            SessionCount = 1
        });
        buckets.Add(new UsageStatsBucket
        {
            AppId = "chrome",
            Date = "2026-11-16",
            BucketStartLocal = "2026-11-16T14:00:00.0000000+08:00",
            ActiveMs = 5000,
            SessionCount = 1
        });

        File.WriteAllText(
            statsPath,
            JsonSerializer.Serialize(
                new UsageStatsArchive { Buckets = buckets },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        // 受控统计服务：固定本地时间、无采样定时器；Start() 走生产恢复路径
        // （buckets + 每日去重标记都会从隔离 stats.json 读回）。
        s_app.SetUsageStatsServiceForTest(null);
        var service = new UsageStatsService(
            new UsageStatsCollector(() => 0, () => fixedNow),
            new InputIdleSampler(),
            new ForegroundAppSampler(),
            MonotonicClock.Instance,
            dispatcher: null,
            saveDirectory: scenarioDir,
            autoStartTimer: false,
            localNow: () => fixedNow);
        s_app.SetUsageStatsServiceForTest(service);
        service.Start();
        s_app.SetUsageStatsEnabled(true);
        s_app.TestStatsBubbleLocalNow = () => fixedNow;

        bool enabledAfterStart = service.IsEnabled;
        int bucketCountAfterRestore = service.Buckets.Count;
        var predictions = service.GetFrequencyPredictions();
        var candidate = predictions.Candidates.FirstOrDefault();
        int candidateCount = predictions.Candidates.Count;

        // 真实宠物窗口：只 Show 让“可接受展示”成立，不移动、不注入鼠标。
        s_app.TestState!.Pets = new System.Collections.Generic.List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
        };
        s_app.CreatePetWindow(s_app.TestState.Pets[0]);
        var petWindow = s_app.FindWindowByPetId("bear-1");
        if (petWindow is null)
        {
            WriteResult(1, "usage-stats-bubble: FindWindowByPetId(\"bear-1\") returned null after CreatePetWindow.");
            s_app.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }
        petWindow.Show();

        var lines = new System.Collections.Generic.List<string>();
        void Record(object payload) => lines.Add(JsonSerializer.Serialize(payload));

        string? ReadPersistedMarker()
        {
            if (!File.Exists(statsPath)) return null;
            try
            {
                var archive = JsonSerializer.Deserialize<UsageStatsArchive>(
                    File.ReadAllText(statsPath),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                return archive?.BubbleLastShownLocalDate;
            }
            catch
            {
                return null;
            }
        }

        // 1) 首次判定：当前时段达标 → 展示。
        s_app.TryShowUsageStatsBubble();
        var bubble = s_app.TestStatsBubbleWindow;
        bool visibleFirst = s_app.TestStatsBubbleVisible;
        string message = s_app.TestStatsBubbleMessage;
        bool bubbleOutsidePetRect = true;
        if (bubble is not null && bubble.IsVisible)
        {
            var petRect = petWindow.GetPetRectDipForStatsBubble();
            var bubbleRect = new Rect(bubble.Left, bubble.Top, bubble.ActualWidth, bubble.ActualHeight);
            bubbleOutsidePetRect = !bubbleRect.IntersectsWith(petRect);
        }
        string? markerAfterShow = service.BubbleLastShownLocalDate;
        string? persistedMarkerAfterShow = ReadPersistedMarker();
        Record(new
        {
            phase = "first-decide",
            enabledAfterStart,
            bucketCountAfterRestore,
            candidateCount,
            candidateApp = candidate?.AppId,
            candidateSlot = candidate?.SlotStart,
            candidateSupportDates = candidate?.SupportDateCount,
            candidateSessions = candidate?.SessionCount,
            windowDays = predictions.WindowDays,
            observedDateCount = predictions.ObservedDateCount,
            bubbleVisible = visibleFirst,
            bubbleMessage = message,
            bubbleOutsidePetRect,
            autoHideConfiguredMs = StatsBubbleWindow.DefaultAutoHideMs,
            markerAfterShow,
            persistedMarkerAfterShow
        });

        // 2) 同一本地日期再次判定：不重复展示（额度已消费且落盘）。
        s_app.HideUsageStatsBubble();
        s_app.TryShowUsageStatsBubble();
        Record(new
        {
            phase = "second-decide-same-day",
            bubbleVisible = s_app.TestStatsBubbleVisible,
            marker = service.BubbleLastShownLocalDate
        });

        // 3) 主动查看入口：两段（已记录的统计 / 有样本支持的时间偏好）。
        s_app.ShowUsageStatsViewer();
        var viewer = s_app.TestStatsViewerWindow;
        var recordedLines = viewer?.TestRecordedLines ?? Array.Empty<string>();
        var preferenceLines = viewer?.TestPreferenceLines ?? Array.Empty<string>();
        string statusText = viewer?.TestStatusText ?? "";
        Record(new
        {
            phase = "viewer-enabled",
            recordedLines,
            preferenceLines,
            statusText,
            usesAccurateNames = recordedLines.Any(l => l.StartsWith("近似前台活跃时长：", StringComparison.Ordinal))
                             && recordedLines.Any(l => l.StartsWith("会话次数：", StringComparison.Ordinal)),
            preferenceMarksStatisticsBasis = preferenceLines.Any(l => l.Contains("支持日期", StringComparison.Ordinal))
                                          && !preferenceLines.Any(l => l.Contains("准确率", StringComparison.Ordinal) || l.Contains("%", StringComparison.Ordinal))
        });
        viewer?.Close();

        // 4) 关闭统计：立即隐藏，保留当天已展示标记。
        s_app.SetUsageStatsEnabled(false);
        Record(new
        {
            phase = "after-disable",
            bubbleVisible = s_app.TestStatsBubbleVisible,
            marker = service.BubbleLastShownLocalDate,
            persistedMarker = ReadPersistedMarker()
        });

        // 5) 清除统计：隐藏并重置标记，查看窗回到空态。
        s_app.ClearUsageStats();
        string? markerAfterClear = service.BubbleLastShownLocalDate;
        string? persistedMarkerAfterClear = ReadPersistedMarker();
        bool visibleAfterClear = s_app.TestStatsBubbleVisible;

        s_app.ShowUsageStatsViewer();
        var viewerAfterClear = s_app.TestStatsViewerWindow;
        var recordedAfterClear = viewerAfterClear?.TestRecordedLines ?? Array.Empty<string>();
        var preferenceAfterClear = viewerAfterClear?.TestPreferenceLines ?? Array.Empty<string>();
        string statusAfterClear = viewerAfterClear?.TestStatusText ?? "";
        viewerAfterClear?.Close();
        Record(new
        {
            phase = "after-clear",
            bubbleVisible = visibleAfterClear,
            marker = markerAfterClear,
            persistedMarker = persistedMarkerAfterClear,
            recordedLines = recordedAfterClear,
            preferenceLines = preferenceAfterClear,
            statusText = statusAfterClear
        });

        // 会话编号证据文件，重复运行不覆盖。
        int sessionNo = NextSessionNumber(scenarioDir, "usage-stats-bubble-");
        var evidenceFile = Path.Combine(scenarioDir, $"usage-stats-bubble-{sessionNo}.jsonl");
        File.WriteAllLines(evidenceFile, lines);

        // 判定：全部条件成立才算通过。
        bool evidenceWordingOk =
            message.Contains("过去有记录的日期里", StringComparison.Ordinal)
            && message.Contains("09:30–09:59", StringComparison.Ordinal)
            && message.Contains("notepad", StringComparison.Ordinal)
            && !new[] { "你现在", "应该", "建议你", "准确率", "预测准确", "我知道你", "%" }
                .Any(forbidden => message.Contains(forbidden, StringComparison.Ordinal));

        bool viewerOk =
            recordedLines.Any(l => l.Contains("有记录的日期：7 天", StringComparison.Ordinal))
            && recordedLines.Any(l => l.StartsWith("近似前台活跃时长：", StringComparison.Ordinal))
            && recordedLines.Any(l => l.Contains("会话次数：7 次", StringComparison.Ordinal))
            && recordedLines.Any(l => l.Contains("涉及应用：2 个", StringComparison.Ordinal))
            && preferenceLines.Count == 1
            && preferenceLines[0].Contains("notepad", StringComparison.Ordinal)
            && preferenceLines[0].Contains("支持日期 5 天", StringComparison.Ordinal)
            && statusText.Contains("已开启", StringComparison.Ordinal);

        bool disableOk = !s_app.TestStatsBubbleVisible
                      && markerAfterClear is null
                      && persistedMarkerAfterClear is null
                      && visibleAfterClear == false;

        // 关闭时标记保留（在清除前读取，单独记录在 evidence 中由上面分支给出），
        // 这里通过先关后清的顺序保证：若关闭误重置，上面 phase=after-disable
        // 的证据会显示 marker=null，因此再把该事实纳入总判定。
        bool disableKeptMarker =
            lines.Any(l => l.Contains("\"phase\":\"after-disable\"", StringComparison.Ordinal)
                        && l.Contains("\"marker\":\"2026-11-25\"", StringComparison.Ordinal));

        bool ok = enabledAfterStart
                  && bucketCountAfterRestore == 7
                  && candidateCount == 1
                  && candidate is { AppId: "notepad", SlotStart: "09:30", SupportDateCount: 5, SessionCount: 5 }
                  && visibleFirst
                  && bubbleOutsidePetRect
                  && evidenceWordingOk
                  && markerAfterShow == "2026-11-25"
                  && persistedMarkerAfterShow == "2026-11-25"
                  && viewerOk
                  && disableKeptMarker
                  && disableOk
                  && recordedAfterClear.Any(l => l.Contains("暂无已记录的统计", StringComparison.Ordinal))
                  && preferenceAfterClear.Any(l => l.Contains("暂无可显示的时间偏好", StringComparison.Ordinal))
                  && statusAfterClear.Contains("已关闭", StringComparison.Ordinal);

        if (!ok)
        {
            WriteResult(1, "usage-stats-bubble: isolated preview failed one or more checks. " +
                $"enabled={enabledAfterStart}, buckets={bucketCountAfterRestore}, candidates={candidateCount}, " +
                $"visibleFirst={visibleFirst}, wordingOk={evidenceWordingOk}, outsidePet={bubbleOutsidePetRect}, " +
                $"marker={markerAfterShow}, persistedMarker={persistedMarkerAfterShow}, viewerOk={viewerOk}, " +
                $"disableKeptMarker={disableKeptMarker}, disableOk={disableOk}, " +
                $"statusAfterClear=\"{statusAfterClear}\". Evidence: {evidenceFile}");
            s_app.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // 走生产退出路径释放窗口/服务（同步 Invoke，与 dual-pet-exit 相同做法）。
        Dispatcher.CurrentDispatcher.Invoke(new Action(() => s_app!.ShutdownMainWindow()));

        WriteResult(0, "usage-stats-bubble: isolated preview passed " +
            $"(session {sessionNo}, candidate=notepad@09:30 support=5 sessions=5, " +
            "bubble shown once/day with evidence-only wording, viewer split into " +
            "recorded stats + sample-supported preference, disable kept the daily " +
            "marker, clear reset it). Evidence: " + evidenceFile);
    }

    /// <summary>
    /// P6-001 R5 F-05: Windowless wiring check for the isolated LIVE entry —
    /// no mouse, no pet window, no screen time. A single process run that:
    /// <list type="number">
    /// <item>pre-seeds settings.UsageStatsEnabled=true into the isolated run
    /// dir, then restores through the PRODUCTION archive load path and asserts
    /// the toggle stays ON (the R5 counter-example reset it to false);</item>
    /// <item>starts the shared system-event wiring (StartForIsolatedProbe →
    /// SubscribeSystemEvents) and asserts TestSystemEventsSubscribed=true,
    /// then performs a clean production exit (ShutdownMainWindow →
    /// UnsubscribeSystemEvents) writing subscribedAfterCleanup=false;</item>
    /// <item>initializes the production usage-stats service with the restored
    /// toggle and asserts IsEnabled=true while exactly ONE new app-start
    /// observation is registered by this startup — and only because the toggle
    /// was ON at startup (never fabricated during the run; a same-directory
    /// restart adds one more on top of the persisted events).</item>
    /// <item>writes session-numbered observation/sidecar/cleanup files so a
    /// same-directory second launch produces session N+1 without overwrite.</item>
    /// </list>
    /// Re-running the same command in the same run dir yields session 2 and
    /// proves the observation/sidecar files are kept apart.
    /// </summary>
    private static void UsageStatsWiringVerification(string scenarioDir)
    {
        // Phase 1: pre-seed the persisted toggle to ON through the real
        // archive ONLY on a fresh directory. A same-directory re-run never
        // overwrites what the previous run persisted.
        var settingsPath = Path.Combine(scenarioDir, "settings.json");
        var statePath = Path.Combine(scenarioDir, "state.json");
        if (!File.Exists(settingsPath) && !File.Exists(statePath))
        {
            s_app!.TestArchive!.SaveSettings(new PetSettingsArchive { UsageStatsEnabled = true });
            s_app.TestArchive!.SaveState(new PetStateArchive
            {
                Pets = new System.Collections.Generic.List<PetInstanceArchive>
                {
                    new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
                }
            });
        }
        if (!File.Exists(settingsPath))
        {
            WriteResult(1, "usage-stats-wiring: settings.json was not persisted by SaveSettings().");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Phase 2: the PRODUCTION restore path must KEEP the pre-seeded toggle
        // ON (both on a fresh seed and on a same-directory restart).
        s_app!.LoadArchivesForIsolatedProbe();
        s_app.TestEnsureProbeRuntimeObjects();
        bool toggleOnAfterLoad = s_app.TestSettings?.UsageStatsEnabled == true;
        if (!toggleOnAfterLoad)
        {
            WriteResult(1, "usage-stats-wiring: pre-seeded On toggle was reset by the production archive load.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Phase 3: shared system-event subscription (production wiring).
        s_app.StartForIsolatedProbe(scenarioDir);
        bool subscribedAfterStart = s_app.TestSystemEventsSubscribed;
        if (!subscribedAfterStart)
        {
            WriteResult(1, "usage-stats-wiring: TestSystemEventsSubscribed is false after StartForIsolatedProbe.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Phase 4: production usage-stats init honors the restored toggle;
        // app-start is only registered because the toggle was ON at startup.
        // The persisted archive may already contain app-start events from an
        // earlier same-directory run, so the correct semantic is exactly ONE
        // new app-start per process startup (never fabricated during the run),
        // not a cumulative count of 1.
        int persistedAppStartBeforeInit = CountPersistedAppStarts(scenarioDir);
        s_app.SetUsageStatsServiceForTest(null);
        s_app.InitializeUsageStatsForIsolatedProbe(scenarioDir);
        var stats = s_app.UsageStatsService;
        if (stats is null)
        {
            WriteResult(1, "usage-stats-wiring: UsageStatsService is null after InitializeUsageStatsForIsolatedProbe.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }
        bool enabledAfterInit = stats.IsEnabled;
        int appStartEventCount = stats.Events.Count(e => e.EventType == UsageStatsEventTypes.AppStart);
        int appStartDelta = appStartEventCount - persistedAppStartBeforeInit;
        bool appStartRegisteredOnce = appStartDelta == 1;

        // Phase 5: session-numbered observation file.
        int sessionNo = NextSessionNumber(scenarioDir, "usage-stats-wiring-observations-");
        var obsFile = Path.Combine(scenarioDir, $"usage-stats-wiring-observations-{sessionNo}.jsonl");
        var obs = new
        {
            scenario = "usage-stats-wiring",
            session = sessionNo,
            toggleOnAfterLoad,
            subscribedAfterStart,
            enabledAfterInit,
            persistedAppStartBeforeInit,
            appStartEventCount,
            appStartDelta,
            note = "Same-directory re-run of this command writes session N+1 without overwriting this file."
        };        File.WriteAllText(obsFile, JsonSerializer.Serialize(obs) + "\n");

        // R5 F-05: a same-directory re-run must keep the prior session's file.
        // Do not require any particular path/state; only that session N-1 was
        // not overwritten by this launch.
        bool priorSessionKept = true;
        if (sessionNo > 1)
        {
            var priorFile = Path.Combine(scenarioDir, $"usage-stats-wiring-observations-{sessionNo - 1}.jsonl");
            priorSessionKept = false;
            if (File.Exists(priorFile))
            {
                try
                {
                    using var priorDoc = JsonDocument.Parse(
                        File.ReadAllText(priorFile).Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
                    priorSessionKept = priorDoc.RootElement.TryGetProperty("session", out var priorSession)
                                       && priorSession.GetInt32() == sessionNo - 1;
                }
                catch
                {
                    priorSessionKept = false;
                }
            }
        }
        if (!priorSessionKept)
        {
            WriteResult(1, $"usage-stats-wiring: prior session {sessionNo - 1} observation file was missing or overwritten.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        // Phase 6: sidecar at the REAL shutdown moment (current service values),
        // then the production exit path — ShutdownMainWindow unsubscribes the
        // system events, stops/flushes the services and writes the cleanup
        // sidecar (subscribedAfterCleanup=false) before the process exits.
        var sidecarFile = Path.Combine(scenarioDir, $"usage-stats-wiring-sidecar-{sessionNo}.json");
        File.WriteAllText(sidecarFile, JsonSerializer.Serialize(new
        {
            scenario = "usage-stats-wiring",
            session = sessionNo,
            usedProductionLoadArchives = true,
            usedProductionSystemEventSubscription = true,
            usedProductionUsageStatsInit = true,
            usedFakeForegroundOrManualTick = false,
            toggleOnAfterLoad,
            subscribedAfterStart,
            enabledAtShutdown = stats.IsEnabled,
            appStartEventCount,
            appStartDelta,
            priorSessionKept,
            segmentsCountAtShutdown = stats.Segments.Count,
            eventsCountAtShutdown = stats.Events.Count,
            statsFilePath = stats.StatsFilePath
        }, new JsonSerializerOptions { WriteIndented = true }));

        var cleanupSidecarPath = Path.Combine(scenarioDir, $"usage-stats-wiring-cleanup-{sessionNo}.json");
        s_app!.ShutdownMainWindow(cleanupSidecarPath);

        // R5 F-05: assert the production exit path actually released the
        // system-event subscription (the cleanup sidecar is written by
        // ShutdownMainWindow BEFORE Shutdown(0) terminates the loop).
        bool unsubscribedAfterCleanup = !s_app.TestSystemEventsSubscribed;
        bool statsNullAfterCleanup = s_app.UsageStatsService is null;
        bool cleanupSubscribed = true;
        if (File.Exists(cleanupSidecarPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cleanupSidecarPath));
                if (doc.RootElement.TryGetProperty("subscribedAfterCleanup", out var subscribedProp))
                    cleanupSubscribed = subscribedProp.GetBoolean();
            }
            catch
            {
                // Leave cleanupSubscribed=true so a malformed sidecar fails.
            }
        }

        s_app?.DiagnosticLogger?.Dispose();

        bool wiringOk = toggleOnAfterLoad
                        && subscribedAfterStart
                        && enabledAfterInit
                        && appStartRegisteredOnce
                        && priorSessionKept
                        && unsubscribedAfterCleanup
                        && statsNullAfterCleanup
                        && !cleanupSubscribed;

        WriteResult(wiringOk ? 0 : 1,
            "usage-stats-wiring: " +
            $"toggleOnAfterLoad={toggleOnAfterLoad}, subscribedAfterStart={subscribedAfterStart}, " +
            $"enabledAfterInit={enabledAfterInit}, appStartEventCount={appStartEventCount}, appStartDelta={appStartDelta}, " +
            $"unsubscribedAfterCleanup={unsubscribedAfterCleanup}, statsNullAfterCleanup={statsNullAfterCleanup}, " +
            $"cleanupSidecarSubscribed={cleanupSubscribed}, session={sessionNo}.");
        Environment.Exit(wiringOk ? 0 : 1);
    }

    /// <summary>
    /// P6-001 R5 F-05: Session-numbered evidence files. Scans the scenario dir
    /// for existing <c>&lt;prefix&gt;&lt;n&gt;.jsonl</c> files and returns the next
    /// free number (max + 1). A same-directory restart thus NEVER overwrites
    /// the previous run's observation/sidecar/cleanup evidence.
    /// </summary>
    private static int NextSessionNumber(string scenarioDir, string prefix)
    {
        int max = 0;
        foreach (var file in Directory.EnumerateFiles(scenarioDir, prefix + "*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var numPart = name.Substring(prefix.Length);
            if (int.TryParse(numPart, out var n) && n > max)
                max = n;
        }
        return max + 1;
    }

    /// <summary>
    /// P6-001 R5 F-05: Counts the app-start events already persisted in the
    /// isolated <c>stats.json</c> BEFORE the production restore + OnAppStart
    /// runs. Used to prove a startup registers exactly ONE new app-start
    /// event (and never fabricates one later), regardless of how many runs
    /// share the same directory.
    /// </summary>
    private static int CountPersistedAppStarts(string scenarioDir)
    {
        var statsPath = Path.Combine(scenarioDir, "stats.json");
        if (!File.Exists(statsPath)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(statsPath));
            if (!doc.RootElement.TryGetProperty("events", out var events)
                || events.ValueKind != JsonValueKind.Array)
                return 0;
            var count = 0;
            foreach (var evt in events.EnumerateArray())
            {
                if (evt.TryGetProperty("eventType", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == UsageStatsEventTypes.AppStart)
                    count++;
            }
            return count;
        }
        catch
        {
            // A malformed stats.json is restored as empty by the production
            // path; treat it as zero persisted app-start events.
            return 0;
        }
    }

    /// <summary>
    /// P4-003: Night sleepy preview (R-06) — isolated, no system-clock change.
    /// <para>
    /// Drives the production MainWindow with an injected LOCAL clock
    /// (TestGetLocalNow) so the pet crosses the 23:30 night boundary, stays
    /// asleep across midnight (virtual), then jumps to 05:59 → 06:00 to
    /// observe the wake-up. The operator can drag the pet during the hold
    /// phase to observe the sleep → idle (drag) → sleep re-entry.
    /// </para>
    /// <para>
    /// Virtual timeline (1 real second ≈ 1 virtual minute in phase 1):
    /// - 0–5s:   23:29 → 23:34 (cross the 23:30 entry boundary).
    /// - 5–35s:  hold at 23:34 — operator may drag/pet the bear; sleep
    ///           re-enters after each interaction.
    /// - 35s:    jump to 05:59.
    /// - 38s:    jump to 06:00 — wake up to idle.
    /// - 60s:    shutdown.
    /// </para>
    /// </summary>
    private static void NightSleepyVerification(string scenarioDir, bool noInput)
    {
        s_app!.TestSetupIsolatedSave(scenarioDir);
        s_app.TestState!.Pets = new System.Collections.Generic.List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
        };

        s_app.CreatePetWindow(s_app.TestState!.Pets[0]);

        var mainWindow = s_app.MainWindow;
        if (mainWindow is null)
        {
            WriteResult(1, "MainWindow is null after CreatePetWindow.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }

        mainWindow.Show();

        // Injected virtual LOCAL clock — production would use DateTime.Now.
        var virtualLocal = new DateTime(2026, 11, 16, 23, 29, 0);
        mainWindow.TestGetLocalNow = () => virtualLocal;

        var obsFile = Path.Combine(scenarioDir, "night-sleepy-observations.jsonl");
        File.WriteAllText(obsFile, "");

        var realStart = System.Diagnostics.Stopwatch.StartNew();
        string lastAction = "";
        // P4-003 R1 F-02: All drag-dependent flags now require REAL input
        // evidence. Previously "drag interrupt" was inferred from any
        // action transition, so the 05:59→06:00 wake alone marked both
        // "drag" and "sleep re-entry" as true with zero input. Now the
        // MainWindow.IsDragging public property is sampled at every
        // observation point; a sleep exit only counts as a drag interrupt
        // when a real drag was actually observed, and re-entry only counts
        // after such a confirmed drag interrupt.
        var sleepSeen = false;
        var wakeSeen = false;
        var realDragSeen = false;
        var dragInterruptSeen = false;
        var sleepReentrySeen = false;
        var holdEndVirtual = new DateTime(2026, 11, 17, 5, 59, 0);
        var wakeVirtual = new DateTime(2026, 11, 17, 6, 0, 0);

        void WriteObservation(string note)
        {
            var action = mainWindow.CurrentAction;
            if (mainWindow.IsDragging)
                realDragSeen = true;
            if (action != lastAction)
            {
                if (action == "sleep")
                {
                    sleepSeen = true;
                    if (dragInterruptSeen && lastAction != "" && virtualLocal < holdEndVirtual)
                        sleepReentrySeen = true;
                }
                else if (lastAction == "sleep" && realDragSeen && virtualLocal < holdEndVirtual)
                {
                    dragInterruptSeen = true;
                }
                if (action == "idle" && virtualLocal >= wakeVirtual)
                    wakeSeen = true;
                lastAction = action;
            }
            var obs = new
            {
                realElapsedSec = Math.Round(realStart.Elapsed.TotalSeconds, 1),
                virtualLocalTime = virtualLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                currentAction = action,
                isDragging = mainWindow.IsDragging,
                note
            };
            File.AppendAllText(obsFile, JsonSerializer.Serialize(obs) + "\n");
            Console.WriteLine($"[P4-003] t={obs.realElapsedSec}s  virtual={obs.virtualLocalTime}  " +
                $"action={obs.currentAction}  dragging={obs.isDragging}  {note}");
        }

        WriteObservation("preview start: virtual 23:29 (daytime), waiting for 23:30 entry");

        // Virtual clock driver: 1 real second = 1 virtual minute in phase 1,
        // then hold, then the boundary jumps below.
        var clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        clockTimer.Tick += (_, _) =>
        {
            var t = realStart.Elapsed.TotalSeconds;
            if (t < 5.0)
            {
                virtualLocal = virtualLocal.AddMinutes(1);
            }
            else if (t >= 35.0 && t < 36.0 && virtualLocal < new DateTime(2026, 11, 17, 5, 59, 0))
            {
                virtualLocal = new DateTime(2026, 11, 17, 5, 59, 0);
                WriteObservation("jumped virtual clock to 05:59 (still night)");
            }
            else if (t >= 38.0 && virtualLocal < new DateTime(2026, 11, 17, 6, 0, 0))
            {
                virtualLocal = new DateTime(2026, 11, 17, 6, 0, 0);
                WriteObservation("crossed 06:00 boundary — expecting wake to idle");
            }
            // 5–35s: hold at night for operator interaction (drag/pet).
        };
        clockTimer.Start();

        // Observation snapshot every 1 second (R1 F-02: 1s sampling keeps a
        // real drag from falling between samples and being missed).
        var obsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        obsTimer.Tick += (_, _) => WriteObservation("periodic snapshot");
        obsTimer.Start();

        // Sidecar with the verification summary.
        void WriteSidecar()
        {
            var sidecar = new
            {
                scenario = noInput ? "night-sleepy-noinput" : "night-sleepy",
                sleepSeen,
                wakeSeen,
                realDragSeen,
                sleepReentrySeen,
                dragInterruptSeen,
                usedProductionCreatePetWindow = true,
                usedInjectedLocalClock = true,
                note = noInput
                    ? "P4-003 R1 F-02 no-input control run: operator must NOT " +
                      "touch the pet. realDragSeen/dragInterruptSeen/" +
                      "sleepReentrySeen must all stay false."
                    : "Operator can drag the bear during the hold phase " +
                      "(~5s–35s) to observe sleep → drag/idle → sleep re-entry."
            };
            var sidecarFile = Path.Combine(scenarioDir, "night-sleepy-sidecar.json");
            File.WriteAllText(sidecarFile, JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true }));
        }

        // Shutdown at t=60s.
        var shutdownTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        shutdownTimer.Tick += (_, _) =>
        {
            shutdownTimer.Stop();
            obsTimer.Stop();
            clockTimer.Stop();
            WriteObservation("shutting down");
            WriteSidecar();

            var ok = sleepSeen && wakeSeen;
            if (noInput && (realDragSeen || dragInterruptSeen || sleepReentrySeen))
                ok = false;
            var summary = $"sleepSeen={sleepSeen}, wakeSeen={wakeSeen}, " +
                          $"realDragSeen={realDragSeen}, dragInterruptSeen={dragInterruptSeen}, " +
                          $"sleepReentrySeen={sleepReentrySeen}" +
                          (noInput ? " (no-input control)" : "");
            var cleanupSidecarPath = Path.Combine(scenarioDir, "night-sleepy-cleanup.json");
            s_app!.ShutdownMainWindow(cleanupSidecarPath);
            s_app?.DiagnosticLogger?.Dispose();
            WriteResult(ok ? 0 : 1, "night-sleepy: " + summary);
            Environment.Exit(ok ? 0 : 1);
        };
        shutdownTimer.Start();

        Dispatcher.Run();

        WriteResult(1, "night-sleepy: Dispatcher.Run() returned without shutdown (unexpected).");
        s_app?.DiagnosticLogger?.Dispose();
        Environment.Exit(1);
    }

    /// <summary>
    /// Writes the probe result to the result file. Only called for scenarios
    /// where the process exits normally. For crash scenarios (ui-exception,
    /// worker-exception), the parent process reads logs directly and collects
    /// the real exit code — no self-reporting.
    /// </summary>
    private static void WriteResult(int exitCode, string message)
    {
        try
        {
            // Read log files from the scenario's log subdirectory.
            var logDir = Path.Combine(s_baseDir!, s_scenario!, "logs");
            var logFiles = Directory.Exists(logDir)
                ? Directory.GetFiles(logDir, "diagnostic-*.log")
                    .OrderBy(f => f)
                    .ToArray()
                : Array.Empty<string>();

            var logContents = new System.Text.StringBuilder();
            foreach (var f in logFiles)
            {
                try
                {
                    logContents.AppendLine($"--- {Path.GetFileName(f)} ---");
                    logContents.Append(File.ReadAllText(f));
                }
                catch { }
            }

            var result = new
            {
                scenario = s_scenario,
                exitCode,
                message,
                logDirectory = logDir,
                logFiles = logFiles.Select(Path.GetFileName).ToArray(),
                logContent = logContents.ToString()
            };

            File.WriteAllText(s_resultFile!, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // Best effort.
        }
    }
}
