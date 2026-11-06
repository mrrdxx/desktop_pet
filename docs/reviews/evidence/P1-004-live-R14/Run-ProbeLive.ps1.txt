param([switch]$BuildOnly, [switch]$Interactive)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$suiteId = [guid]::NewGuid().ToString('N').Substring(0,8)
$suiteRoot = Join-Path $repositoryRoot "artifacts/probe-live/run-$suiteId"
$fixtureApp = Join-Path $suiteRoot 'isolated-app'
$fixtureCore = Join-Path $suiteRoot 'isolated-core'
$dataDirectory = Join-Path $suiteRoot 'data'
New-Item -ItemType Directory -Path $fixtureApp,$fixtureCore,$dataDirectory -Force | Out-Null

# F-11: Driver-side logging function (writes to live.log once dataDirectory is ready).
function ProbeLive-Log([string]$msg) {
    $logPath = Join-Path $dataDirectory 'live.log'
    if (Test-Path -LiteralPath (Split-Path -Parent $logPath)) {
        Add-Content -LiteralPath $logPath -Value "$([DateTimeOffset]::Now.ToString('O')) [DRIVER] $msg"
    }
}

# Copy Core source (exclude bin/obj).
$coreSource = Join-Path $repositoryRoot 'src/DesktopPet.Core'
Get-ChildItem -LiteralPath $coreSource -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
    $target = Join-Path $fixtureCore ($_.FullName.Substring($coreSource.TrimEnd('\').Length + 1))
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
}

# Copy App source.
foreach ($item in @('DesktopPet.App.csproj','app.manifest','App.xaml','App.xaml.cs','MainWindow.xaml','MainWindow.xaml.cs','Platform','Persistence')) {
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
if (-not $startupText.Contains('_archive = new ArchiveService();') -or -not $startupText.Contains('_mainWindow.Show();')) { throw 'Production startup seam changed; inspect fixture first.' }
$startupText = $startupText.Replace('_archive = new ArchiveService();','_archive = new ArchiveService(Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR") ?? throw new InvalidOperationException("Test directory required"));')
# Inject probe live probe call after window show.
$startupText = $startupText.Replace('_mainWindow.Show();',"_mainWindow.Show();`n        _ = RunProbeLiveAsync();")
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
[IO.File]::WriteAllText((Join-Path $dataDirectory 'state.json'),$seedState,[Text.UTF8Encoding]::new($false))

# Build.
dotnet build $project -c Debug -m:1 --ignore-failed-sources --nologo -v minimal
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
}
