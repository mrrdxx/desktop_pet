# P5B-002 / TESTING M-18: 窗口攀爬正式入口如实机验证驱动。
# 证据写入 .temp\p5b-002\m18\run-<时间戳>-fixture\。
#
# 与 M-17（探针 start 命令直接起攀）的区别：正式入口关键链必须是
#   真实右键菜单（生产 WndProc WM_RBUTTONUP → ShowNativeContextMenu →
#   TrackPopupMenuEx）→ UIA 定位「Climb on Window」→ 键盘选中 →
#   真实左键点击合格外部窗口 → 拾取状态机分派 Climb.TryStart 起攀 →
#   既有攀爬路线 → 交接栖息。
# pick-entry 探针命令与 menu-entry 缝合命令都不能代替真实右键入口证据
# （本驱动不使用这两条命令；真实右键注入后菜单由生产 WndProc 自己弹出）。
#
# 操作序列（每步断言探针事件与心跳状态）：
#   1  目标标记与归属确认（独立 WinForms 夹具 P5B002NP-<ts>，零写入前提）
#   2  真实右键「Climb on Window」→ climbPickActive=true、climbPickStatus=active
#   3  真实点击合格近距目标 → started + climb-started(targetHwnd 匹配) + climbActive=true
#   4  路线推进：climb-step ≥3 且 fraction 递增
#   5  顶部交接：climb-completed 脚点=上缘中点±6、perchActive、无跳动
#   6  交接后跟随：移动目标 → 脚点贴新上缘（perchActive 保持）
#   7  平行入口回归：真实右键「Perch on Window」→ 点击目标 → selected +
#      perchActive=true、climbActive=false（攀爬不触发）
#   8  拒绝：真实点击桌面/壳（Progman/WorkerW/任务栏）→ rejected-ineligible，宠物不动
#   9  拒绝：拾取中把目标移至 >200px → 点击 → rejected-too-far，宠物不动
#   10 取消：拾取中点击宠物自身 → cancelled-self，宠物不进入攀爬
#   11 取消：拾取保持 15s 真实超时 → cancelled-timeout，宠物不动
#   12 取消：拾取中打开真实菜单 → cancelled（EnterContextMenuMode 先取消拾取），Esc 关菜单
#   13 取消：拾取中暂停 → cancelled，resume 后不续接
#   14 取消：拾取中真实拖动 → cancelled（拖动接管，回空闲）
#   15 既有接管不回归：攀爬中真实拖动 → climb-drag-preempted，松手后不回拉
#   16 探针 exitCode=0
#   17 其他轮标记文件/标记窗口/无窗口进程未被越界清理（诱饵存活）
#   18 完整流程走完（固定条目数）
#
# 目标锁定纪律与 M-16/M-17 相同：本轮独有标记 + 精确启动 PID +
# StartTime≥t0−2s 归属确认，不碰用户窗口；清理只针对本轮归属对象，
# 诱饵对照证明清理范围不越界。
# 真实鼠标注入纪律：SetCursorPos 回读±2 + WindowFromPoint 根窗口前置校验；
# 每个 attempt 前查探针存活；注入前 ≤2.5s 确认拾取仍 active（防 15s 超时耗尽）。
# 菜单出现/关闭判定纯 Find-ProbeMenuHwnd（#32768 归探针进程）；Enter 后零 UIA。
#
# 用法（仓库根目录，pwsh 7 -File 独立进程运行）：
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B002EntryPreview.ps1 -DurationSec 600

param(
    [string]$FixtureTitle,
    [string]$ProbePath,
    [int]$DurationSec = 600
)

$ErrorActionPreference = 'Stop'
if ($FixtureTitle) {
    Add-Type -AssemblyName System.Windows.Forms
    $form = [System.Windows.Forms.Form]::new()
    try {
        $form.Text = $FixtureTitle
        $form.ShowInTaskbar = $true
        $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
        [System.Windows.Forms.Application]::Run($form)
    } finally { $form.Dispose() }
    return
}

$repo = Split-Path -Parent $PSScriptRoot
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:t0 = Get-Date
$runDir = Join-Path $repo ".temp\p5b-002\m18\run-$ts-fixture"
$scenarioDir = Join-Path $runDir 'window-climb-live'
New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
$resultFile = Join-Path $runDir 'probe-result.json'
$eventsFile = Join-Path $scenarioDir 'climb-live-events.jsonl'
$stateFile = Join-Path $scenarioDir 'climb-live-state.json'
$commandsFile = Join-Path $scenarioDir 'climb-live-commands.txt'
$probeExe = if ($ProbePath) { (Resolve-Path -LiteralPath $ProbePath).Path } else { Join-Path $repo 'tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe' }
Start-Transcript -Path (Join-Path $runDir 'driver-transcript.log') | Out-Null

Add-Type -AssemblyName System.Windows.Forms
if (-not ([System.Management.Automation.PSTypeName]'M15Dpi').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M15Dpi {
  private static readonly IntPtr PMv2 = new IntPtr(-4);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);
  public static int EnablePerMonitorV2() {
    SetThreadDpiAwarenessContext(PMv2);
    return GetAwarenessFromDpiAwarenessContext(SetThreadDpiAwarenessContext(PMv2));
  }
}
"@
}
$threadAwareness = [M15Dpi]::EnablePerMonitorV2()
Write-Host "[M18] driver thread DPI awareness = $threadAwareness (2=PerMonitor; required for physical px)"
if (-not ([System.Management.Automation.PSTypeName]'M15W').Type) {
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class M15W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public static System.Collections.Generic.List<long> Tops(){var l=new System.Collections.Generic.List<long>();EnumWindows((h,p)=>{if(IsWindowVisible(h))l.Add(h.ToInt64());return true;},IntPtr.Zero);return l;}
}
"@
}
if (-not ([System.Management.Automation.PSTypeName]'M15W2').Type) {
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class M15W2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, ref RECT r, int cb);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
  [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
"@
}
if (-not ([System.Management.Automation.PSTypeName]'TW').Type) {
Add-Type @"
using System.Runtime.InteropServices;
public static class TW { [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey); }
"@
}
if (-not ([System.Management.Automation.PSTypeName]'M15Hit').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M15Hit {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint ga);
}
"@
}
if (-not ([System.Management.Automation.PSTypeName]'M16K').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M16K {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  static void Tap(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }
  public static void Esc() { Tap(0x1B); }
  public static void Down() { Tap(0x28); }
  public static void Enter() { Tap(0x0D); }
  public static void AltTap() { Tap(0x12); }
}
"@
}
if (-not ([System.Management.Automation.PSTypeName]'M16M').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M16M {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 256
    [M15W]::GetWindowText($h, $sb, 256) | Out-Null
    $sb.ToString()
}
function Get-Class([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 256
    [M15W2]::GetClassName($h, $sb, 256) | Out-Null
    $sb.ToString()
}
function Get-Tops { [M15W]::Tops() }
function Get-FrameBounds([IntPtr]$h) {
    $r = New-Object M15W2+RECT
    $hr = [M15W2]::DwmGetWindowAttribute($h, 9, [ref]$r, [System.Runtime.InteropServices.Marshal]::SizeOf([type][M15W2+RECT]))
    if ($hr -ne 0) { return $null }
    $r
}
function Raise-Top([IntPtr]$h) {
    [M15W]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
}
function HwndHex([IntPtr]$h) { '0x' + $h.ToInt64().ToString('X') }
function Get-OccludersAtPoint([int]$x, [int]$y) {
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($h64 in (Get-Tops)) {
        $h = [IntPtr]$h64
        $r = New-Object M15W+RECT
        if (-not [M15W]::GetWindowRect($h, [ref]$r)) { continue }
        if ($x -ge $r.L -and $x -lt $r.R -and $y -ge $r.T -and $y -lt $r.B) {
            $t = (Get-Title $h)
            if ($t) { $hits.Add("[$($r.L),$($r.T),$($r.R),$($r.B)]$t") }
        }
    }
    ($hits | Select-Object -Unique | Select-Object -First 5) -join ' ; '
}
function Read-Events {
    if (-not (Test-Path $eventsFile)) { return @() }
    Get-Content $eventsFile -ErrorAction SilentlyContinue | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }
}
function Wait-Event([string]$type, [int]$timeoutSec = 15, [int]$afterMs = 0, [scriptblock]$where = $null) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $ev = Read-Events | Where-Object { $_.type -eq $type -and $_.tMs -gt $afterMs }
        if ($where) { $ev = $ev | Where-Object -FilterScript $where }
        if ($ev) { return $ev[-1] }
        Start-Sleep -Milliseconds 200
    }
    return $null
}
function Get-LastEventMs {
    $all = Read-Events
    if (-not $all) { return 0 }
    ($all | Measure-Object -Property tMs -Maximum).Maximum
}
function Get-State {
    for ($i = 0; $i -lt 6; $i++) {
        if (Test-Path $stateFile) {
            try { return (Get-Content $stateFile -Raw | ConvertFrom-Json) } catch { }
        }
        Start-Sleep -Milliseconds 50
    }
    return $null
}
function Send-Command([string]$cmd) {
    Add-Content -LiteralPath $commandsFile -Value $cmd -Encoding UTF8
}
function Test-PointOnScreen([int]$x, [int]$y) {
    $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
    return ($x -ge $vs.X -and $x -lt ($vs.X + $vs.Width) -and
        $y -ge $vs.Y -and $y -lt ($vs.Y + $vs.Height))
}
function Test-ProbeAlive {
    if ($null -ne $script:probe -and $script:probe.HasExited) {
        Write-Host "[M18] probe exited early (pid $($script:probe.Id)) - driver stalled past probe lifetime"
        return $false
    }
    return $true
}
function Find-PetHwnd([int]$tolerance = 3) {
    $st = Get-State
    if ($null -eq $st -or $null -eq $st.petRectPx) { return [IntPtr]::Zero }
    if ($st.petHwnd) {
        $known = [IntPtr]([long]$st.petHwnd)
        $knownPid = [uint32]0
        [M15W2]::GetWindowThreadProcessId($known,[ref]$knownPid) | Out-Null
        if ([M15W2]::IsWindow($known) -and $knownPid -eq $script:probe.Id) { return $known }
        return [IntPtr]::Zero
    }
    $petRect = $st.petRectPx
    foreach ($h in Get-Tops) {
        $p = [uint32]0
        [M15W2]::GetWindowThreadProcessId([IntPtr]$h, [ref]$p) | Out-Null
        if ($p -ne [uint32]$script:probe.Id) { continue }
        $pr = New-Object M15W+RECT
        [M15W]::GetWindowRect([IntPtr]$h, [ref]$pr) | Out-Null
        if ([Math]::Abs($pr.L - $petRect[0]) -le $tolerance -and [Math]::Abs($pr.T - $petRect[1]) -le $tolerance) { return [IntPtr]$h }
    }
    return [IntPtr]::Zero
}
function Get-FootToFrameDistance($foot, $fb) {
    $dx = 0
    if ($foot[0] -lt $fb.L) { $dx = $fb.L - $foot[0] }
    elseif ($foot[0] -gt $fb.R) { $dx = $foot[0] - $fb.R }
    $dy = 0
    if ($foot[1] -lt $fb.T) { $dy = $fb.T - $foot[1] }
    elseif ($foot[1] -gt $fb.B) { $dy = $foot[1] - $fb.B }
    [Math]::Sqrt($dx * $dx + $dy * $dy)
}
function Find-ProbeMenuHwnd {
    $mh = [M16M]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, '#32768', $null)
    while ($mh -ne [IntPtr]::Zero) {
        $mp = [uint32]0
        [M16M]::GetWindowThreadProcessId($mh, [ref]$mp) | Out-Null
        if ($mp -eq [uint32]$script:probe.Id) { return $mh }
        $mh = [M16M]::FindWindowEx([IntPtr]::Zero, $mh, '#32768', $null)
    }
    return [IntPtr]::Zero
}
function Wait-PickStatus([string[]]$statuses, [int]$timeoutSec = 8, [string]$statusProp = 'climbPickStatus') {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $st = Get-State
        if ($null -ne $st -and $null -ne $st.PSObject.Properties[$statusProp] -and
            $statuses -contains $st.$statusProp) { return $st }
        Start-Sleep -Milliseconds 150
    }
    return $null
}

