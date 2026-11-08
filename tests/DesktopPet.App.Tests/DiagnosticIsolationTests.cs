using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// PREP-001 R2 F-02: Isolation tests verify diagnostic exception wiring through
/// real subprocess exception events, using the PRODUCTION App.RegisterDiagnosticHandlers
/// entry point — NOT by registering handler copies.
///
/// R2 changes:
/// - Probe Main is [STAThread] — fixes "调用线程必须为STA" error.
/// - Probe uses ProbeSetupIsolated() — does NOT execute production OnStartup.
///   Isolated ArchiveService (temp dir), SingleInstanceGuard (unique mutex),
///   DiagnosticLogger (temp dir) — none touch user data or production lock.
/// - UI/worker exceptions keep real default termination — parent collects
///   actual exit code, NOT probe self-reported exit 0.
/// - Normal/abnormal save snapshot verification added (R2 F-02 requirement).
/// - Task scenario: if GC doesn't fire the event, explicitly record as
///   "未验证" (not verified), not "just delayed" — Inconclusive per R2.
/// </summary>
[TestClass]
public class DiagnosticIsolationTests
{
    private static string s_probeExe = string.Empty;
    private static string s_probeProject = string.Empty;
    private string _tempDir = string.Empty;

    [AssemblyInitialize]
    public static void ClassInit(TestContext context)
    {
        // Build the probe project once for all tests in this class.
        var repoRoot = FindRepoRoot();
        s_probeProject = Path.Combine(repoRoot,
            "tests", "DesktopPet.DiagnosticProbe", "DesktopPet.DiagnosticProbe.csproj");

        // Build to a known output directory.
        var buildOutput = Path.Combine(repoRoot, "artifacts", "prep001-probe");
        Directory.CreateDirectory(buildOutput);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{s_probeProject}\" -c Debug -o \"{buildOutput}\" --ignore-failed-sources",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var buildProc = Process.Start(psi)!;
        buildProc.WaitForExit(120000);
        if (buildProc.ExitCode != 0)
        {
            var stdout = buildProc.StandardOutput.ReadToEnd();
            var stderr = buildProc.StandardError.ReadToEnd();
            Assert.Fail($"Probe build failed (exit {buildProc.ExitCode}):\n{stdout}\n{stderr}");
        }

        s_probeExe = Path.Combine(buildOutput, "DesktopPet.DiagnosticProbe.exe");
        Assert.IsTrue(File.Exists(s_probeExe),
            $"Probe executable not found at {s_probeExe}");
    }

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "DesktopPetTests_DiagIso_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [TestMethod]
    [TestCategory("Isolation")]
    public void UiException_ThroughDispatcher_LogsToDiagnosticFile()
    {
        // R2 F-02: UI exception propagates with real default behavior.
        // The process should crash with a non-zero exit code.
        // Probe Main is [STAThread] so WPF Dispatcher works correctly.
        var (exitCode, logContent) = RunProbe("ui-exception");

        Assert.IsTrue(logContent.Contains("source=DispatcherUnhandled"),
            "Log must contain DispatcherUnhandled source. Content:\n" + logContent);
        Assert.IsTrue(logContent.Contains("PREP-001 probe: UI thread exception"),
            "Log must contain the actual exception message. Content:\n" + logContent);
        Assert.IsTrue(logContent.Contains("InvalidOperationException"),
            "Log must contain exception type. Content:\n" + logContent);
        // R2 F-02: Verify real non-zero exit code.
        Assert.AreNotEqual(0, exitCode,
            "UI unhandled exception should cause non-zero exit code (real default behavior, not self-reported 0).");
    }

