# PREP-003 性能基线隔离启动脚本
# 创建隔离副本（不注入 ProbeLiveProbe），用 Release 配置，设置隔离存档
# 用法：.\Run-PerfIsolated.ps1 -Configuration Release
param(
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$suiteId = [guid]::NewGuid().ToString('N').Substring(0,8)
$suiteRoot = Join-Path $repositoryRoot "artifacts/perf-baseline/run-$suiteId"
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

# Copy App source (including Diagnostics).
foreach ($item in @('DesktopPet.App.csproj','app.manifest','App.xaml','App.xaml.cs','MainWindow.xaml','MainWindow.xaml.cs','Platform','Persistence','Diagnostics')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "src/DesktopPet.App/$item") -Destination $fixtureApp -Recurse
}

# Fix project references for isolated copy.
$project = Join-Path $fixtureApp 'DesktopPet.App.csproj'
$projectText = (Get-Content -LiteralPath $project -Raw).Replace('..\DesktopPet.Core\DesktopPet.Core.csproj',(Join-Path $fixtureCore 'DesktopPet.Core.csproj'))
$projectText = $projectText.Replace('..\..\assets\pets\**\*',(Join-Path $repositoryRoot 'assets\pets\**\*'))
[IO.File]::WriteAllText($project,$projectText,[Text.UTF8Encoding]::new($false))

# Patch App.xaml.cs: use isolated archive directory via env var.
# Do NOT inject ProbeLiveProbe — we want a clean run for performance measurement.
$startup = Join-Path $fixtureApp 'App.xaml.cs'
$startupText = Get-Content -LiteralPath $startup -Raw
if (-not $startupText.Contains('_archive = new ArchiveService();') -or -not $startupText.Contains('_mainWindow.Show();')) { throw 'Production startup seam changed; inspect fixture first.' }
$startupText = $startupText.Replace('_archive = new ArchiveService();','_archive = new ArchiveService(Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR") ?? throw new InvalidOperationException("Test directory required"));')
# Do NOT inject RunProbeLiveAsync — clean run for perf.
[IO.File]::WriteAllText($startup,$startupText,[Text.UTF8Encoding]::new($false))

# Patch SingleInstanceGuard mutex name to avoid collision.
$mutexFile = Join-Path $fixtureApp 'Platform/SingleInstanceGuard.cs'
$mutexText = Get-Content -LiteralPath $mutexFile -Raw
if (-not $mutexText.Contains('Global\DesktopPet.App.SingleInstance')) { throw 'Mutex seam changed; inspect fixture first.' }
[IO.File]::WriteAllText($mutexFile,$mutexText.Replace('Global\DesktopPet.App.SingleInstance',"Local\DesktopPet.Perf.$suiteId"),[Text.UTF8Encoding]::new($false))

# Pre-seed isolated state.json with consumed=true (no probe trigger during idle).
$today = (Get-Date).ToString('yyyy-MM-dd')
$seedState = @{
    schemaVersion = 3
    accumulatedRuntimeMinutes = 150.0
    rareEventPlan = @{
        localDate = $today
        triggerDelayMinutes = 20
        triggerRoll = 0.0
        consumed = $true
        accumulatedBaselineMinutes = 0.0
    }
    grievance = 50
    isPaused = $false
} | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $dataDirectory 'state.json'),$seedState,[Text.UTF8Encoding]::new($false))

# Build.
dotnet build $project -c $Configuration -m:1 --ignore-failed-sources --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Isolated fixture build failed.' }

# Output the suite root and exe path for the caller.
$suiteRoot | Out-File -FilePath (Join-Path $repositoryRoot 'artifacts/perf-baseline/latest-run.txt') -Encoding UTF8
Write-Host "SUITE_ROOT=$suiteRoot"
Write-Host "DATA_DIR=$dataDirectory"
Write-Host "EXE=dotnet $(Join-Path $fixtureApp "bin/$Configuration/net10.0-windows/DesktopPet.dll")"
Write-Host "ENV_VAR=DESKTOPPET_VALIDATION_DIR=$dataDirectory"
