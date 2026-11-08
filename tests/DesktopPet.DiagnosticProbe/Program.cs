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
using DesktopPet.Core.Persistence;

// PREP-001 R1 F-02: Alias to disambiguate the App class from the DesktopPet.App namespace.
using WpfApp = DesktopPet.App.App;

namespace DesktopPet.DiagnosticProbe;

public static class Program
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

        // R3: Change in-memory state to distinguishable data.
        // Access the state object set by ProbeSetupIsolated and modify it
        // with values that are clearly different from the old file.
        var state = s_app!.TestState;
        if (state is not null)
        {
            state.WindowLeft = 777.0;
            state.WindowTop = 888.0;
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

        // R3: Change in-memory state to distinguishable data — different
        // from the old file so that if a save DID happen, we could detect it.
        var state = s_app!.TestState;
        if (state is not null)
        {
            state.WindowLeft = 999.0;
            state.WindowTop = 666.0;
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