    [TestMethod]
    [TestCategory("Isolation")]
    public void WorkerException_ThroughAppDomain_LogsToDiagnosticFile()
    {
        // R2 F-02: Worker thread exception triggers AppDomain.UnhandledException
        // which is typically terminating. The process should exit with non-zero code.
        var (exitCode, logContent) = RunProbe("worker-exception");

        Assert.IsTrue(logContent.Contains("source=AppDomainUnhandled"),
            "Log must contain AppDomainUnhandled source. Content:\n" + logContent);
        Assert.IsTrue(logContent.Contains("PREP-001 probe: worker thread exception"),
            "Log must contain the actual exception message.");
        Assert.IsTrue(logContent.Contains("terminating=True"),
            "AppDomain unhandled exception should be marked as terminating.");

        // R2 F-02: Verify real non-zero exit code.
        Assert.AreNotEqual(0, exitCode,
            "Worker thread unhandled exception should cause non-zero exit code (real default behavior).");
    }

    [TestMethod]
    [TestCategory("Isolation")]
    public void TaskUnobserved_ThroughTaskScheduler_LogsToDiagnosticFile()
    {
        // R2 F-02: Task unobserved exception is GC-dependent. The event may
        // fire during GC.Collect or later. Per ARCHITECTURE §10:
        // "Task通知不等于立即或必然终止"
        //
        // R2 F-02: If the event did not fire, we must record this as
        // "未验证" (not verified), NOT infer "just delayed and wiring is fine".
        var (exitCode, logContent) = RunProbe("task-unobserved");

        if (logContent.Contains("source=TaskUnobserved"))
        {
            Assert.IsTrue(logContent.Contains("PREP-001 probe: unobserved task exception"),
                "Log must contain the actual exception message.");
            Assert.IsTrue(logContent.Contains("type: System.InvalidOperationException"),
                "Log must contain exception type with 'type:' format (not 'type=').");
        }
        else
        {
            // R2 F-02: Task notification wiring is 未验证 (not verified) —
            // we cannot infer the wiring is correct just because GC didn't
            // fire the event. This is a known GC-dependent limitation per
            // ARCHITECTURE §10, recorded as Inconclusive.
            Assert.Inconclusive(
                "TaskScheduler.UnobservedTaskException did not fire within the GC window. " +
                "R2 F-02: Task notification wiring is 未验证 (not verified) — " +
                "cannot infer wiring is correct from GC timing alone. " +
                "The handler is registered via production RegisterDiagnosticHandlers, " +
                "but the event did not fire in this run.");
        }
    }

    [TestMethod]
    [TestCategory("Isolation")]
    public void NormalExit_NoFalsePositiveDiagnosticLogs()
    {
        var (exitCode, logContent) = RunProbe("normal-exit");

        Assert.AreEqual(0, exitCode, "Normal exit should return 0.");

        // Verify no exception-source entries in the log.
        Assert.IsFalse(logContent.Contains("source=DispatcherUnhandled"),
            "Normal exit should not produce DispatcherUnhandled entries.");
        Assert.IsFalse(logContent.Contains("source=AppDomainUnhandled"),
            "Normal exit should not produce AppDomainUnhandled entries.");
        Assert.IsFalse(logContent.Contains("source=TaskUnobserved"),
            "Normal exit should not produce TaskUnobserved entries.");
    }