$script:checks = New-Object System.Collections.Generic.List[object]
function Test-M18Complete($checks, [bool]$reachedEnd, [bool]$failed) {
    # 调用时机：完整性条目 ⑱ 尚未加入 list；传入的 list = ①-⑯ 行为项 + ⑰ 诱饵 = 17 项。
    # 全部唯一 PASS + reachedEnd 且未 failed ⇒ 18 项清单（加 ⑱ 完整性后）完整。
    $expectedIds = '①②③④⑤⑥⑦⑧⑨⑩⑪⑫⑬⑭⑮⑯⑰'
    $actualIds = (@($checks | ForEach-Object { $_.Item.Substring(0,1) }) -join '')
    return ($reachedEnd -and -not $failed -and $checks.Count -eq 17 -and
        $actualIds -ceq $expectedIds -and @($checks | Where-Object Result -NE 'PASS').Count -eq 0)
}
function Add-Check([string]$name, [bool]$pass, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Item = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} : {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

# ---- 目标题材（独立 WinForms 夹具，M-17 手法）----
$script:markerFile = Join-Path $runDir "P5B002NP-$ts.txt"
Set-Content -LiteralPath $script:markerFile -Value "P5B-002 M-18 marker file (run $ts)" -Encoding UTF8
$targetSubstr = "P5B002NP-$ts"
$script:otherRunMarkerFiles = @(
    Get-ChildItem (Split-Path $runDir -Parent) -Directory -Filter 'run-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -File -Filter 'P5B002NP-*.txt' -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -ne $script:markerFile } |
        ForEach-Object { $_.FullName })
function Start-Target {
    Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @(
        '-NoProfile','-Sta','-File', "`"$PSCommandPath`"", '-FixtureTitle', $targetSubstr
    ) -PassThru -WindowStyle Normal
}

$script:ownedTargetHwnd = [IntPtr]::Zero
$script:ownedTargetPid = 0
function Invoke-CleanupOwnedTarget {
    $h = $script:ownedTargetHwnd
    if ($h -eq [IntPtr]::Zero -or -not [M15W2]::IsWindow($h)) { return }
    $ownerPid = [uint32]0
    [M15W2]::GetWindowThreadProcessId($h,[ref]$ownerPid) | Out-Null
    if ($ownerPid -ne $targetProc.Id -or $targetProc.HasExited -or (Get-Title $h) -cne $targetSubstr) { return }
    [M15W]::SendMessage($h,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
}

# ---- 诱饵对照（M-17 R1 F-03 纪律：清理范围不越界）----
$script:lureDir = Join-Path (Split-Path $runDir -Parent) "lure-run-$ts"
New-Item -ItemType Directory -Force -Path $script:lureDir | Out-Null
$script:lureFile = Join-Path $script:lureDir "P5B002NP-lure-$ts.txt"
Set-Content -LiteralPath $script:lureFile -Value "P5B-002 other-run marker lure (must survive this run)" -Encoding UTF8
$script:lureWindow = New-Object System.Windows.Forms.Form
$script:lureWindow.Text = "P5B002NP-lure-$ts.txt"
$script:lureWindow.StartPosition = 'Manual'
$script:lureWindow.Left = 60; $script:lureWindow.Top = 60
$script:lureWindow.Width = 300; $script:lureWindow.Height = 120
$script:lureWindow.Show()
Start-Sleep -Milliseconds 300
$lureR = New-Object M15W+RECT
[M15W]::GetWindowRect($script:lureWindow.Handle, [ref]$lureR) | Out-Null
$script:lureSnapshot = @{ Text = $script:lureWindow.Text; L = $lureR.L; T = $lureR.T; R = $lureR.R; B = $lureR.B }
$script:windowlessLure = Start-Process -FilePath 'powershell' `
    -ArgumentList '-NoProfile', '-Command', 'Start-Sleep 600' -PassThru `
    -WindowStyle Hidden
Write-Host "[M18] lures set: file=$script:lureFile window='$($script:lureWindow.Text)' @($($lureR.L),$($lureR.T)) windowlessPid=$($script:windowlessLure.Id)"

Write-Host "[M18] run dir: $runDir"
Write-Host "[M18] targetSubstr='$targetSubstr' (unique; user windows cannot match)"
@{ targetTitleSubstr = $targetSubstr; durationSec = $DurationSec } |
    ConvertTo-Json | Set-Content (Join-Path $scenarioDir 'window-climb-live-config.json') -Encoding UTF8

function Find-OwnedTarget([long[]]$excludeHwnds) {
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        foreach ($h in Get-Tops) {
            if ($excludeHwnds -contains $h) { continue }
            $t = Get-Title ([IntPtr]$h)
            if ($t -notlike "*$targetSubstr*") { continue }
            $cPid = [uint32]0
            [M15W2]::GetWindowThreadProcessId([IntPtr]$h, [ref]$cPid) | Out-Null
            $cProc = Get-Process -Id $cPid -ErrorAction SilentlyContinue
            $owned = ($null -ne $cProc -and $cPid -eq $targetProc.Id -and -not $targetProc.HasExited -and
                $cPid -ne $PID -and $cProc.StartTime -ge $script:t0.AddSeconds(-2))
            if (-not $owned) { continue }
            return @{ Hwnd = [IntPtr]$h; Pid = $cPid }
        }
    }
    return $null
}

# 真实右键注入 + 生产 TrackPopupMenuEx 菜单定位 + UIA 索引 + 键盘选中。
# 参照 M-16 R1 补证轮 Invoke-RealContextMenu；选项名精确匹配。
function Invoke-RealContextMenu([string]$itemName) {
    $petHwnd = [IntPtr]::Zero
    for ($fp = 0; $fp -lt 4 -and $petHwnd -eq [IntPtr]::Zero; $fp++) {
        $petHwnd = Find-PetHwnd
        if ($petHwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
    }
    if ($petHwnd -eq [IntPtr]::Zero) { return @{ Ok = $false; Detail = 'pet hwnd not found by probe pid' } }
    $st = Get-State
    if ($null -eq $st) { return @{ Ok = $false; Detail = 'state missing for pet rect' } }
    $petRect = $st.petRectPx
    $petW = $petRect[2] - $petRect[0]; $petH = $petRect[3] - $petRect[1]
    $click = $null
    foreach ($fy in 0.5, 0.4, 0.6, 0.3, 0.7, 0.25, 0.75) {
        foreach ($fx in 0.5, 0.4, 0.6, 0.35, 0.65, 0.3, 0.7) {
            $cx = $petRect[0] + [int]($petW * $fx)
            $cy = $petRect[1] + [int]($petH * $fy)
            if (-not (Test-PointOnScreen $cx $cy)) { continue }
            $tp = New-Object M15Hit+POINT
            $tp.X = $cx; $tp.Y = $cy
            if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($tp), 2) -eq $petHwnd) { $click = @($cx, $cy); break }
        }
        if ($click) { break }
    }
    if (-not $click) { return @{ Ok = $false; Detail = "no right-click point resolving to pet root" } }
    $posOk = $false
    for ($try = 1; $try -le 5; $try++) {
        [M15W2]::SetCursorPos($click[0], $click[1]) | Out-Null
        $cp = New-Object M15W2+POINT
        [M15W2]::GetCursorPos([ref]$cp) | Out-Null
        if ([Math]::Abs($cp.X - $click[0]) -le 2 -and [Math]::Abs($cp.Y - $click[1]) -le 2) { $posOk = $true; break }
        Start-Sleep -Milliseconds 300
    }
    if (-not $posOk) { return @{ Ok = $false; Detail = "cursor injection ineffective (user mouse active?) at $($click -join ',')" } }
    $hitPt = New-Object M15Hit+POINT
    $hitPt.X = $click[0]; $hitPt.Y = $click[1]
    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hitPt), 2) -ne $petHwnd) {
        return @{ Ok = $false; Detail = 'hit check failed at right-click point' }
    }
    $selected = $false
    $selDetail = ''
    $lastFg = [IntPtr]::Zero
    for ($rc = 1; $rc -le 4 -and -not $selected; $rc++) {
        $fgBefore = [M15W2]::GetForegroundWindow()
        $fgPid = [uint32]0
        [M15W2]::GetWindowThreadProcessId($fgBefore, [ref]$fgPid) | Out-Null
        $lii = New-Object M15W2+LASTINPUTINFO
        $lii.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($lii)
        [M15W2]::GetLastInputInfo([ref]$lii) | Out-Null
        $idleSec = [Math]::Round(([Environment]::TickCount - [int]$lii.dwTime) / 1000.0, 1)
        Write-Host "[M18] real-menu attempt $rc pre-click: fg=0x$($fgBefore.ToString('X')) title='$(Get-Title $fgBefore)' lastInput=${idleSec}s ago"
        [M15W2]::SetCursorPos($click[0], $click[1]) | Out-Null
        [M15W2]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)   # RIGHTDOWN
        Start-Sleep -Milliseconds 120
        [M15W2]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)   # RIGHTUP
        $menuNative = [IntPtr]::Zero
        $deadline = (Get-Date).AddSeconds(3)
        while ((Get-Date) -lt $deadline -and $menuNative -eq [IntPtr]::Zero) {
            $menuNative = Find-ProbeMenuHwnd
            if ($menuNative -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
        }
        if ($menuNative -eq [IntPtr]::Zero) {
            $fgAfter = [M15W2]::GetForegroundWindow()
            Write-Host "[M18] real-menu attempt $rc : no menu (fgBefore=0x$($fgBefore.ToString('X')) petHwnd=0x$($petHwnd.ToString('X')))"
            [M16K]::AltTap()
            Start-Sleep -Milliseconds 600
            continue
        }
        $lastFg = [M15W2]::GetForegroundWindow()
        if ($lastFg -ne $petHwnd) {
            Write-Host "[M18] real-menu attempt $rc : menu open but foreground=0x$($lastFg.ToString('X')) != pet; dismiss & retry"
            [M16K]::Esc()
            Start-Sleep -Milliseconds 400
            continue
        }
        $menuEl = $null
        for ($ui = 0; $ui -lt 5 -and $null -eq $menuEl; $ui++) {
            $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$script:probe.Id)
            $elems = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
            foreach ($el in $elems) {
                if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu) { $menuEl = $el; break }
            }
            if ($null -eq $menuEl) { Start-Sleep -Milliseconds 150 }
        }
        if ($null -eq $menuEl) {
            Write-Host "[M18] real-menu attempt $rc : native menu open but UIA menu element missing; dismiss & retry"
            [M16K]::Esc()
            Start-Sleep -Milliseconds 400
            continue
        }
        $itemsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
        $allItems = $menuEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemsCond)
        $idx = -1
        for ($i = 0; $i -lt $allItems.Count; $i++) {
            if ($allItems[$i].Current.Name -eq $itemName) { $idx = $i; break }
        }
        if ($idx -lt 0) {
            [M16K]::Esc()
            return @{ Ok = $false; Detail = "menu item '$itemName' not found in native menu (items: $((@($allItems | ForEach-Object { $_.Current.Name })) -join '|'))" }
        }
        for ($k = 0; $k -le $idx; $k++) {
            [M16K]::Down()
            Start-Sleep -Milliseconds 70
        }
        Start-Sleep -Milliseconds 150
        [M16K]::Enter()
        $closed = $false
        $deadlineClose = (Get-Date).AddMilliseconds(1500)
        while ((Get-Date) -lt $deadlineClose) {
            Start-Sleep -Milliseconds 100
            if ((Find-ProbeMenuHwnd) -eq [IntPtr]::Zero) { $closed = $true; break }
        }
        if (-not $closed) {
            Write-Host "[M18] real-menu attempt $rc : menu did not close after Enter; dismiss & retry"
            [M16K]::Esc()
            Start-Sleep -Milliseconds 400
            continue
        }
        $selected = $true
        $selDetail = "real menu '$itemName' selected: rightClick=[$($click -join ',')] itemIndex=$idx totalItems=$($allItems.Count) viaKeyboard petHwnd=0x$($petHwnd.ToString('X'))"
    }
    if (-not $selected) {
        [M16K]::Esc()
        return @{ Ok = $false; Detail = "real menu '$itemName' not selectable (last fg=0x$($lastFg.ToString('X')) petHwnd=0x$($petHwnd.ToString('X')))" }
    }
    return @{ Ok = $true; Detail = $selDetail }
}

# 起攀前保证宠物空闲：未栖息/未攀爬。栖息中先真实拖动脱离（M-17 Ensure-IdleForClimb）。
function Ensure-IdleForClimb {
    for ($e = 1; $e -le 3; $e++) {
        $st = Get-State
        if ($null -eq $st) { Start-Sleep -Milliseconds 400; continue }
        if (-not $st.climbActive -and -not $st.perchActive) { return @{ Ok = $true; Detail = 'idle' } }
        if (-not $st.climbActive -and $st.perchActive) {
            $dg = Invoke-RealDrag 26 4 10 700
            Start-Sleep -Milliseconds 800
            $st2 = Get-State
            if ($null -ne $st2 -and -not $st2.perchActive -and -not $st2.climbActive) {
                return @{ Ok = $true; Detail = "detached: $($dg.Detail)" }
            }
            Start-Sleep -Milliseconds 600
            continue
        }
        if ($st.climbActive) {
            $deadlineIdle = (Get-Date).AddSeconds(12)
            while ((Get-Date) -lt $deadlineIdle) {
                $st3 = Get-State
                if ($null -ne $st3 -and -not $st3.climbActive -and -not $st3.perchActive) {
                    return @{ Ok = $true; Detail = 'climb finished' }
                }
                Start-Sleep -Milliseconds 300
            }
        }
    }
    return @{ Ok = $false; Detail = 'pet busy and could not free' }
}

# 落点遮挡者诊断（M-17）：产品正确 RejectedOccluded 与环境的区分。
function Get-LandingOccluder([IntPtr]$targetHwnd) {
    $fb = Get-FrameBounds $targetHwnd
    if ($null -eq $fb) { return 'no target frame bounds' }
    $stL = Get-State
    if ($null -eq $stL -or $null -eq $stL.petRectPx) { return 'pet rect missing' }
    $petW = [int]($stL.petRectPx[2] - $stL.petRectPx[0])
    $cy = $fb.T + 2
    $cx = [int](($fb.L + $fb.R) / 2.0)
    $half = [int]($petW / 2)
    $found = [System.Collections.Generic.List[string]]::new()
    for ($x = ($cx - $half + 5); $x -le ($cx + $half - 5); $x += 20) {
        $p = New-Object M15Hit+POINT
        $p.X = $x; $p.Y = $cy
        $hit = [M15Hit]::WindowFromPoint($p)
        $root = [M15Hit]::GetAncestor($hit, 2)
        if ($root -ne [IntPtr]::Zero -and $root -ne $targetHwnd) {
            $found.Add(("0x{0:X} '{1}'" -f $root.ToInt64(), (Get-Title $root)))
        }
    }
    if ($found.Count -eq 0) { return 'none' }
    (($found | Select-Object -Unique) -join '; ')
}

# 真实拖动助手（M-17）：三重校验（SetCursorPos 回读±2 + WindowFromPoint 根==宠物）。
function Invoke-RealDrag([int]$steps, [int]$dx, [int]$dy, [int]$holdMs, [int]$tolerance = 3) {
    $petHwnd = [IntPtr]::Zero
    for ($fp = 0; $fp -lt 4 -and $petHwnd -eq [IntPtr]::Zero; $fp++) {
        $petHwnd = Find-PetHwnd $tolerance
        if ($petHwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
    }
    if ($petHwnd -eq [IntPtr]::Zero) { return @{ Ok = $false; Detail = 'pet hwnd not found by probe pid' } }
    $grab = $null
    for ($gs = 0; $gs -lt 3 -and $null -eq $grab; $gs++) {
        $st = Get-State
        if ($null -eq $st) { Start-Sleep -Milliseconds 250; continue }
        $liveRect = New-Object M15W+RECT
        [M15W]::GetWindowRect($petHwnd,[ref]$liveRect) | Out-Null
        $pr = @($liveRect.L,$liveRect.T,$liveRect.R,$liveRect.B)
        $petW = $pr[2] - $pr[0]; $petH = $pr[3] - $pr[1]
        foreach ($fy in 0.85, 0.75, 0.65, 0.55, 0.45, 0.35, 0.25, 0.15) {
            foreach ($fx in 0.5, 0.4, 0.6, 0.3, 0.7) {
                $cx = $pr[0] + [int]($petW * $fx)
                $cy = $pr[1] + [int]($petH * $fy)
                if (-not (Test-PointOnScreen $cx $cy)) { continue }
                $tp = New-Object M15Hit+POINT
                $tp.X = $cx; $tp.Y = $cy
                if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($tp), 2) -eq $petHwnd) { $grab = @($cx, $cy); break }
            }
            if ($grab) { break }
        }
    }
    if ($null -eq $grab) { return @{ Ok = $false; Detail = 'no grab point resolving to pet root' } }
    $cx = $grab[0]; $cy = $grab[1]
    $posOk = $false
    for ($try = 1; $try -le 5; $try++) {
        [M15W2]::SetCursorPos($cx, $cy) | Out-Null
        $cp = New-Object M15W2+POINT
        [M15W2]::GetCursorPos([ref]$cp) | Out-Null
        if ([Math]::Abs($cp.X - $cx) -le 2 -and [Math]::Abs($cp.Y - $cy) -le 2) { $posOk = $true; break }
        Start-Sleep -Milliseconds 300
    }
    if (-not $posOk) { return @{ Ok = $false; Detail = "cursor injection ineffective at ($cx,$cy) - user mouse active?" } }
    $hitPt = New-Object M15Hit+POINT
    $hitPt.X = $cx; $hitPt.Y = $cy
    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hitPt), 2) -ne $petHwnd) {
        return @{ Ok = $false; Detail = 'hit check failed at grab point' }
    }
    [M15W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
    Start-Sleep -Milliseconds 150
    for ($i = 1; $i -le $steps; $i++) {
        $nx = $cx + $i * $dx; $ny = $cy + $i * $dy
        if (-not (Test-PointOnScreen $nx $ny)) { break }
        [M15W2]::SetCursorPos($nx, $ny) | Out-Null
        Start-Sleep -Milliseconds 90
    }
    Start-Sleep -Milliseconds $holdMs
    [M15W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
    Start-Sleep -Milliseconds 400
    return @{ Ok = $true; Detail = "dragged from ($cx,$cy) by ($($steps*$dx),$($steps*$dy)) steps=$steps hold=$holdMs`ms" }
}

# 目标贴宠物脚侧放置（M-17 手法）：右 130px（放不下镜像），上缘 ≥160px 屏顶。
function Place-TargetNearFoot {
    for ($r = 1; $r -le 4; $r++) {
        $st = Get-State
        if ($null -ne $st -and $null -ne $st.petFootPx) { break }
        Start-Sleep -Milliseconds 300
    }
    $st = Get-State
    if ($null -eq $st -or $null -eq $st.petFootPx) { return $false }
    $sb = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $fx = [int]$st.petFootPx[0]; $fy = [int]$st.petFootPx[1]
    $tx = $fx + 130
    if (($tx + 800) -gt $sb.Right) { $tx = $fx - 130 - 800 }
    if ($tx -lt $sb.Left) { $tx = $sb.Left }
    $ty = $fy - 260
    if ($ty -lt ($sb.Top + 160)) { $ty = $sb.Top + 160 }
    if (($ty + 500) -gt $sb.Bottom) { $ty = $sb.Bottom - 500 }
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, $tx, $ty, 800, 500, 0x0004) | Out-Null
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 600
    if ([M15W2]::IsIconic($hwnd)) {
        [M15W]::ShowWindow($hwnd, 9) | Out-Null
        Raise-Top $hwnd
        Start-Sleep -Milliseconds 600
    }
    return $true
}

# 找一个 WindowFromPoint 根窗口==$h 的点（标题区优先，避开边框拖拽带；M-16 手法）。
function Find-TargetClickPoint([IntPtr]$h) {
    $fb = $null
    for ($scanTry = 1; $scanTry -le 3; $scanTry++) {
        if ($scanTry -gt 1) {
            if ([M15W2]::IsIconic($h)) { [M15W]::ShowWindow($h, 1) | Out-Null }
            Raise-Top $h
            Start-Sleep -Milliseconds 800
        }
        $fb = Get-FrameBounds $h
        if ($null -eq $fb) { continue }
        foreach ($fy in 0.10, 0.06, 0.15, 0.20, 0.05) {
            foreach ($fx in 0.25, 0.4, 0.6, 0.75, 0.5, 0.15, 0.85) {
                $cx = $fb.L + [int](($fb.R - $fb.L) * $fx)
                $cy = $fb.T + [int](($fb.B - $fb.T) * $fy)
                if ($cy - $fb.T -lt 10) { $cy = $fb.T + 12 }
                if (-not (Test-PointOnScreen $cx $cy)) { continue }
                $pt = New-Object M15Hit+POINT
                $pt.X = $cx; $pt.Y = $cy
                if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($pt), 2) -eq $h) { return @{ Ok = $true; Point = @($cx, $cy); Bounds = $fb } }
            }
        }
    }
    if ($null -eq $fb) { return @{ Ok = $false; Detail = 'frame bounds missing' } }
    return @{ Ok = $false; Detail = 'no click point resolving to target root' }
}

