param([string[]]$Scenarios=@('normal','paused-start','offscreen-start','fast-final','recover-after-save','archive-cases','hold'))
$ErrorActionPreference='Stop'
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
$runRoot=Join-Path $PSScriptRoot ('run-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $runRoot | Out-Null
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'latest-run.txt') -Value $runRoot
$exe=Join-Path $PSScriptRoot 'isolated-app/bin/Debug/net10.0-windows/DesktopPet.exe'
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
 return Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $Directory ("stderr$Suffix.txt"))
}
try {
 "Isolated run: $runRoot"
 foreach($scenario in $Scenarios) {
  $caseDirectory=Join-Path $runRoot $scenario
  New-Item -ItemType Directory -Path $caseDirectory | Out-Null
  if($scenario -eq 'paused-start') {Set-Content (Join-Path $caseDirectory 'state.json') '{"schemaVersion":1,"windowLeft":100,"windowTop":100,"isPaused":true}'}
  if($scenario -eq 'offscreen-start') {Set-Content (Join-Path $caseDirectory 'state.json') '{"schemaVersion":1,"windowLeft":100000,"windowTop":100000,"savedDeviceName":"MISSING_DISPLAY","isPaused":false}'}
  if($scenario -eq 'restart-position') {
   Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-e6346a9a/fast-final/state.json') -Destination (Join-Path $caseDirectory 'state.json')
   Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-e6346a9a/archive-cases/roundtrip/settings.json') -Destination (Join-Path $caseDirectory 'settings.json')
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
   } else {'FAIL: second-instance dialog not observed'}
   if(-not $second.WaitForExit(5000)) {'FAIL: duplicate process did not exit'}
   else {"Second instance exit code=$($second.ExitCode)"}
   $process.Refresh()
   if(-not $process.HasExited) {'PASS: first instance remains running after duplicate exits'} else {'FAIL: first instance exited unexpectedly'}
  }
  if(-not $process.WaitForExit(22000)) {
   "FAIL: $scenario process did not exit within timeout; requesting WM_CLOSE on owned window"
   $owned=[LifecycleNative]::Find([uint32]$process.Id,$false)
   if($owned -ne [IntPtr]::Zero) {[LifecycleNative]::PostMessage($owned,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null}
   $process.WaitForExit(1500) | Out-Null
  }
  $process.Refresh()
  "CASE: $scenario; PID=$($process.Id); exited=$($process.HasExited); exitCode=$($process.ExitCode)"
  $probeLog=Join-Path $caseDirectory 'probe.log'
  if(Test-Path $probeLog) {Get-Content -LiteralPath $probeLog}
  $stderr=Join-Path $caseDirectory 'stderr.txt'
  if((Get-Item $stderr).Length -gt 0) {Get-Content -LiteralPath $stderr}
  if($scenario -in @('fast-final','recover-after-save')) {
   $expected=Get-Content (Join-Path $caseDirectory 'expected-position.json') -Raw | ConvertFrom-Json
   $statePath=Join-Path $caseDirectory 'state.json'
   if(Test-Path $statePath) {
    $saved=Get-Content $statePath -Raw | ConvertFrom-Json
    $matches=[Math]::Abs($saved.windowLeft-$expected.left) -lt 0.01 -and [Math]::Abs($saved.windowTop-$expected.top) -lt 0.01
    $(if($matches){'PASS'}else{'FAIL'})+": final position persisted; expected=($($expected.left),$($expected.top)), saved=($($saved.windowLeft),$($saved.windowTop))"
   } else {'FAIL: final position state file missing'}
  }
 }
 $originalAfter=Get-OriginalSnapshot
 if($originalBefore -ne $originalAfter) {throw '真实存档目录出现变化，请核对是否有其他实例运行'}
 'PASS: original LocalApplicationData/DesktopPet archive snapshot unchanged'
} finally {
 $env:DESKTOPPET_VALIDATION_DIR=$previousDirectory
 $env:DESKTOPPET_VALIDATION_SCENARIO=$previousScenario
}