    /// <summary>
    /// R3 F-02: Normal save verification — pre-seed old state, change in-memory
    /// state to distinguishable data, save via production FlushFinalState path.
    /// Verify state.json was updated with the new state (not the old marker).
    /// </summary>
    [TestMethod]
    [TestCategory("Isolation")]
    public void NormalSave_IsolatedArchive_WritesStateFile()
    {
        var (exitCode, resultJson) = RunProbeWithResult("normal-save");

        Assert.AreEqual(0, exitCode, "Normal save scenario should exit 0.");

        var result = JsonDocument.Parse(resultJson);
        var message = result.RootElement.GetProperty("message").GetString() ?? "";
        var saveResult = JsonDocument.Parse(message);

        Assert.IsTrue(saveResult.RootElement.GetProperty("stateFileExists").GetBoolean(),
            "Normal save should produce a state file. Result: " + message);

        var content = saveResult.RootElement.GetProperty("stateFileContent").GetString() ?? "";
        Assert.IsTrue(content.Length > 0,
            "State file should not be empty after normal save.");

        // R3: Verify the new state was written — content must contain the
        // new marker, NOT the old marker.
        var oldMarker = saveResult.RootElement.GetProperty("oldMarker").GetString() ?? "";
        Assert.IsFalse(content.Contains(oldMarker),
            "State file should NOT contain the old pre-seeded marker after normal save. " +
            "Old marker: " + oldMarker + " Content: " + content);
        // Note: ArchiveService.SaveState overwrites LastSavedUtc with current
        // UTC time, so we verify via WindowLeft/WindowTop which are preserved.
        Assert.IsTrue(content.Contains("777"),
            "State file should contain the new WindowLeft value (777). Content: " + content);
        Assert.IsTrue(content.Contains("888"),
            "State file should contain the new WindowTop value (888). Content: " + content);
        Assert.IsFalse(content.Contains("111"),
            "State file should NOT contain the old WindowLeft value (111). Content: " + content);

        // R3: Confirm the save went through the production exit path.
        Assert.IsTrue(saveResult.RootElement.GetProperty("savedViaProductionPath").GetBoolean(),
            "Normal save should be via the production FlushFinalState path. Result: " + message);
    }

    /// <summary>
    /// R3 F-02: Abnormal save verification — pre-seed old state, change
    /// in-memory state, KEEP save coordinator active, trigger real UI
    /// exception. Parent process compares state.json before and after the
    /// crash to confirm the exception path does NOT force-save unknown state.
    /// The process should crash with a non-zero exit code (not self-report 0).
    /// </summary>
    [TestMethod]
    [TestCategory("Isolation")]
    public void AbnormalSave_RealException_CoordinatorActive_DoesNotForceSave()
    {
        // R3: Read the state.json BEFORE launching the probe — it doesn't
        // exist yet, so pre-crash content should be empty/none.
        // The probe writes a sidecar file with the pre-crash content and
        // old marker before triggering the exception.

        // Use RunProbe (not RunProbeWithResult) because the probe crashes
        // and won't write a result file.
        var (exitCode, logContent) = RunProbe("abnormal-save");

        // R3: The process should crash with a non-zero exit code.
        Assert.AreNotEqual(0, exitCode,
            "Abnormal save: process should crash with non-zero exit code (real UI exception, not self-reported 0).");

        // R3: Verify the exception was logged.
        Assert.IsTrue(logContent.Contains("source=DispatcherUnhandled"),
            "Log must contain DispatcherUnhandled source for the abnormal-save exception. Content:\n" + logContent);
        Assert.IsTrue(logContent.Contains("PREP-001 R3 probe: abnormal save"),
            "Log must contain the R3 abnormal-save exception message. Content:\n" + logContent);

        // R3: Read the sidecar file that the probe wrote before crashing.
        var sidecarFile = Path.Combine(_tempDir, "abnormal-save", "abnormal-pre-crash.json");
        Assert.IsTrue(File.Exists(sidecarFile),
            "Sidecar file should exist (probe writes it before triggering the exception).");

        var sidecarJson = File.ReadAllText(sidecarFile);
        var sidecar = JsonDocument.Parse(sidecarJson);

        var preCrashContent = sidecar.RootElement.GetProperty("preCrashContent").GetString() ?? "";
        var oldMarker = sidecar.RootElement.GetProperty("oldMarker").GetString() ?? "";
        var coordinatorActive = sidecar.RootElement.GetProperty("coordinatorActive").GetBoolean();

        // R3: Verify the save coordinator was ACTIVE when the exception occurred.
        Assert.IsTrue(coordinatorActive,
            "Save coordinator must be ACTIVE (not null/disposed) when the exception is triggered. " +
            "We must NOT pre-null the coordinator to prove 'no save'.");

        // R3: Read the state.json AFTER the crash.
        var stateFile = Path.Combine(_tempDir, "abnormal-save", "state.json");
        var postCrashContent = File.Exists(stateFile) ? File.ReadAllText(stateFile) : "";

        // R3: The pre-seeded content should contain the old marker.
        Assert.IsTrue(preCrashContent.Contains(oldMarker),
            "Pre-crash state.json should contain the old pre-seeded marker. " +
            "Old marker: " + oldMarker + " Pre-crash content: " + preCrashContent);

        // R3: Core assertion — after the crash, the state.json should still
        // contain the old marker (unchanged), NOT the new abnormal-save data.
        // This proves the exception path did NOT force-save the unknown
        // in-memory state through OnExit/FlushFinalState.
        // Note: ArchiveService.SaveState overwrites LastSavedUtc, so we
        // detect forced save via WindowLeft (999) / WindowTop (666) values.
        Assert.IsFalse(postCrashContent.Contains("999"),
            "After crash, state.json must NOT contain WindowLeft 999 — " +
            "the exception path must NOT force-save unknown in-memory state. " +
            "Post-crash content: " + postCrashContent);
        Assert.IsFalse(postCrashContent.Contains("666"),
            "After crash, state.json must NOT contain WindowTop 666 — " +
            "the exception path must NOT force-save unknown in-memory state. " +
            "Post-crash content: " + postCrashContent);
        Assert.IsTrue(postCrashContent.Contains(oldMarker),
            "After crash, state.json should still contain the old pre-seeded marker (unchanged). " +
            "Old marker: " + oldMarker + " Post-crash content: " + postCrashContent);
    }