# 真实左键点击指定物理点（rejected/cancelled 场景用）。注入前校验
# SetCursorPos 回读±2 + WindowFromPoint 根窗口==指定目标（点击点必达层）。
function Invoke-RealLeftClick([int[]]$point, [IntPtr]$expectedRoot, [int]$holdMs = 300) {
    $posOk = $false
    for ($try = 1; $try -le 5; $try++) {
        [M15W2]::SetCursorPos($point[0], $point[1]) | Out-Null
        $cp = New-Object M15W2+POINT
        [M15W2]::GetCursorPos([ref]$cp) | Out-Null
        if ([Math]::Abs($cp.X - $point[0]) -le 2 -and [Math]::Abs($cp.Y - $point[1]) -le 2) { $posOk = $true; break }
        Start-Sleep -Milliseconds 300
    }
    if (-not $posOk) { return @{ Ok = $false; Detail = "cursor injection ineffective (user mouse active?) at $($point -join ',')" } }
    $hitPt = New-Object M15Hit+POINT
    $hitPt.X = $point[0]; $hitPt.Y = $point[1]
    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hitPt), 2) -ne $expectedRoot) {
        $actualRoot = [M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hitPt), 2)
        return @{ Ok = $false; Detail = "hit check failed: root=0x$($actualRoot.ToString('X')) expected=0x$($expectedRoot.ToString('X')) occluders=$(Get-OccludersAtPoint $point[0] $point[1])" }
    }
    [M15W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
    Start-Sleep -Milliseconds $holdMs
    [M15W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
    Start-Sleep -Milliseconds 300
    return @{ Ok = $true; Detail = "click=[$($point -join ',')] hold=${holdMs}ms" }
}

# 进入攀爬拾取：先把目标重放到宠物脚侧（消除脱栖后与宠物重叠、目标 TOPMOST
# 盖住宠物导致的右键点找不到宠物根——M-18 run-151430 ⑦ 教训），再真实右键
# 菜单「Climb on Window」→ 等 climbPickActive/active。
function Enter-ClimbPick {
    if (-not (Place-TargetNearFoot)) { return @{ Ok = $false; Detail = 'target relocate to foot failed (no petFootPx)' } }
    $menuRes = Invoke-RealContextMenu 'Climb on Window'
    Write-Host "[M18] real-menu Climb on Window: $($menuRes.Detail)"
    if (-not $menuRes.Ok) { return @{ Ok = $false; Detail = "real menu Climb on Window failed: $($menuRes.Detail)" } }
    $stA = Wait-PickStatus @('active') 5
    if ($null -eq $stA) { return @{ Ok = $false; Detail = 'pick not active after real menu (no active status)' } }
    return @{ Ok = $true; Detail = $menuRes.Detail }
}

$script:probe = $null
$cover = $null
try {
    # 1. 启动目标并归属确认
    $targetProc = Start-Target
    Write-Host "[M18] target start issued pid=$($targetProc.Id)"
    $found = Find-OwnedTarget @()
    if ($null -eq $found) { throw 'no marker-owned target window appeared - aborting (user windows untouched)' }
    $hwnd = $found.Hwnd
    $script:ownedTargetHwnd = $found.Hwnd
    $script:ownedTargetPid = $found.Pid
    Start-Sleep -Milliseconds 1500
    if ((Get-Title $hwnd) -notlike "*$targetSubstr*") {
        throw "target title drifted off this run's marker - aborting (user windows untouched)"
    }
    Write-Host "[M18] target ownership verified: hwnd=$(HwndHex $hwnd) pid=$($found.Pid)"
    Add-Check '① 目标标记与归属确认（驱动侧零写入前提）' $true "hwnd=$(HwndHex $hwnd) pid=$($found.Pid) marker=$targetSubstr"

    # 2. 启动探针（真实宠物窗口）并等心跳
    $script:probe = Start-Process -FilePath $probeExe -ArgumentList "`"$runDir`"", 'window-climb-live', "`"$resultFile`"" -PassThru -WindowStyle Hidden
    Write-Host "[M18] probe started pid=$($script:probe.Id)"
    $stDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $stDeadline -and $null -eq (Get-State)) { Start-Sleep -Milliseconds 300 }
    if ($null -eq (Get-State)) { throw 'probe state file not appearing - aborting' }

    # 3. 贴脚放置目标 + 真实右键「Climb on Window」→ active
    if (-not (Place-TargetNearFoot)) { throw 'state missing for near-foot placement' }
    $idle0 = Ensure-IdleForClimb
    if (-not $idle0.Ok) { throw "pet busy at start: $($idle0.Detail)" }
    if (-not (Test-ProbeAlive)) { throw 'probe exited before entry' }
    $pick1 = Enter-ClimbPick
    Add-Check '② 真实右键「Climb on Window」→ climbPickActive=true status=active' $pick1.Ok $pick1.Detail
    if (-not $pick1.Ok) { throw 'first real-menu entry failed - aborting' }

    # 4. 真实点击合格近距目标 → started + climb-started(targetHwnd 匹配) + climbActive=true
    $tgt = Find-TargetClickPoint $hwnd
    if (-not $tgt.Ok) { Add-Check '③ 真实点击合格近距目标 → started+climb-started(targetHwnd 匹配)' $false $tgt.Detail; throw 'no target click point' }
    # 注入前拾取新鲜度门（≤2.5s 确认 climbPickActive）
    $gateOk = $false
    $gateDeadline = (Get-Date).AddMilliseconds(2500)
    while ((Get-Date) -lt $gateDeadline) {
        $gst = Get-State
        if ($null -ne $gst -and $gst.climbPickActive -and $gst.climbPickStatus -eq 'active') { $gateOk = $true; break }
        Start-Sleep -Milliseconds 150
    }
    if (-not $gateOk) { throw 'pre-inject gate: climb pick not active within 2.5s' }
    $baseMs3 = Get-LastEventMs
    $click3 = Invoke-RealLeftClick $tgt.Point $hwnd
    Start-Sleep -Milliseconds 300
    $started3 = Wait-Event 'climb-started' 8 $baseMs3
    $st3 = Get-State
    $ok3 = ($click3.Ok -and $null -ne $started3 -and $started3.targetHwnd -eq (HwndHex $hwnd) -and
        $null -ne $st3 -and $st3.climbActive -and -not $st3.climbPickActive -and $st3.climbPickStatus -eq 'started')
    Add-Check '③ 真实点击合格近距目标 → started+climb-started(targetHwnd 匹配)' $ok3 "click=$($click3.Ok) $($click3.Detail) startedEv=$($null -ne $started3) targetHwnd=$($started3.targetHwnd) climb=$($st3.climbActive) status=$($st3.climbPickStatus)"
    if (-not $ok3) {
        if ($st3.climbPickStatus -eq 'rejected-occluded') {
            $occ = Get-LandingOccluder $hwnd
            Write-Host "[M18] ③ landing occluded - occluders at y=$(($null -eq (Get-FrameBounds $hwnd)) ? 'n/a' : ((Get-FrameBounds $hwnd).T + 2)): $occ"
            Write-Host "[M18] ③ ENV: minimize or move covering windows above, then rerun"
        }
        throw 'first climb pick click failed - aborting'
    }

    # 5. 路线推进：climb-step ≥3 且 fraction 递增
    $steps = @(Read-Events | Where-Object { $_.type -eq 'climb-step' -and $_.tMs -gt $baseMs3 })
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and $steps.Count -lt 3) {
        Start-Sleep -Milliseconds 300
        $steps = @(Read-Events | Where-Object { $_.type -eq 'climb-step' -and $_.tMs -gt $baseMs3 })
    }
    $fracInc = $true
    for ($i = 1; $i -lt $steps.Count; $i++) {
        if ($steps[$i].fraction -le $steps[$i - 1].fraction) { $fracInc = $false; break }
    }
    Add-Check '④ 路线推进：climb-step≥3 且 fraction 递增' ($steps.Count -ge 3 -and $fracInc) "steps=$($steps.Count) fracInc=$fracInc fractions=[$(@($steps | ForEach-Object { $_.fraction }) -join ',')]"

    # 6. 顶部交接：climb-completed 脚点=上缘中点±6、perchActive、无跳动
    $completed = Wait-Event 'climb-completed' 20 $baseMs3
    $compOk = $false; $compDetail = 'no climb-completed'
    $fb1 = Get-FrameBounds $hwnd
    if ($null -ne $completed -and $null -ne $fb1) {
        $expX = ($fb1.L + $fb1.R) / 2.0; $expY = $fb1.T
        $dx = [Math]::Abs($completed.foot[0] - $expX); $dy = [Math]::Abs($completed.foot[1] - $expY)
        Start-Sleep -Milliseconds 800
        $stComp = Get-State
        $noJump = ($null -ne $stComp -and
            [Math]::Abs($stComp.petFootPx[0] - $completed.foot[0]) -le 4 -and
            [Math]::Abs($stComp.petFootPx[1] - $completed.foot[1]) -le 4)
        $compOk = ($dx -le 6 -and $dy -le 6 -and $stComp.perchActive -and $noJump -and -not $stComp.climbActive)
        $compDetail = "foot=[$($completed.foot -join ',')] expected=[$([Math]::Round($expX,1)),$expY] dx=$dx dy=$dy perch=$($stComp.perchActive) climb=$($stComp.climbActive) noJump=$noJump"
    }
    Add-Check '⑤ 顶部交接：climb-completed 脚点=上缘中点±6 perchActive 无跳动' $compOk $compDetail
    if (-not $compOk) { throw 'climb handoff failed - aborting' }

    # 7. 交接后跟随：移动目标 → 脚点贴新上缘（perchActive 保持）
    $r3 = New-Object M15W+RECT
    [M15W]::GetWindowRect($hwnd, [ref]$r3) | Out-Null
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, ($r3.L + 100), ($r3.T + 50), 800, 500, 0x0004) | Out-Null
    $followOk = $false; $followDetail = 'state missing'
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        $stF = Get-State
        $fbF = Get-FrameBounds $hwnd
        if ($null -ne $stF -and $null -ne $fbF) {
            $expX = ($fbF.L + $fbF.R) / 2.0; $expY = $fbF.T
            if ([Math]::Abs($stF.petFootPx[0] - $expX) -le 6 -and [Math]::Abs($stF.petFootPx[1] - $expY) -le 6 -and $stF.perchActive) {
                $followOk = $true
                $followDetail = "foot=[$($stF.petFootPx -join ',')] newEdge=[$([Math]::Round($expX,1)),$expY] perch=$($stF.perchActive)"
                break
            }
            $followDetail = "foot=[$($stF.petFootPx -join ',')] expected=[$([Math]::Round($expX,1)),$expY] perch=$($stF.perchActive)"
        }
        Start-Sleep -Milliseconds 250
    }
    Add-Check '⑥ 交接后跟随：移动目标后脚点贴新上缘' $followOk $followDetail

    # 8. 平行入口回归：真实右键「Perch on Window」→ 点击目标 → selected + perchActive、
    #    climbActive=false（攀爬不触发）。先脱离当前栖息。
    $idle8 = Ensure-IdleForClimb
    if (-not $idle8.Ok) { Add-Check '⑦ 平行入口「Perch on Window」→ selected+perchActive 攀爬不触发' $false "idle: $($idle8.Detail)"; throw 'cannot free pet for parallel entry' }
    # 脱栖后重放目标到脚侧，避免与宠物重叠挡住右键点（M-18 run-151430 ⑦ 教训）。
    if (-not (Place-TargetNearFoot)) { Add-Check '⑦ 平行入口「Perch on Window」→ selected+perchActive 攀爬不触发' $false 'target relocate failed (no petFootPx)'; throw 'cannot relocate target for parallel entry' }
    $menuP = Invoke-RealContextMenu 'Perch on Window'
    Write-Host "[M18] real-menu Perch on Window: $($menuP.Detail)"
    $stP = Wait-PickStatus @('active') 5
    $parallelOk = $false; $parallelDetail = $menuP.Detail
    if ($menuP.Ok -and $null -ne $stP) {
        $tgtP = Find-TargetClickPoint $hwnd
        if ($tgtP.Ok) {
            $gateOk = $false
            $gateDeadline = (Get-Date).AddMilliseconds(2500)
            while ((Get-Date) -lt $gateDeadline) {
                $gst = Get-State
                if ($null -ne $gst -and $gst.climbPickStatus -eq 'active') { $gateOk = $true; break }
                Start-Sleep -Milliseconds 150
            }
            if ($gateOk) {
                $baseMs7 = Get-LastEventMs
                $clickP = Invoke-RealLeftClick $tgtP.Point $hwnd
                $stSel = Wait-PickStatus @('selected') 8
                Start-Sleep -Milliseconds 400
                $stAfterP = Get-State
                $parallelOk = ($clickP.Ok -and $null -ne $stSel -and $null -ne $stAfterP -and
                    $stAfterP.perchActive -and -not $stAfterP.climbActive -and
                    @(Read-Events | Where-Object { $_.type -eq 'climb-started' -and $_.tMs -gt $baseMs7 }).Count -eq 0)
                $parallelDetail = "menu=$($menuP.Ok) click=$($clickP.Ok) $($clickP.Detail) status=$($stSel.climbPickStatus) perch=$($stAfterP.perchActive) climb=$($stAfterP.climbActive)"
            } else { $parallelDetail = "parallel pick not active within gate" }
        } else { $parallelDetail = "parallel click target: $($tgtP.Detail)" }
    }
    Add-Check '⑦ 平行入口「Perch on Window」→ selected+perchActive 攀爬不触发' $parallelOk $parallelDetail
    if (-not $parallelOk) { throw 'parallel perch entry failed - aborting' }

    # 8(续). 拒绝：真实点击桌面/壳点 → rejected-ineligible，宠物不动
    $idle9 = Ensure-IdleForClimb
    $pick9 = Enter-ClimbPick
    if ($idle9.Ok -and $pick9.Ok) {
        # 找 Shell 桌面/任务栏可见点（KnownShellClasses 依 ExternalWindowEligibility.cs）
        $shellPoint = $null; $shellRoot = [IntPtr]::Zero; $shellDetail = 'no shell point found'
        foreach ($cls in 'Progman', 'WorkerW', 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd') {
            foreach ($h64 in (Get-Tops)) {
                $h = [IntPtr]$h64
                if ((Get-Class $h) -ne $cls) { continue }
                $hr = New-Object M15W+RECT
                [M15W]::GetWindowRect($h, [ref]$hr) | Out-Null
                # 扫描窗口内若干候选点：WindowFromPoint 根==该壳窗口 且非宠物/目标
                foreach ($fy in 0.5, 0.7, 0.3, 0.85) {
                    foreach ($fx in 0.3, 0.5, 0.7, 0.25, 0.75) {
                        $sx = $hr.L + [int](($hr.R - $hr.L) * $fx)
                        $sy = $hr.T + [int](($hr.B - $hr.T) * $fy)
                        if (-not (Test-PointOnScreen $sx $sy)) { continue }
                        $pt0 = New-Object M15Hit+POINT
                        $pt0.X = $sx; $pt0.Y = $sy
                        $root0 = [M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($pt0), 2)
                        if ($root0 -eq $h -and $root0 -ne $hwnd -and $root0 -ne (Find-PetHwnd)) {
                            $shellPoint = @($sx, $sy); $shellRoot = $root0
                            $shellDetail = "class=$cls root=0x$($root0.ToString('X')) point=[$sx,$sy]"
                            break
                        }
                    }
                    if ($shellPoint) { break }
                }
                if ($shellPoint) { break }
            }
            if ($shellPoint) { break }
        }
        if ($shellPoint) {
            $st9Before = Get-State
            $baseMs9 = Get-LastEventMs
            $click9 = Invoke-RealLeftClick $shellPoint $shellRoot 300
            $st9 = Wait-PickStatus @('rejected-ineligible', 'cancelled-self', 'cancelled-timeout', 'cancelled', 'started') 8
            Start-Sleep -Milliseconds 600
            $st9After = Get-State
            $noMove9 = ($null -ne $st9Before -and $null -ne $st9After -and
                [Math]::Abs($st9After.petFootPx[0] - $st9Before.petFootPx[0]) -le 2 -and
                [Math]::Abs($st9After.petFootPx[1] - $st9Before.petFootPx[1]) -le 2)
            $ok9 = ($click9.Ok -and $null -ne $st9 -and $st9.climbPickStatus -eq 'rejected-ineligible' -and
                -not $st9After.climbActive -and $noMove9)
            Add-Check '⑧ 拒绝：点击桌面/壳 → rejected-ineligible 宠物不动' $ok9 "click=$($click9.Ok) $($click9.Detail) shell=$shellDetail status=$($st9.climbPickStatus) climb=$($st9After.climbActive) noMove=$noMove9"
        } else {
            Add-Check '⑧ 拒绝：点击桌面/壳 → rejected-ineligible 宠物不动' $false "未执行：找不到 Shell 桌面/任务栏可见点（$shellDetail）；需人工确认环境后补测"
        }
    } else {
        Add-Check '⑧ 拒绝：点击桌面/壳 → rejected-ineligible 宠物不动' $false "idle=$($idle9.Ok) entry=$($pick9.Ok)"
    }

    # 9. 拒绝：拾取中把目标移至 >200px → 点击 → rejected-too-far，宠物不动
    # ⑧ 点击任务栏可能弹出系统「搜索」面板抢占前台（run-151846 ⑨ 教训）：
    # 右键菜单要求前台=宠物窗口，被系统 UI 占用会 no menu。进入前探测并关闭
    # 残留系统面板，入口失败且前台是系统面板时清理后重试一次。
    $pick10 = $null
    for ($entryTry = 1; $entryTry -le 2 -and $null -eq $pick10; $entryTry++) {
        $fgTitle9 = Get-Title ([M15W2]::GetForegroundWindow())
        if ($fgTitle9 -eq '搜索' -or $fgTitle9 -eq 'Search') {
            Write-Host "[M18] ⑨ env: closing system UI '$fgTitle9' (entryTry $entryTry)"
            [M16K]::Esc()
            Start-Sleep -Milliseconds 800
            [M15W]::SetForegroundWindow($hwnd) | Out-Null
            Start-Sleep -Milliseconds 400
        }
        if ($null -eq $pick10) { $pick10 = Enter-ClimbPick; if (-not $pick10.Ok -and $entryTry -lt 2) { Write-Host "[M18] ⑨ entry try $entryTry failed: $($pick10.Detail); will retry after env cleanup" } }
    }
    if ($pick10.Ok) {
        $st10Before = Get-State
        $fb10 = Get-FrameBounds $hwnd
        $sb10 = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $fx10 = [int]$st10Before.petFootPx[0]
        $farX = $(if ($fx10 -lt ($sb10.Left + $sb10.Width / 2)) { $sb10.Right - 820 } else { $sb10.Left + 20 })
        [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, $farX, 400, 800, 500, 0x0004) | Out-Null
        Raise-Top $hwnd
        Start-Sleep -Milliseconds 600
        $fb10b = Get-FrameBounds $hwnd
        $dist10 = Get-FootToFrameDistance @([int]$st10Before.petFootPx[0], [int]$st10Before.petFootPx[1]) $fb10b
        $tgt10 = Find-TargetClickPoint $hwnd
        if ($tgt10.Ok -and $dist10 -gt 200) {
            $baseMs10 = Get-LastEventMs
            $click10 = Invoke-RealLeftClick $tgt10.Point $hwnd
            $st10 = Wait-PickStatus @('rejected-too-far', 'rejected-ineligible', 'cancelled-timeout', 'cancelled', 'started') 8
            Start-Sleep -Milliseconds 600
            $st10After = Get-State
            $noMove10 = ($null -ne $st10After -and
                [Math]::Abs($st10After.petFootPx[0] - $st10Before.petFootPx[0]) -le 2 -and
                [Math]::Abs($st10After.petFootPx[1] - $st10Before.petFootPx[1]) -le 2)
            $ok10 = ($click10.Ok -and $null -ne $st10 -and $st10.climbPickStatus -eq 'rejected-too-far' -and
                -not $st10After.climbActive -and $noMove10)
            Add-Check '⑨ 拒绝：超距目标 → rejected-too-far 宠物不动' $ok10 "click=$($click10.Ok) $($click10.Detail) distance=$([Math]::Round($dist10,1))px(>200) status=$($st10.climbPickStatus) climb=$($st10After.climbActive) noMove=$noMove10"
        } else {
            Add-Check '⑨ 拒绝：超距目标 → rejected-too-far 宠物不动' $false "tgt=$($tgt10.Ok) distance=$([Math]::Round($dist10,1))px"
        }
    } else {
        Add-Check '⑨ 拒绝：超距目标 → rejected-too-far 宠物不动' $false "entry: $($pick10.Detail)"
    }
    # 目标回归贴脚，供后续场景使用
    Place-TargetNearFoot | Out-Null

    # 10. 取消：拾取中点击宠物自身 → 不进入攀爬
    # 真实输入语义：左键点宠物先置 _isPettingCandidate → IsPerchWriteAllowed=false →
    # UpdatePerchPick 兜底 EndPerchPick("cancelled")（MainWindow.xaml.cs:2515-2519），先于
    # 「点自身→cancelled-self」分支（2556-2563）；cancelled-self 仅测试缝可达（ClimbEntryTests.cs:184）。
    # 断言接受 cancelled / cancelled-self 等价集合，detail 如实保留实际终态。
    $pick11 = Enter-ClimbPick
    if ($pick11.Ok) {
        $petHwnd11 = Find-PetHwnd
        $st11Before = Get-State
        $petW = $st11Before.petRectPx[2] - $st11Before.petRectPx[0]; $petH = $st11Before.petRectPx[3] - $st11Before.petRectPx[1]
        $selfPt = $null
        foreach ($fy in 0.5, 0.4, 0.6, 0.3, 0.7) {
            foreach ($fx in 0.5, 0.4, 0.6, 0.3, 0.7) {
                $sx = $st11Before.petRectPx[0] + [int]($petW * $fx)
                $sy = $st11Before.petRectPx[1] + [int]($petH * $fy)
                if (-not (Test-PointOnScreen $sx $sy)) { continue }
                $pt0 = New-Object M15Hit+POINT
                $pt0.X = $sx; $pt0.Y = $sy
                if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($pt0), 2) -eq $petHwnd11) { $selfPt = @($sx, $sy); break }
            }
            if ($selfPt) { break }
        }
        if ($selfPt) {
            $click11 = Invoke-RealLeftClick $selfPt $petHwnd11 300
            $st11 = Wait-PickStatus @('cancelled-self', 'cancelled', 'cancelled-timeout', 'started') 8
            Start-Sleep -Milliseconds 500
            $st11After = Get-State
            $ok11 = ($click11.Ok -and $null -ne $st11 -and @('cancelled-self', 'cancelled') -contains $st11.climbPickStatus -and
                -not $st11After.climbActive -and -not $st11After.perchActive)
            Add-Check '⑩ 取消：点击宠物自身 → cancelled-self' $ok11 "click=$($click11.Ok) $($click11.Detail) status=$($st11.climbPickStatus) climb=$($st11After.climbActive) perch=$($st11After.perchActive)"
        } else {
            Add-Check '⑩ 取消：点击宠物自身 → cancelled-self' $false '未找到宠物可见点击点'
        }
    } else {
        Add-Check '⑩ 取消：点击宠物自身 → cancelled-self' $false "entry: $($pick11.Detail)"
    }

    # 11. 取消：拾取保持 15s 真实超时 → cancelled-timeout，宠物不动
    $pick12 = Enter-ClimbPick
    if ($pick12.Ok) {
        $st12Before = Get-State
        $st12 = Wait-PickStatus @('cancelled-timeout') 18
        Start-Sleep -Milliseconds 500
        $st12After = Get-State
        $noMove12 = ($null -ne $st12After -and
            [Math]::Abs($st12After.petFootPx[0] - $st12Before.petFootPx[0]) -le 2 -and
            [Math]::Abs($st12After.petFootPx[1] - $st12Before.petFootPx[1]) -le 2)
        $ok12 = ($null -ne $st12 -and -not $st12After.climbActive -and -not $st12After.perchActive -and $noMove12)
        Add-Check '⑪ 取消：15s 真实超时 → cancelled-timeout 宠物不动' $ok12 "status=$($st12.climbPickStatus) climb=$($st12After.climbActive) perch=$($st12After.perchActive) noMove=$noMove12"
    } else {
        Add-Check '⑪ 取消：15s 真实超时 → cancelled-timeout 宠物不动' $false "entry: $($pick12.Detail)"
    }

    # 12. 取消：拾取中打开真实菜单 → cancelled（EnterContextMenuMode 先取消拾取），Esc 关菜单
    $pick13 = Enter-ClimbPick
    if ($pick13.Ok) {
        # 真实右键（注入 RIGHTDOWN/UP）→ 生产 EnterContextMenuMode 先 EndPerchPick("cancelled")
        $petHwnd13 = Find-PetHwnd
        $st13b = Get-State
        $petW13 = $st13b.petRectPx[2] - $st13b.petRectPx[0]; $petH13 = $st13b.petRectPx[3] - $st13b.petRectPx[1]
        $rcPt = $null
        foreach ($fy in 0.5, 0.4, 0.6, 0.3, 0.7) {
            foreach ($fx in 0.5, 0.4, 0.6, 0.3, 0.7) {
                $sx = $st13b.petRectPx[0] + [int]($petW13 * $fx)
                $sy = $st13b.petRectPx[1] + [int]($petH13 * $fy)
                if (-not (Test-PointOnScreen $sx $sy)) { continue }
                $pt0 = New-Object M15Hit+POINT
                $pt0.X = $sx; $pt0.Y = $sy
                if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($pt0), 2) -eq $petHwnd13) { $rcPt = @($sx, $sy); break }
            }
            if ($rcPt) { break }
        }
        if ($rcPt) {
            [M15W2]::SetCursorPos($rcPt[0], $rcPt[1]) | Out-Null
            [M15W2]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 120
            [M15W2]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
            $st13 = Wait-PickStatus @('cancelled', 'cancelled-self') 8
            Start-Sleep -Milliseconds 400
            # 菜单此刻应已打开（EnterContextMenuMode 在 ShowNativeContextMenu 先执行）→ Esc 关闭
            $menuOpen13 = (Find-ProbeMenuHwnd) -ne [IntPtr]::Zero
            if ($menuOpen13) { [M16K]::Esc(); Start-Sleep -Milliseconds 500 }
            $menuClosed13 = (Find-ProbeMenuHwnd) -eq [IntPtr]::Zero
            $ok13 = ($null -ne $st13 -and $st13.climbPickStatus -eq 'cancelled' -and $menuOpen13 -and $menuClosed13 -and
                -not (Get-State).climbActive -and -not (Get-State).perchActive)
            Add-Check '⑫ 取消：拾取中打开真实菜单 → cancelled 且菜单可正常关闭' $ok13 "status=$($st13.climbPickStatus) menuOpened=$menuOpen13 menuClosedAfterEsc=$menuClosed13"
        } else {
            Add-Check '⑫ 取消：拾取中打开真实菜单 → cancelled 且菜单可正常关闭' $false '未找到宠物可见右键点'
        }
    } else {
        Add-Check '⑫ 取消：拾取中打开真实菜单 → cancelled 且菜单可正常关闭' $false "entry: $($pick13.Detail)"
    }

    # 13. 取消：拾取中暂停 → cancelled，resume 后不续接
    $pick14 = Enter-ClimbPick
    if ($pick14.Ok) {
        Send-Command 'pause'
        $st14 = Wait-PickStatus @('cancelled') 8
        Start-Sleep -Milliseconds 500
        Send-Command 'resume'
        Start-Sleep -Milliseconds 1500
        $st14After = Get-State
        $ok14 = ($null -ne $st14 -and $st14.climbPickStatus -eq 'cancelled' -and
            $null -ne $st14After -and -not $st14After.climbPickActive -and
            -not $st14After.climbActive -and -not $st14After.perchActive)
        Add-Check '⑬ 取消：拾取中暂停 → cancelled resume 后不续接' $ok14 "status=$($st14.climbPickStatus) pickActive=$($st14After.climbPickActive) climb=$($st14After.climbActive) perch=$($st14After.perchActive)"
    } else {
        Add-Check '⑬ 取消：拾取中暂停 → cancelled resume 后不续接' $false "entry: $($pick14.Detail)"
    }

    # 14. 取消：拾取中真实拖动 → cancelled（拖动接管，回空闲）
    $pick15 = Enter-ClimbPick
    if ($pick15.Ok) {
        $drag15 = Invoke-RealDrag 12 8 6 700
        $st15 = Wait-PickStatus @('cancelled', 'cancelled-self', 'cancelled-timeout') 10
        Start-Sleep -Milliseconds 600
        $st15After = Get-State
        $ok15 = ($drag15.Ok -and $null -ne $st15 -and $st15.climbPickStatus -eq 'cancelled' -and
            $null -ne $st15After -and -not $st15After.climbActive -and -not $st15After.perchActive -and -not $st15After.isDragging)
        Add-Check '⑭ 取消：拾取中真实拖动 → cancelled 回空闲' $ok15 "$($drag15.Detail) status=$($st15.climbPickStatus) drag=$($st15After.isDragging) climb=$($st15After.climbActive) perch=$($st15After.perchActive)"
    } else {
        Add-Check '⑭ 取消：拾取中真实拖动 → cancelled 回空闲' $false "entry: $($pick15.Detail)"
    }

    # 15. 既有接管不回归：攀爬中真实拖动 → climb-drag-preempted，松手后不回拉
    $idle16 = Ensure-IdleForClimb
    $pick16 = Enter-ClimbPick
    $dragRouteOk = $false; $dragRouteDetail = 'attempts exhausted'
    if ($idle16.Ok -and $pick16.Ok) {
        $tgt16 = Find-TargetClickPoint $hwnd
        if ($tgt16.Ok) {
            $gateOk = $false
            $gateDeadline = (Get-Date).AddMilliseconds(2500)
            while ((Get-Date) -lt $gateDeadline) {
                $gst = Get-State
                if ($null -ne $gst -and $gst.climbPickActive -and $gst.climbPickStatus -eq 'active') { $gateOk = $true; break }
                Start-Sleep -Milliseconds 150
            }
            if ($gateOk) {
                $baseMs16 = Get-LastEventMs
                $click16 = Invoke-RealLeftClick $tgt16.Point $hwnd
                $started16 = Wait-Event 'climb-started' 8 $baseMs16
                if ($null -ne $started16) {
                    $drag16 = Invoke-RealDrag 12 8 6 700 20
                    $preempt16 = Wait-Event 'climb-drag-preempted' 8 $baseMs16
                    Start-Sleep -Milliseconds 600
                    $st16 = Get-State
                    if ($null -ne $preempt16) {
                        $r6 = New-Object M15W+RECT
                        [M15W]::GetWindowRect($hwnd, [ref]$r6) | Out-Null
                        [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, ($r6.L + 150), ($r6.T + 80), 800, 500, 0x0004) | Out-Null
                        Start-Sleep -Milliseconds 1500
                        $st16After = Get-State
                        $noPull16 = ($null -ne $st16After -and -not $st16After.climbActive -and -not $st16After.perchActive -and
                            [Math]::Abs($st16After.petFootPx[0] - $st16.petFootPx[0]) -le 2 -and
                            [Math]::Abs($st16After.petFootPx[1] - $st16.petFootPx[1]) -le 2)
                        $dragRouteOk = ($drag16.Ok -and $noPull16)
                        $dragRouteDetail = "preempted=$($null -ne $preempt16) $($drag16.Detail) stayAtDrop=$noPull16 attempt=1"
                    } else {
                        $dragRouteDetail = "no climb-drag-preempted (drag=$($drag16.Ok) $($drag16.Detail))"
                    }
                } else {
                    $dragRouteDetail = "no climb-started after click ($($click16.Detail))"
                }
            } else { $dragRouteDetail = 'pick not active within gate' }
        } else { $dragRouteDetail = "click target: $($tgt16.Detail)" }
    } else {
        $dragRouteDetail = "idle=$($idle16.Ok) entry=$($pick16.Ok)"
    }
    Add-Check '⑮ 攀爬中真实拖动 → climb-drag-preempted 松手后不回拉' $dragRouteOk $dragRouteDetail

    # 16. 结束
    Send-Command 'quit'
    if (-not $script:probe.WaitForExit(30 * 1000)) { Stop-Process -Id $script:probe.Id -Force }
    Write-Host "[M18] probe exited code=$($script:probe.ExitCode)"
    $res = $(if (Test-Path $resultFile) { Get-Content $resultFile -Raw | ConvertFrom-Json } else { $null })
    $script:reachedEnd = $true
    Add-Check '⑯ 探针 exitCode=0' ($script:probe.ExitCode -eq 0 -and $null -ne $res -and $res.exitCode -eq 0) "probe exit=$($script:probe.ExitCode) result exitCode=$($res.exitCode)"
}
catch {
    $script:failed = $true
    Write-Host "[M18] FATAL: $($_.Exception.GetType().Name): $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
    Write-Host "[M18] FATAL position: $($_.InvocationInfo.PositionMessage)"
    throw
}
finally {
    if ($cover) { $cover.Dispose() }
    if ($script:probe -and -not $script:probe.HasExited) { Stop-Process -Id $script:probe.Id -Force -ErrorAction SilentlyContinue }
    Invoke-CleanupOwnedTarget
    if (Test-Path -LiteralPath $script:markerFile) { Remove-Item -LiteralPath $script:markerFile -Force }
    Start-Sleep -Milliseconds 300
    # 诱饵存活验证：其他轮标记文件/标记窗口/无窗口进程不得被清理/移动
    $lureFileAlive = $false
    if (Test-Path -LiteralPath $script:lureFile) {
        $lureContent = Get-Content -LiteralPath $script:lureFile -Raw -ErrorAction SilentlyContinue
        $lureFileAlive = ($null -ne $lureContent -and
            $lureContent.Trim() -eq 'P5B-002 other-run marker lure (must survive this run)')
    }
    $lureWinAlive = $false; $lureWinDetail = 'no lure window'
    if ($null -ne $script:lureWindow -and [M15W2]::IsWindow($script:lureWindow.Handle)) {
        $lr = New-Object M15W+RECT
        [M15W]::GetWindowRect($script:lureWindow.Handle, [ref]$lr) | Out-Null
        $lureTitleNow = Get-Title $script:lureWindow.Handle
        $lureIconic = [M15W2]::IsIconic($script:lureWindow.Handle)
        $lureWinAlive = ($lureTitleNow -eq $script:lureSnapshot.Text -and
            $lr.L -eq $script:lureSnapshot.L -and $lr.T -eq $script:lureSnapshot.T -and
            $lr.R -eq $script:lureSnapshot.R -and $lr.B -eq $script:lureSnapshot.B)
        $lureWinDetail = "title='$lureTitleNow' rect=[$($lr.L),$($lr.T),$($lr.R),$($lr.B)] snapshot=[$($script:lureSnapshot.L),$($script:lureSnapshot.T),$($script:lureSnapshot.R),$($script:lureSnapshot.B)] iconic=$lureIconic"
    }
    $lureProcAlive = ($null -ne $script:windowlessLure -and -not $script:windowlessLure.HasExited)
    Add-Check '⑰ 诱饵存活（其他轮标记文件/标记窗口/无窗口进程未被越界清理）' ($lureFileAlive -and $lureWinAlive -and $lureProcAlive) "lureFile=$lureFileAlive lureWin=$lureWinAlive $lureWinDetail windowless=$lureProcAlive pid=$($script:windowlessLure.Id)"
    if ($script:lureWindow) { $script:lureWindow.Dispose() }
    if ($null -ne $script:windowlessLure -and -not $script:windowlessLure.HasExited) {
        Stop-Process -Id $script:windowlessLure.Id -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $script:lureDir) { Remove-Item -LiteralPath $script:lureDir -Recurse -Force -ErrorAction SilentlyContinue }
    if ($script:checks.Count -eq 0) {
        Write-Host "[M18] ABORTED before any check - no checklist"
        Stop-Transcript | Out-Null
        exit 1
    }
    $expectCount = 16
    if ($script:checks.Count -lt $expectCount) {
        # 前 16 项行为/证据未走完（提前失败/中止）→ 拒绝把不完整清单当完整
        Write-Host "[M18] FAILED: checklist incomplete ($($script:checks.Count)/$expectCount) - marked FAIL"
        Add-Check '⑱ 完整流程走完（固定条目数）' $false "$($script:checks.Count)/$expectCount"
    } else {
        Add-Check '⑱ 完整流程走完（固定条目数）' (Test-M18Complete $script:checks ([bool]$script:reachedEnd) ([bool]$script:failed)) "①-⑰ 全部唯一 PASS + reachedEnd 且未 failed（合计 18 项含本条）"
    }
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Set-Content (Join-Path $runDir 'm18-checklist.txt')
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $allPass = -not ($script:checks | Where-Object { $_.Result -eq 'FAIL' })
    Write-Host "[M18] checklist: $(if ($allPass) { 'ALL PASS' } else { 'HAS FAIL' }) -> $runDir"
    Stop-Transcript | Out-Null
    if (-not $allPass) { exit 1 }
}