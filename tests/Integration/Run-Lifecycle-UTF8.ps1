param([string[]]$Scenarios=@('normal','paused-start','offscreen-start','fast-final','recover-after-save','archive-cases','hold','restart-position','readonly-state','readonly-settings'), [switch]$BuildOnly)
$ErrorActionPreference='Stop'
$repositoryRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$suiteId=[guid]::NewGuid().ToString('N').Substring(0,8)
$suiteRoot=Join-Path $repositoryRoot ("artifacts/p0-004-fix/run-$suiteId")
$fixtureApp=Join-Path $suiteRoot 'isolated-app'
New-Item -ItemType Directory -Path $fixtureApp -Force | Out-Null
foreach($fixtureItem in @('DesktopPet.App.csproj','app.manifest','App.xaml','App.xaml.cs','MainWindow.xaml','MainWindow.xaml.cs','Platform','Persistence')) {
 Copy-Item -LiteralPath (Join-Path $repositoryRoot "src/DesktopPet.App/$fixtureItem") -Destination $fixtureApp -Recurse
}
$fixtureProject=Join-Path $fixtureApp 'DesktopPet.App.csproj'
$fixtureProjectText=(Get-Content -LiteralPath $fixtureProject -Raw).Replace('..\DesktopPet.Core\DesktopPet.Core.csproj',(Join-Path $repositoryRoot 'src/DesktopPet.Core/DesktopPet.Core.csproj'))
# F-05: Replace relative assets path with absolute path so isolated fixture gets real character resources.
$fixtureProjectText=$fixtureProjectText.Replace('..\..\assets\pets\**\*',(Join-Path $repositoryRoot 'assets\pets\**\*'))
[IO.File]::WriteAllText($fixtureProject,$fixtureProjectText,[Text.UTF8Encoding]::new($false))
$fixtureStartup=Join-Path $fixtureApp 'App.xaml.cs'
$fixtureStartupText=Get-Content -LiteralPath $fixtureStartup -Raw
if(-not $fixtureStartupText.Contains('_archive = new ArchiveService();') -or -not $fixtureStartupText.Contains('_mainWindow.Show();')) {throw 'Startup test seams changed; review fixture instrumentation before running'}
$fixtureStartupText=$fixtureStartupText.Replace('_archive = new ArchiveService();','_archive = new ArchiveService(Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR") ?? throw new InvalidOperationException("Test archive path missing"));')
$fixtureStartupText=$fixtureStartupText.Replace('_mainWindow.Show();',"_mainWindow.Show();`n        _ = RunValidationAsync();")
[IO.File]::WriteAllText($fixtureStartup,$fixtureStartupText,[Text.UTF8Encoding]::new($false))
$fixtureMutex=Join-Path $fixtureApp 'Platform/SingleInstanceGuard.cs'
$fixtureMutexText=(Get-Content -LiteralPath $fixtureMutex -Raw).Replace('Global\DesktopPet.App.SingleInstance',"Local\DesktopPet.Validation.$suiteId")
[IO.File]::WriteAllText($fixtureMutex,$fixtureMutexText,[Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LifecycleProbe.cs') -Destination (Join-Path $fixtureApp 'LifecycleProbe.cs')
dotnet build $fixtureProject -c Debug -m:1 --ignore-failed-sources
if($LASTEXITCODE -ne 0) {throw 'Isolated fixture build failed'}
if($BuildOnly) {"Fixture build only: $fixtureApp"; exit 0}
$driverFailures=[Collections.Generic.List[string]]::new()
$ownedProcesses=[Collections.Generic.List[Diagnostics.Process]]::new()
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class LifecycleNative {
 public delegate bool EnumProc(IntPtr w,IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback,IntPtr data);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd,StringBuilder name,int size);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll",EntryPoint="PostMessageW")] public static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr dialog,int id);
 [DllImport("user32.dll",EntryPoint="SendMessageW")] public static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
 public static IntPtr Find(uint pid,bool dialog) {
  IntPtr result=IntPtr.Zero;
  EnumWindows((w,p)=>{uint actual;GetWindowThreadProcessId(w,out actual);var c=new StringBuilder(256);GetClassName(w,c,256);
   if(actual==pid && IsWindowVisible(w) && (c.ToString()=="#32770")==dialog) {result=w;return false;} return true;},IntPtr.Zero);
  return result;
 }
}
'@
$runRoot=Join-Path $suiteRoot 'data'
New-Item -ItemType Directory -Path $runRoot | Out-Null
Set-Content -LiteralPath (Join-Path $repositoryRoot 'artifacts/p0-004-fix/latest-run.txt') -Value $suiteRoot
$exe=Join-Path $fixtureApp 'bin/Debug/net10.0-windows/DesktopPet.exe'
$originalDirectory=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopPet'
function Get-OriginalSnapshot {
 if(Test-Path -LiteralPath $originalDirectory) {
  return @(Get-ChildItem -LiteralPath $originalDirectory -File | Sort-Object Name | ForEach-Object { $_.Name+':'+(Get-FileHash -LiteralPath $_.FullName).Hash }) -join '|'
 }
 return 'DIRECTORY_ABSENT'
}
$originalBefore=Get-OriginalSnapshot
$previousDirectory=$env:DESKTOPPET_VALIDATION_DIR
$previousScenario=$env:DESKTOPPET_VALIDATION_SCENARIO
function Start-TestPet([string]$Directory,[string]$Scenario,[string]$Suffix='') {
 $env:DESKTOPPET_VALIDATION_DIR=$Directory
 $env:DESKTOPPET_VALIDATION_SCENARIO=$Scenario
 $psi=[Diagnostics.ProcessStartInfo]::new()
 $psi.FileName=$exe
 $psi.WindowStyle='Hidden'
 $psi.UseShellExecute=$false
 $psi.RedirectStandardError=$true
 $started=[Diagnostics.Process]::Start($psi)
 $ownedProcesses.Add($started)
 return $started
}
try {
 "Isolated run: $runRoot"
 foreach($scenario in $Scenarios) {
  $caseDirectory=Join-Path $runRoot $scenario
  New-Item -ItemType Directory -Path $caseDirectory | Out-Null
  if($scenario -eq 'paused-start') {Set-Content (Join-Path $caseDirectory 'state.json') '{"schemaVersion":1,"windowLeft":100,"windowTop":100,"isPaused":true}'}
  if($scenario -eq 'offscreen-start') {Set-Content (Join-Path $caseDirectory 'state.json') '{"schemaVersion":1,"windowLeft":100000,"windowTop":100000,"savedDeviceName":"MISSING_DISPLAY","isPaused":false}'}
  if($scenario -eq 'readonly-state') {Set-Content (Join-Path $caseDirectory 'state.json') '{"schemaVersion":99,"windowLeft":{"newFormat":1},"futurePayload":42}'}
  if($scenario -eq 'readonly-settings') {Set-Content (Join-Path $caseDirectory 'settings.json') '{"schemaVersion":99,"animationSpeed":{"newFormat":2},"futurePayload":77}'}
  if($scenario -eq 'restart-position') {
   if(-not(Test-Path (Join-Path $runRoot 'fast-final/state.json'))) {throw 'restart-position requires fast-final earlier in this run'}
   Copy-Item -LiteralPath (Join-Path $runRoot 'fast-final/state.json') -Destination (Join-Path $caseDirectory 'state.json')
   Set-Content (Join-Path $caseDirectory 'settings.json') '{"schemaVersion":1,"animationSpeed":1.7}'
  }
  $process=Start-TestPet $caseDirectory $scenario
  if($scenario -eq 'hold') {
   $timer=[Diagnostics.Stopwatch]::StartNew()
   while(-not(Test-Path (Join-Path $caseDirectory 'probe.log')) -and $timer.ElapsedMilliseconds -lt 5000) {Start-Sleep -Milliseconds 50}
   $second=Start-TestPet $caseDirectory $scenario '-second'
   $dialog=[IntPtr]::Zero
   $timer.Restart()
   while($dialog -eq [IntPtr]::Zero -and $timer.ElapsedMilliseconds -lt 5000) {$dialog=[LifecycleNative]::Find([uint32]$second.Id,$true);Start-Sleep -Milliseconds 50}
   if($dialog -ne [IntPtr]::Zero) {
    "PASS: second isolated instance PID=$($second.Id) shows a dialog; activating its OK button via BM_CLICK"
    $okButton=[LifecycleNative]::GetDlgItem($dialog,1)
    if($okButton -eq [IntPtr]::Zero) {$okButton=[LifecycleNative]::GetDlgItem($dialog,2)}
    if($okButton -eq [IntPtr]::Zero) {throw 'Duplicate-instance OK button not found'}
    [LifecycleNative]::SendMessage($okButton,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
   } else {$driverFailures.Add('second-instance dialog not observed');'FAIL: second-instance dialog not observed'}
   if(-not $second.WaitForExit(5000)) {$driverFailures.Add('duplicate process did not exit');'FAIL: duplicate process did not exit'}
   else {"Second instance exit code=$($second.ExitCode)"}
   $process.Refresh()
   if(-not $process.HasExited) {'PASS: first instance remains running after duplicate exits'} else {$driverFailures.Add('first instance exited unexpectedly');'FAIL: first instance exited unexpectedly'}
  }
  if(-not $process.WaitForExit(22000)) {
   "FAIL: $scenario process did not exit within timeout; requesting WM_CLOSE on owned window"
   $driverFailures.Add("$scenario process timeout")
   $owned=[LifecycleNative]::Find([uint32]$process.Id,$false)
   if($owned -ne [IntPtr]::Zero) {[LifecycleNative]::PostMessage($owned,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null}
   $process.WaitForExit(1500) | Out-Null
  }
  $process.WaitForExit(500) | Out-Null
  $process.Refresh()
  $ec=$process.ExitCode
  if($ec -eq $null) {$ec=-1}
  "CASE: $scenario; PID=$($process.Id); exited=$($process.HasExited); exitCode=$ec"
  if(-not $process.HasExited -or $ec -ne 0) {$driverFailures.Add("$scenario unsuccessful exit")}
  $probeLog=Join-Path $caseDirectory 'probe.log'
  if(Test-Path $probeLog) {
   $caseLog=Get-Content -LiteralPath $probeLog
   $caseLog
   foreach($failureLine in @($caseLog | Where-Object {$_ -like 'FAIL:*'})) {$driverFailures.Add("$scenario $failureLine")}
  } else {$driverFailures.Add("$scenario probe log missing")}
  $stderrText=$process.StandardError.ReadToEnd()
  if($stderrText.Length -gt 0) {$stderrText}
  if($scenario -in @('fast-final','recover-after-save')) {
   $expected=Get-Content (Join-Path $caseDirectory 'expected-position.json') -Raw | ConvertFrom-Json
   $statePath=Join-Path $caseDirectory 'state.json'
   if(Test-Path $statePath) {
    $saved=Get-Content $statePath -Raw | ConvertFrom-Json
    $matches=[Math]::Abs($saved.windowLeft-$expected.left) -lt 0.01 -and [Math]::Abs($saved.windowTop-$expected.top) -lt 0.01
    if(-not $matches) {$driverFailures.Add("$scenario final position not persisted")}
    $(if($matches){'PASS'}else{'FAIL'})+": final position persisted; expected=($($expected.left),$($expected.top)), saved=($($saved.windowLeft),$($saved.windowTop))"
   } else {$driverFailures.Add("$scenario state file missing");'FAIL: final position state file missing'}
  }
 }
 $originalAfter=Get-OriginalSnapshot
 if($originalBefore -ne $originalAfter) {throw '真实存档目录出现变化，请核对是否有其他实例运行'}
 'PASS: original LocalApplicationData/DesktopPet archive snapshot unchanged'
} finally {
 foreach($ownedProcess in $ownedProcesses) {
  $ownedProcess.Refresh()
  if(-not $ownedProcess.HasExited) {
   foreach($isDialog in @($true,$false)) {
    $ownedWindow=[LifecycleNative]::Find([uint32]$ownedProcess.Id,$isDialog)
    if($ownedWindow -ne [IntPtr]::Zero) {[LifecycleNative]::PostMessage($ownedWindow,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null}
   }
   if(-not $ownedProcess.WaitForExit(3000)) {Write-Warning "Owned test PID $($ownedProcess.Id) did not close; not force-killed."}
  }
 }
 $env:DESKTOPPET_VALIDATION_DIR=$previousDirectory
 $env:DESKTOPPET_VALIDATION_SCENARIO=$previousScenario
}
if($driverFailures.Count -gt 0) {
 $driverFailures | ForEach-Object {"FAIL: $_"}
 exit 1
}
'ALL ISOLATED LIFECYCLE SCENARIOS PASS'
