param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$suiteId = [guid]::NewGuid().ToString('N').Substring(0,8)
$suiteRoot = Join-Path $repositoryRoot "artifacts/emotion-live/run-$suiteId"
$fixtureApp = Join-Path $suiteRoot 'isolated-app'
$fixtureCore = Join-Path $suiteRoot 'isolated-core'
$dataDirectory = Join-Path $suiteRoot 'data'
New-Item -ItemType Directory -Path $fixtureApp,$fixtureCore,$dataDirectory -Force | Out-Null
$coreSource = Join-Path $repositoryRoot 'src/DesktopPet.Core'
Get-ChildItem -LiteralPath $coreSource -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object {
    $target = Join-Path $fixtureCore ([IO.Path]::GetRelativePath($coreSource,$_.FullName))
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target
}
foreach ($item in @('DesktopPet.App.csproj','app.manifest','App.xaml','App.xaml.cs','MainWindow.xaml','MainWindow.xaml.cs','Platform','Persistence')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "src/DesktopPet.App/$item") -Destination $fixtureApp -Recurse
}
$project = Join-Path $fixtureApp 'DesktopPet.App.csproj'
$projectText = (Get-Content -LiteralPath $project -Raw).Replace('..\DesktopPet.Core\DesktopPet.Core.csproj',(Join-Path $fixtureCore 'DesktopPet.Core.csproj'))
$projectText = $projectText.Replace('..\..\assets\pets\**\*',(Join-Path $repositoryRoot 'assets\pets\**\*'))
[IO.File]::WriteAllText($project,$projectText,[Text.UTF8Encoding]::new($false))
$startup = Join-Path $fixtureApp 'App.xaml.cs'
$startupText = Get-Content -LiteralPath $startup -Raw
if (-not $startupText.Contains('_archive = new ArchiveService();') -or -not $startupText.Contains('_mainWindow.Show();')) { throw 'Production startup seam changed; inspect fixture first.' }
$startupText = $startupText.Replace('_archive = new ArchiveService();','_archive = new ArchiveService(Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR") ?? throw new InvalidOperationException("Test directory required"));')
$startupText = $startupText.Replace('_mainWindow.Show();',"_mainWindow.Show();`n        _ = RunEmotionLiveAsync();")
[IO.File]::WriteAllText($startup,$startupText,[Text.UTF8Encoding]::new($false))
$mutexFile = Join-Path $fixtureApp 'Platform/SingleInstanceGuard.cs'
$mutexText = Get-Content -LiteralPath $mutexFile -Raw
if (-not $mutexText.Contains('Global\DesktopPet.App.SingleInstance')) { throw 'Mutex seam changed; inspect fixture first.' }
[IO.File]::WriteAllText($mutexFile,$mutexText.Replace('Global\DesktopPet.App.SingleInstance',"Local\DesktopPet.EmotionLive.$suiteId"),[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EmotionLiveProbe.cs') -Destination (Join-Path $fixtureApp 'EmotionLiveProbe.cs')
Set-Content -LiteralPath (Join-Path $repositoryRoot 'artifacts/emotion-live/latest-run.txt') -Value $suiteRoot
dotnet build $project -c Debug -m:1 --ignore-failed-sources --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Isolated fixture build failed.' }
if ($BuildOnly) { "BUILD_ONLY_COMPLETE=$suiteRoot"; exit 0 }
$realArchive = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopPet'
function Archive-Snapshot {
    if (-not (Test-Path -LiteralPath $realArchive)) { return 'ABSENT' }
    return (@(Get-ChildItem -LiteralPath $realArchive -File | Sort-Object Name | ForEach-Object { $_.Name+':'+(Get-FileHash -LiteralPath $_.FullName).Hash }) -join '|')
}
$before = Archive-Snapshot
$oldDirectory = $env:DESKTOPPET_VALIDATION_DIR
try {
    $env:DESKTOPPET_VALIDATION_DIR = $dataDirectory
    $exe = Join-Path $fixtureApp 'bin/Debug/net10.0-windows/DesktopPet.exe'
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $exe
    $start.UseShellExecute = $false
    $start.WindowStyle = 'Hidden'
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(60000)) { throw "Owned test PID $($process.Id) exceeded 60s; inspect its log. No unknown process killed." }
    $process.WaitForExit()
    $log = Join-Path $dataDirectory 'live.log'
    Get-Content -LiteralPath $log
    "PROCESS_EXIT_CODE=$($process.ExitCode)"
    if ($before -ne (Archive-Snapshot)) { throw 'Real archive changed; investigate before claiming isolation.' }
    'PASS original user archive snapshot unchanged'
    $saved = Get-Content -LiteralPath (Join-Path $dataDirectory 'state.json') -Raw | ConvertFrom-Json
    if ($saved.affinity -ne 55 -or $saved.isPaused) { throw 'Final production archive does not contain expected affinity/resume state.' }
    'PASS normal production tray exit persisted affinity=55, isPaused=false'
    if ($process.ExitCode -ne 0 -or @(Select-String -LiteralPath $log -Pattern ' FAIL | ERROR ').Count -gt 0) { throw 'Live validation failed; retain all evidence.' }
    "ALL EMOTION LIVE CHECKS PASS; $suiteRoot"
} finally {
    $env:DESKTOPPET_VALIDATION_DIR = $oldDirectory
}