    /// <summary>
    /// R3 F-02: Isolation verification — after ProbeSetupIsolated and
    /// Dispatcher pumping, verify archive path, log directory, and mutex
    /// name still point to isolated temp dir, NOT production defaults.
    /// Also verify no MainWindow was created.
    /// </summary>
    [TestMethod]
    [TestCategory("Isolation")]
    public void IsolationVerify_AfterDispatcherPump_DependenciesStayIsolated()
    {
        var (exitCode, resultJson) = RunProbeWithResult("isolation-verify");

        Assert.AreEqual(0, exitCode, "Isolation verify scenario should exit 0.");

        var result = JsonDocument.Parse(resultJson);
        var message = result.RootElement.GetProperty("message").GetString() ?? "";
        var isoResult = JsonDocument.Parse(message);

        // R3: Dispatcher was actually pumped.
        Assert.IsTrue(isoResult.RootElement.GetProperty("dispatcherPumped").GetBoolean(),
            "Dispatcher should have been pumped (no-op action completed). Result: " + message);

        // R3: Archive path points to isolated temp dir, NOT production default.
        Assert.IsTrue(isoResult.RootElement.GetProperty("archiveIsolated").GetBoolean(),
            "Archive path should point to isolated temp dir. Result: " + message);
        Assert.IsTrue(isoResult.RootElement.GetProperty("archiveNotProduction").GetBoolean(),
            "Archive path should NOT be the production default path. Result: " + message);

        // R3: Log directory points to isolated temp dir, NOT production default.
        Assert.IsTrue(isoResult.RootElement.GetProperty("logIsolated").GetBoolean(),
            "Log directory should point to isolated temp dir. Result: " + message);
        Assert.IsTrue(isoResult.RootElement.GetProperty("logNotProduction").GetBoolean(),
            "Log directory should NOT be the production default path. Result: " + message);

        // R3: Instance guard uses isolated mutex name, NOT production default.
        Assert.IsTrue(isoResult.RootElement.GetProperty("mutexNotProduction").GetBoolean(),
            "Mutex name should NOT be the production default. Result: " + message);
        Assert.IsTrue(isoResult.RootElement.GetProperty("guardNotNull").GetBoolean(),
            "Instance guard should not be null after Dispatcher pumping. Result: " + message);

        // R3: No MainWindow was created (OnStartup was skipped).
        Assert.IsFalse(isoResult.RootElement.GetProperty("hasMainWindow").GetBoolean(),
            "No MainWindow should be created in isolated mode. Result: " + message);

        // R3: Probe isolated mode flag is set.
        Assert.IsTrue(isoResult.RootElement.GetProperty("probeIsolatedMode").GetBoolean(),
            "ProbeIsolatedMode flag should be true. Result: " + message);
    }

