param([switch]$BuildOnly, [switch]$Interactive, [switch]$Restart, [switch]$Chase, [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# F-11: Driver-side logging function (writes to live.log once dataDirectory is ready).
function ProbeLive-Log([string]$msg) {
    $logPath = Join-Path $dataDirectory 'live.log'
    if (Test-Path -LiteralPath (Split-Path -Parent $logPath)) {
        Add-Content -LiteralPath $logPath -Value "$([DateTimeOffset]::Now.ToString('O')) [DRIVER] $msg"
    }
}

# R11 F-08: -Restart reuses the latest run's isolated copy and data directory.
# It does NOT rewrite the seed state.json, does NOT generate a new suiteId,
# and does NOT rebuild — it launches the already-built isolated app with the
# existing data directory. This allows verifying save/remove persistence
# across restarts with the same isolated archive.
if ($Restart) {
    $latestFile = Join-Path $repositoryRoot 'artifacts/probe-live/latest-run.txt'
    if (-not (Test-Path -LiteralPath $latestFile)) {
        throw 'Cannot restart: no previous run found (artifacts/probe-live/latest-run.txt missing). Run without -Restart first.'
    }
    $suiteRoot = (Get-Content -LiteralPath $latestFile -Raw).Trim()
    if (-not (Test-Path -LiteralPath $suiteRoot)) {
        throw "Cannot restart: previous run directory does not exist: $suiteRoot"
    }
    $fixtureApp = Join-Path $suiteRoot 'isolated-app'
    $dataDirectory = Join-Path $suiteRoot 'data'
    if (-not (Test-Path -LiteralPath $dataDirectory)) {
        throw "Cannot restart: data directory does not exist: $dataDirectory"
    }
    ProbeLive-Log "RESTART using existing suiteRoot=$suiteRoot dataDir=$dataDirectory"

    # Launch the already-built isolated app with the existing data directory.
    $oldDirectory = $env:DESKTOPPET_VALIDATION_DIR
    $oldProbeMode = $env:DESKTOPPET_PROBE_MODE
    try {
        $env:DESKTOPPET_VALIDATION_DIR = $dataDirectory
        $env:DESKTOPPET_PROBE_MODE = 'interactive'
        $exe = Join-Path $fixtureApp 'bin/Debug/net10.0-windows/DesktopPet.exe'
        if (-not (Test-Path -LiteralPath $exe)) {
            throw "Cannot restart: built exe not found at $exe. Run without -Restart first to build."
        }
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exe
        $start.UseShellExecute = $false
        $start.WindowStyle = 'Hidden'
        $process = [Diagnostics.Process]::Start($start)
        ProbeLive-Log "RESTART_PROCESS_STARTED pid=$($process.Id)"

        if (-not $process.WaitForExit(600000)) {
            ProbeLive-Log "RESTART_TIMEOUT pid=$($process.Id) exceeded 600000ms; killing."
            try { $process.Kill() } catch { ProbeLive-Log "KILL_FAILED $($_.Exception.Message)" }
            $process.WaitForExit()
            "RESTART_PROCESS_EXIT_CODE=$($process.ExitCode)"
            "RESTART_EXIT_MODE=timeout"
        } else {
            $process.WaitForExit()
            "RESTART_PROCESS_EXIT_CODE=$($process.ExitCode)"
            "RESTART_EXIT_MODE=auto (process exited on its own)"
        }
        if (Test-Path -LiteralPath (Join-Path $dataDirectory 'live.log')) {
            Get-Content -LiteralPath (Join-Path $dataDirectory 'live.log')
        }
    } finally {
        $env:DESKTOPPET_VALIDATION_DIR = $oldDirectory
        $env:DESKTOPPET_PROBE_MODE = $oldProbeMode
    }
    "RESTART_COMPLETE; reused $suiteRoot"
    exit 0
}

$suiteId = [guid]::NewGuid().ToString('N').Substring(0,8)
$suiteRoot = Join-Path $repositoryRoot "artifacts/probe-live/run-$suiteId"
$fixtureApp = Join-Path $suiteRoot 'isolated-app'
$fixtureCore = Join-Path $suiteRoot 'isolated-core'
$dataDirectory = Join-Path $suiteRoot 'data'
New-Item -ItemType Directory -Path $fixtureApp,$fixtureCore,$dataDirectory -Force | Out-Null

# Copy Core source (exclude bin/obj).
$coreSource = Join-Path $repositoryRoot 'src/DesktopPet.Core'
Get-ChildItem -LiteralPath $coreSource -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
    $target = Join-Path $fixtureCore ($_.FullName.Substring($coreSource.TrimEnd('\').Length + 1))
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
}

# Copy App source.
foreach ($item in @('DesktopPet.App.csproj','app.manifest','App.xaml','App.xaml.cs','MainWindow.xaml','MainWindow.xaml.cs','Platform','Persistence','Diagnostics')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "src/DesktopPet.App/$item") -Destination $fixtureApp -Recurse
}

# Fix project references for isolated copy.
$project = Join-Path $fixtureApp 'DesktopPet.App.csproj'
$projectText = (Get-Content -LiteralPath $project -Raw).Replace('..\DesktopPet.Core\DesktopPet.Core.csproj',(Join-Path $fixtureCore 'DesktopPet.Core.csproj'))
$projectText = $projectText.Replace('..\..\assets\pets\**\*',(Join-Path $repositoryRoot 'assets\pets\**\*'))
[IO.File]::WriteAllText($project,$projectText,[Text.UTF8Encoding]::new($false))

# Patch App.xaml.cs: use isolated archive directory via env var.
$startup = Join-Path $fixtureApp 'App.xaml.cs'
$startupText = Get-Content -LiteralPath $startup -Raw
if (-not $startupText.Contains('_archive = new ArchiveService();') -or -not $startupText.Contains('w.Show();')) { throw 'Production startup seam changed; inspect fixture first.' }
$startupText = $startupText.Replace('_archive = new ArchiveService();','_archive = new ArchiveService(Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR") ?? throw new InvalidOperationException("Test directory required"));')
# R11 F-08: Only inject RunProbeLiveAsync in the startup foreach (the first
# occurrence of 'w.Show();'), NOT in AddPet's 'w.Show();'. The global Replace
# was injecting the probe at every window show, causing re-evaluation of
# consumed rare-event plans on each AddPet. In Interactive mode, skip probe
# injection entirely for a clean interaction experience.
if (-not $Interactive) {
    # Replace only the FIRST occurrence (startup foreach), not AddPet's.
    $startupText = [Text.RegularExpressions.Regex]::new('w\.Show\(\);').Replace($startupText, "w.Show();`n            _ = RunProbeLiveAsync(); // F-11: injected by Run-ProbeLive.ps1", 1)
}
[IO.File]::WriteAllText($startup,$startupText,[Text.UTF8Encoding]::new($false))

# Patch SingleInstanceGuard mutex name to avoid collision.
$mutexFile = Join-Path $fixtureApp 'Platform/SingleInstanceGuard.cs'
$mutexText = Get-Content -LiteralPath $mutexFile -Raw
if (-not $mutexText.Contains('Global\DesktopPet.App.SingleInstance')) { throw 'Mutex seam changed; inspect fixture first.' }
[IO.File]::WriteAllText($mutexFile,$mutexText.Replace('Global\DesktopPet.App.SingleInstance',"Local\DesktopPet.ProbeLive.$suiteId"),[Text.UTF8Encoding]::new($false))

# Copy probe source.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ProbeLiveProbe.cs') -Destination (Join-Path $fixtureApp 'ProbeLiveProbe.cs')

# Record latest run.
Set-Content -LiteralPath (Join-Path $repositoryRoot 'artifacts/probe-live/latest-run.txt') -Value $suiteRoot

# Pre-seed isolated state.json with a fireable rare-event plan.
# accumulatedRuntimeMinutes=150 exceeds any delay (20-120).
# triggerDelayMinutes=20 (minimum), triggerRoll=0.0 (always passes 0.5 probability).
# consumed=false — the plan has never been evaluated.
# The plan date is today so EnsurePlanForDate will not create a new one.
$today = (Get-Date).ToString('yyyy-MM-dd')

# P3-004: -Chase seeds TWO pets on the same bottom edge so the user can
# observe chase behavior in -Interactive mode. Both pets are Proactive
# with grievance=50, placed 200 DIP apart (center distance within
# ChaseDistanceMinDip..ChaseDistanceMaxDip = 120..300).
# P3-004 R1 F-04: personality must be a number (0=Proactive, 1=SlowPaced)
# because System.Text.Json without JsonStringEnumConverter serializes
# enums as numbers. The string "Proactive" fails to deserialize.
# P3-004 R1 F-04: windowTop is computed from the real work-area bottom
# minus the pet window height (120 DIP), not a fixed value.
if ($Chase) {
    # F-04: Compute the real work-area bottom in DIP using Win32 API.
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WorkAreaHelper {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern IntPtr GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO {
        public int cbSize; public RECT rcMonitor; public RECT rcWork;
        public uint dwFlags;
    }
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
    public static double GetWorkAreaBottomDip() {
        const uint MONITOR_DEFAULTTOPRIMARY = 1;
        var hMon = MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMon, ref mi);
        uint dpiX, dpiY;
        GetDpiForMonitor(hMon, 0, out dpiX, out dpiY);
        if (dpiY == 0) dpiY = 96;
        return mi.rcWork.Bottom / (dpiY / 96.0);
    }
}
"@ -ErrorAction SilentlyContinue
    $waBottomDip = [WorkAreaHelper]::GetWorkAreaBottomDip()
    $petHeight = 120
    $bottomTop = [Math]::Round($waBottomDip - $petHeight, 0)
    ProbeLive-Log "CHASE_MODE workAreaBottomDip=$waBottomDip petHeight=$petHeight bottomTop=$bottomTop"

    $seedState = @{
        schemaVersion = 4
        accumulatedRuntimeMinutes = 150.0
        rareEventPlan = @{
            localDate = $today
            triggerDelayMinutes = 20
            triggerRoll = 0.0
            consumed = $false
            accumulatedBaselineMinutes = 0.0
        }
        isPaused = $false
        pets = @(
            # F-04: personality=0 (Proactive) as number, not string.
            # F-04: windowTop computed from real work-area bottom.
            @{ petId = 'bear-1'; personality = 0; windowLeft = 200; windowTop = $bottomTop; grievance = 50 }
            @{ petId = 'bear-2'; personality = 0; windowLeft = 400; windowTop = $bottomTop; grievance = 50 }
        )
    } | ConvertTo-Json -Depth 5
    ProbeLive-Log "CHASE_MODE seedState has 2 pets on bottom edge (top=$bottomTop, left=200/400)"
} else {
    $seedState = @{
        schemaVersion = 3
        accumulatedRuntimeMinutes = 150.0
        rareEventPlan = @{
            localDate = $today
            triggerDelayMinutes = 20
            triggerRoll = 0.0
            consumed = $false
            accumulatedBaselineMinutes = 0.0
        }
        grievance = 50
        isPaused = $false
    } | ConvertTo-Json -Depth 5
}
[IO.File]::WriteAllText((Join-Path $dataDirectory 'state.json'),$seedState,[Text.UTF8Encoding]::new($false))

# Build.
dotnet build $project -c $Configuration -m:1 --ignore-failed-sources --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Isolated fixture build failed.' }
if ($BuildOnly) { "BUILD_ONLY_COMPLETE=$suiteRoot"; exit 0 }

# Snapshot real archive before run.
$realArchive = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopPet'
function Archive-Snapshot {
    if (-not (Test-Path -LiteralPath $realArchive)) { return 'ABSENT' }
    return (@(Get-ChildItem -LiteralPath $realArchive -File | Sort-Object Name | ForEach-Object { $_.Name+':'+(Get-FileHash -LiteralPath $_.FullName).Hash }) -join '|')
}
$before = Archive-Snapshot
ProbeLive-Log "REAL_ARCHIVE_BEFORE=$before"

# F-11: Determine timeout and probe mode.
if ($Interactive) {
    $timeoutMs = 600000   # 10 minutes for manual interaction
    $probeMode = 'interactive'
} else {
    $timeoutMs = 90000    # 90 seconds for auto probe sequence
    $probeMode = 'auto'
}

# F-11 R13: Initialize per-run state variables before process start.
$script:_TimeoutOccurred = $false
$script:_ProcessExitCode = $null
$script:_LogPath = Join-Path $dataDirectory 'live.log'

$oldDirectory = $env:DESKTOPPET_VALIDATION_DIR
$oldProbeMode = $env:DESKTOPPET_PROBE_MODE
try {
    $env:DESKTOPPET_VALIDATION_DIR = $dataDirectory
    $env:DESKTOPPET_PROBE_MODE = $probeMode
    $exe = Join-Path $fixtureApp 'bin/Debug/net10.0-windows/DesktopPet.exe'
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $exe
    $start.UseShellExecute = $false
    $start.WindowStyle = 'Hidden'
    $process = [Diagnostics.Process]::Start($start)
    ProbeLive-Log "PROCESS_STARTED pid=$($process.Id) probeMode=$probeMode timeoutMs=$timeoutMs"

    if (-not $process.WaitForExit($timeoutMs)) {
        # F-11: Timeout — record and kill, do NOT throw (finally must run archive comparison).
        ProbeLive-Log "TIMEOUT pid=$($process.Id) exceeded ${timeoutMs}ms; killing process. No unknown process killed."
        try { $process.Kill() } catch { ProbeLive-Log "KILL_FAILED $($_.Exception.Message)" }
        $process.WaitForExit()
        $script:_TimeoutOccurred = $true
        $script:_ProcessExitCode = $process.ExitCode
        if (Test-Path -LiteralPath $script:_LogPath) { Get-Content -LiteralPath $script:_LogPath }
        "PROCESS_EXIT_CODE=$($process.ExitCode)"
        "EXIT_MODE=timeout (external kill after ${timeoutMs}ms)"
    } else {
        $process.WaitForExit()
        $script:_ProcessExitCode = $process.ExitCode
        if (Test-Path -LiteralPath $script:_LogPath) { Get-Content -LiteralPath $script:_LogPath }
        "PROCESS_EXIT_CODE=$($process.ExitCode)"
        "EXIT_MODE=auto (process exited on its own)"
    }
} finally {
    $env:DESKTOPPET_VALIDATION_DIR = $oldDirectory
    $env:DESKTOPPET_PROBE_MODE = $oldProbeMode

    # F-11: Always perform archive snapshot comparison, even on timeout/exception.
    $after = Archive-Snapshot
    ProbeLive-Log "REAL_ARCHIVE_AFTER=$after"
    if ($before -ne $after) {
        "FAIL real archive changed: before=$before after=$after"
        throw 'Real archive changed; investigate before claiming isolation.'
    }
    'PASS original user archive snapshot unchanged'
}

# F-11 R13: Final PASS judgment for auto mode — must satisfy ALL conditions:
#   1. No timeout occurred
#   2. Process exit code is 0
#   3. Log has zero FAIL/ERROR lines
#   4. All required markers present
#   5. RESULT failures=0 present in log (explicit completion marker)
# Interactive mode is manual — no auto PASS judgment.
if (-not $Interactive) {
    $judgmentErrors = @()

    # Condition 1: No timeout.
    if ($script:_TimeoutOccurred) {
        $judgmentErrors += 'FAIL timeout occurred (process was killed after timeout)'
    }

    # Condition 2: Process exit code is 0.
    if ($script:_ProcessExitCode -ne 0) {
        $judgmentErrors += "FAIL process exit code is $($script:_ProcessExitCode) (expected 0)"
    }

    # Condition 3: Log exists and has zero FAIL/ERROR lines.
    if (-not (Test-Path -LiteralPath $script:_LogPath)) {
        $judgmentErrors += 'FAIL live.log not found after run'
    } else {
        $failCount = @(Select-String -LiteralPath $script:_LogPath -Pattern ' FAIL | ERROR ').Count
        if ($failCount -gt 0) {
            $judgmentErrors += "FAIL log contains $failCount FAIL/ERROR line(s)"
        }

        # Condition 4: All required markers present.
        $requiredMarkers = @('BASELINE', 'PROBE_ACTIVE', 'PROBE_ENDED', 'MONITOR', 'HWND_DPI', 'ARCHIVE_SNAPSHOT_AFTER')
        foreach ($marker in $requiredMarkers) {
            if (-not (Select-String -LiteralPath $script:_LogPath -Pattern $marker -Quiet)) {
                $judgmentErrors += "FAIL missing required log marker: $marker"
            }
        }

        # Condition 5: RESULT failures=0 present (explicit completion by probe).
        if (-not (Select-String -LiteralPath $script:_LogPath -Pattern 'RESULT failures=0' -Quiet)) {
            $judgmentErrors += 'FAIL missing RESULT failures=0 completion marker'
        }
    }

    if ($judgmentErrors.Count -gt 0) {
        foreach ($e in $judgmentErrors) { $e }
        throw "Live validation FAILED with $($judgmentErrors.Count) error(s); retain all evidence."
    }
    "ALL PROBE LIVE CHECKS PASS; $suiteRoot"
} else {
    "INTERACTIVE_MODE_COMPLETE; window was kept open for manual testing; $suiteRoot"
    if ($Chase) {
        "CHASE_MODE: Two pets seeded on bottom edge (left=200/400, top=700). Chase invite probability=0.20 (default). Observe occasional chase behavior."
    }
}
