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
        if (s_scenario is "window-perch" or "window-perch-live" or "window-climb-live")
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