    /// <summary>
    /// R2 F-02: Runs the probe and collects the REAL exit code and log content.
    /// The probe now takes a baseDir (not logDir) and creates per-scenario
    /// subdirectories with isolated log/archive/mutex.
    /// </summary>
    private (int exitCode, string logContent) RunProbe(string scenario)
    {
        var resultFile = Path.Combine(_tempDir, $"{scenario}-result.json");

        var psi = new ProcessStartInfo
        {
            FileName = s_probeExe,
            Arguments = $"\"{_tempDir}\" \"{scenario}\" \"{resultFile}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var proc = Process.Start(psi)!;

        // Read stdout/stderr asynchronously to prevent pipe buffer deadlock.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var timeout = scenario switch
        {
            "task-unobserved" => 30000,
            "ui-exception" => 30000,
            "worker-exception" => 15000,
            _ => 15000
        };
        if (!proc.WaitForExit(timeout))
        {
            try { proc.Kill(); } catch { }
            Assert.Fail($"Probe timed out after {timeout}ms for scenario: {scenario}");
        }

        stdoutTask.Wait(5000);
        stderrTask.Wait(5000);

        var realExitCode = proc.ExitCode;

        // Wait briefly for file system to flush.
        System.Threading.Thread.Sleep(300);

        // R2 F-02: Read log content from the scenario's log subdirectory.
        var logDir = Path.Combine(_tempDir, scenario, "logs");
        var logContent = ReadLogContent(logDir);

        return (realExitCode, logContent);
    }

    /// <summary>
    /// R2 F-02: Runs the probe and returns the exit code plus the raw
    /// result file content (JSON). Used for save-verification scenarios
    /// that embed structured data in the result file.
    /// </summary>
    private (int exitCode, string resultJson) RunProbeWithResult(string scenario)
    {
        var resultFile = Path.Combine(_tempDir, $"{scenario}-result.json");

        var psi = new ProcessStartInfo
        {
            FileName = s_probeExe,
            Arguments = $"\"{_tempDir}\" \"{scenario}\" \"{resultFile}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var proc = Process.Start(psi)!;

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(15000))
        {
            try { proc.Kill(); } catch { }
            Assert.Fail($"Probe timed out for scenario: {scenario}");
        }

        stdoutTask.Wait(5000);
        stderrTask.Wait(5000);

        var realExitCode = proc.ExitCode;

        System.Threading.Thread.Sleep(300);

        var resultJson = File.Exists(resultFile) ? File.ReadAllText(resultFile) : "{}";

        return (realExitCode, resultJson);
    }

    /// <summary>
    /// Reads all diagnostic log files from the directory and returns
    /// their concatenated content.
    /// </summary>
    private static string ReadLogContent(string logDir)
    {
        if (!Directory.Exists(logDir))
            return string.Empty;

        var logFiles = Directory.GetFiles(logDir, "diagnostic-*.log")
            .OrderBy(f => f)
            .ToArray();

        var sb = new System.Text.StringBuilder();
        foreach (var f in logFiles)
        {
            try
            {
                sb.AppendLine($"--- {Path.GetFileName(f)} ---");
                sb.Append(File.ReadAllText(f));
            }
            catch { }
        }
        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, ".git"))
            && !File.Exists(Path.Combine(dir, "DesktopPet.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? AppDomain.CurrentDomain.BaseDirectory;
    }
}
