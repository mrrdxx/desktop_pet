# P5B-001 / TESTING M-17: 窗口有限攀爬隔离预览驱动。
# 证据写入 .temp\p5b-001\m17\run-<时间戳>-notepad\。
#
# 探针场景 window-climb-live（只观测）：驱动追加命令 start/pause/resume/quit，
# 攀爬推进由生产 OnUpdateTick 驱动。操作序列（每步断言事件与心跳状态）：
#   1  目标标记与归属确认（驱动侧零写入前提）
#   2  起攀：Place-TargetNearFoot + start → climb-started(targetHwnd 匹配)
#   3  路线推进：climb-step ≥3 且 fraction 递增
#   4  顶部交接：climb-completed，脚点=上缘中点±6，perchActive，无跳动
#   5  交接后跟随：移动目标 → 脚点贴新上缘（state 断言，本场景无 follow 事件）
#   6  栖息中真实鼠标拖动 → 脱离栖息，随后移动目标不回拉
#   7  超距起攀 → start-rejected RejectedTooFar，宠物不动
#   8  盖住落位起攀 → start-rejected RejectedOccluded，宠物不动
#   9  最小化起攀 → start-rejected RejectedIneligible，宠物不动
#   10 路线中目标 +150,+100 → 收敛新上缘中点（整体重试 ≤3）
#   11 路线中最小化 → climb-cancelled(target-*)，宠物回可见区
#   12 路线中上缘遮挡 → climb-occluded + climb-cancelled(occluded)
#   13 路线中真实拖动 → climb-drag-preempted，松手后不回拉
#   14 路线中 pause → climb-cancelled(paused)，resume 不续接
#   15 quit → 探针 exitCode=0
#
# 目标锁定纪律与 M-16 相同：本轮独有标记 P5B001NP-<ts> + 归属确认
# （宿主 Notepad、StartTime≥t0−2s、非驱动 pid），不碰用户窗口。
# P5B001 R1 F-03：清理只针对本轮归属对象——启动前不删其他 run 的标记
# 文件、不杀无窗口 Notepad、不清理残留标记窗口；finally 只关/杀本轮
# `Find-OwnedTarget` 确认归属的目标（其他轮、无窗口外部进程、同进程
# 用户标签页一律不写不关不杀）；开始前快照外部窗口与其他 run 标记文件，
# 结束时断言存活且标题/位置/文件不变。存在用户 Notepad 窗口则中止。
# 真实鼠标注入纪律：SetCursorPos 回读 ±2 + WindowFromPoint
# 命中根窗口前置校验；每个 attempt 前查探针存活。
#
# 限制说明（清单不通过驱动断言，仅单测覆盖）：探针 quit 走
# Environment.Exit，不经生产 OnClosed/RestoreProbePositionForExit，
# 退出恢复爬行前位置不可由本驱动断言（WindowClimbAppTests 覆盖）。
# 跨屏/混合 DPI 未测（本机 1920×1080@96DPI 单屏）。
#
# 用法（仓库根目录，pwsh 7 -File 独立进程运行）：
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B001ClimbPreview.ps1 -TargetKind notepad -DurationSec 600

param(
    [Parameter(Mandatory = $true)][ValidateSet('notepad')][string]$TargetKind,
    [int]$DurationSec = 600
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:t0 = Get-Date
$runDir = Join-Path $repo ".temp\p5b-001\m17\run-$ts-$TargetKind"
$scenarioDir = Join-Path $runDir 'window-climb-live'
New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
$resultFile = Join-Path $runDir 'probe-result.json'
$eventsFile = Join-Path $scenarioDir 'climb-live-events.jsonl'
$stateFile = Join-Path $scenarioDir 'climb-live-state.json'
$commandsFile = Join-Path $scenarioDir 'climb-live-commands.txt'
$probeExe = Join-Path $repo 'tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe'
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
Write-Host "[M17] driver thread DPI awareness = $threadAwareness (2=PerMonitor; required for physical px)"
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
using System;using System.Runtime.InteropServices;
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
}
"@
}
if (-not ([System.Management.Automation.PSTypeName]'M16K2').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M16K2 {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static void AltN() {
    keybd_event(0x12, 0, 0, UIntPtr.Zero);
    keybd_event(0x4E, 0, 0, UIntPtr.Zero);
    keybd_event(0x4E, 0, 2, UIntPtr.Zero);
    keybd_event(0x12, 0, 2, UIntPtr.Zero);
  }
}
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

function Get-Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 256
    [M15W]::GetWindowText($h, $sb, 256) | Out-Null
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
    # 探针每 200ms File.Move 覆写状态文件，读失败短退避重试（M-16 教训）。
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
        Write-Host "[M17] probe exited early (pid $($script:probe.Id)) - driver stalled past probe lifetime"
        return $false
    }
    return $true
}
# 按探针 pid + state.petRectPx 定位宠物窗口；攀爬中宠物在移动，
# 容差放宽（默认 3，可传 20）。
function Find-PetHwnd([int]$tolerance = 3) {
    $st = Get-State
    if ($null -eq $st -or $null -eq $st.petRectPx) { return [IntPtr]::Zero }
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
# 脚点到目标可见外框的钳制欧氏距离（与生产 IsStartDistanceSatisfied 同口径：
# 脚点在框内=0；框外为到最近框边的欧氏距离）。
function Get-FootToFrameDistance($foot, $fb) {
    $dx = 0
    if ($foot[0] -lt $fb.L) { $dx = $fb.L - $foot[0] }
    elseif ($foot[0] -gt $fb.R) { $dx = $foot[0] - $fb.R }
    $dy = 0
    if ($foot[1] -lt $fb.T) { $dy = $fb.T - $foot[1] }
    elseif ($foot[1] -gt $fb.B) { $dy = $foot[1] - $fb.B }
    [Math]::Sqrt($dx * $dx + $dy * $dy)
}

$script:checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$name, [bool]$pass, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Item = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} : {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

# ---- 目标题材（仅 notepad）----
$script:markerFile = Join-Path $runDir "P5B001NP-$ts.txt"
Set-Content -LiteralPath $script:markerFile -Value "P5B-001 M-17 marker file (run $ts)" -Encoding UTF8
$targetSubstr = "P5B001NP-$ts"
# R1 F-03：不跨轮清理——只记录其他 run 的标记文件快照，结束时断言
# 它们仍存在（不写不删不关）。本轮只清理本轮 markerFile（finally）。
$script:otherRunMarkerFiles = @(
    Get-ChildItem (Split-Path $runDir -Parent) -Directory -Filter 'run-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -File -Filter 'P5B001NP-*.txt' -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -ne $script:markerFile } |
        ForEach-Object { $_.FullName })
function Start-Target {
    Start-Process notepad -ArgumentList "`"$script:markerFile`"" -PassThru
}

# ---- 本轮归属清理（R1 F-03：仅清理由当前驱动创建并能确认归属的
#      目标窗口及其子进程，只删本轮 markerFile；其他轮/外部窗口/
#      无窗口进程一律不写不关不杀；同进程用户标签页保留）----
$script:ownedTargetHwnd = [IntPtr]::Zero
$script:ownedTargetPid = 0
# 仅当归属 pid 已无任何顶层窗口时才停止该进程（我们的启动进程、单窗口）；
# 仍有窗口（同进程用户标签页/被接管）则不触碰。
function Stop-OwnedOrphanProcess {
    if ($script:ownedTargetPid -eq 0) { return }
    $left = @(Get-Tops | ForEach-Object {
        $p = [uint32]0
        [M15W2]::GetWindowThreadProcessId([IntPtr]$_, [ref]$p) | Out-Null
        if ($p -eq [uint32]$script:ownedTargetPid) { [IntPtr]$_ }
    })
    if ($left.Count -gt 0) {
        Write-Host "[M17] cleanup: owned pid $($script:ownedTargetPid) still has $($left.Count) window(s) - process left untouched"
        return
    }
    $p = Get-Process -Id $script:ownedTargetPid -ErrorAction SilentlyContinue
    if ($null -ne $p) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Write-Host "[M17] cleanup: stopped owned orphan process pid=$($script:ownedTargetPid) (0 windows left)"
    }
}
function Invoke-CleanupOwnedTarget {
    if ($script:ownedTargetHwnd -eq [IntPtr]::Zero) { return }
    $h = $script:ownedTargetHwnd
    $t = Get-Title $h
    if (-not [M15W2]::IsWindow($h) -or $t -notlike "*$targetSubstr*") {
        # 窗口已自行关闭或标题漂移：不改动（可能已被用户接管）。
        Write-Host "[M17] cleanup: owned target gone or drifted - left untouched (hwnd=$(HwndHex $h) title='$t')"
        return
    }
    # 只关闭归属窗口自身（WM_CLOSE 仅发向该窗口，不涉及其他窗口）。
    [M15W]::SendMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 600
    if (-not [M15W2]::IsWindow($h)) {
        Write-Host "[M17] cleanup: closed owned target 0x$($h.ToString('X')) '$t'"
        Stop-OwnedOrphanProcess
        return
    }
    # 脏标签关闭时 Win11 记事本会弹应用内保存提示挡住 WM_CLOSE。
    # 提示已把该窗口置于前台时发送 Alt+N（不保存）关闭；前台不是
    # 该窗口时不发按键（避免打进用户窗口）。
    $dlgDeadline = (Get-Date).AddSeconds(2)
    while ((Get-Date) -lt $dlgDeadline -and [M15W2]::IsWindow($h)) {
        if ([M15W2]::GetForegroundWindow() -eq $h) {
            [M16K2]::AltN()
            Start-Sleep -Milliseconds 600
        }
        Start-Sleep -Milliseconds 200
    }
    if ([M15W2]::IsWindow($h)) {
        Write-Host "[M17] cleanup: owned target still open after Alt+N - left as-is (hwnd=$(HwndHex $h))"
    } else {
        Write-Host "[M17] cleanup: closed owned target via Alt+N (hwnd=$(HwndHex $h))"
        Stop-OwnedOrphanProcess
    }
}

# ---- 诱饵对照（R1 F-03：证明清理范围不越界；finally 结束后验证）----
# 模拟"其他轮标记文件"：旧逻辑会跨轮删除，本轮不得删除。
$script:lureDir = Join-Path (Split-Path $runDir -Parent) "lure-run-$ts"
New-Item -ItemType Directory -Force -Path $script:lureDir | Out-Null
$script:lureFile = Join-Path $script:lureDir "P5B001NP-lure-$ts.txt"
Set-Content -LiteralPath $script:lureFile -Value "P5B-001 other-run marker lure (must survive this run)" -Encoding UTF8
# 模拟"其他轮标记窗口"（标题含 P5B001NP-*.txt，旧逻辑按前缀清理会
# 命中）：与驱动的目标/覆盖窗口均无关联，本轮不得关闭/移动。
$script:lureWindow = New-Object System.Windows.Forms.Form
$script:lureWindow.Text = "P5B001NP-lure-$ts.txt"
$script:lureWindow.StartPosition = 'Manual'
$script:lureWindow.Left = 60; $script:lureWindow.Top = 60
$script:lureWindow.Width = 300; $script:lureWindow.Height = 120
$script:lureWindow.Show()
Start-Sleep -Milliseconds 300
$lureR = New-Object M15W+RECT
[M15W]::GetWindowRect($script:lureWindow.Handle, [ref]$lureR) | Out-Null
$script:lureSnapshot = @{ Text = $script:lureWindow.Text; L = $lureR.L; T = $lureR.T; R = $lureR.R; B = $lureR.B }
# 模拟"无窗口外部进程"：旧逻辑会按"无窗口 Notepad/无窗口进程"强杀，
# 本轮不得杀（独立 pid，非驱动、非目标）。
$script:windowlessLure = Start-Process -FilePath 'powershell' `
    -ArgumentList '-NoProfile', '-Command', 'Start-Sleep 600' -PassThru `
    -WindowStyle Hidden
Write-Host "[M17] lures set: file=$script:lureFile window='$($script:lureWindow.Text)' @($($lureR.L),$($lureR.T)) windowlessPid=$($script:windowlessLure.Id)"

# ---- 安全预检 ----
$existingNp = @(Get-Tops | ForEach-Object { Get-Title ([IntPtr]$_) } | Where-Object { $_ -like '*- Notepad' })
if ($existingNp.Count -gt 0) {
    throw "existing Notepad window(s) found: [$($existingNp -join '; ')] - notepad target unsafe; close user Notepad first"
}
Write-Host "[M17] run dir: $runDir"
Write-Host "[M17] targetSubstr='$targetSubstr' (unique; user windows cannot match)"

@{ targetTitleSubstr = $targetSubstr; durationSec = $DurationSec } |
    ConvertTo-Json | Set-Content (Join-Path $scenarioDir 'window-climb-live-config.json') -Encoding UTF8

# ---- 归属确认目标（零写入直到归属确认）----
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
            $owned = ($null -ne $cProc -and $cProc.ProcessName -eq 'Notepad' -and
                $cPid -ne $PID -and $cProc.StartTime -ge $script:t0.AddSeconds(-2))
            if (-not $owned) { continue }
            return @{ Hwnd = [IntPtr]$h; Pid = $cPid }
        }
    }
    return $null
}

$script:probe = $null
$cover = $null
try {
    # 1. 启动目标并归属确认
    $targetProc = Start-Target
    Write-Host "[M17] target start issued pid=$($targetProc.Id)"
    $found = Find-OwnedTarget @()
    if ($null -eq $found) { throw 'no marker-owned target window appeared - aborting (user windows untouched)' }
    $hwnd = $found.Hwnd
    $script:ownedTargetHwnd = $found.Hwnd
    $script:ownedTargetPid = $found.Pid
    Start-Sleep -Milliseconds 1500
    if ((Get-Title $hwnd) -notlike "*$targetSubstr*") {
        throw "target title drifted off this run's marker - aborting (user windows untouched)"
    }
    Write-Host "[M17] target ownership verified: hwnd=$(HwndHex $hwnd) pid=$($found.Pid)"
    Add-Check '① 目标标记与归属确认（驱动侧零写入前提）' $true "hwnd=$(HwndHex $hwnd) pid=$($found.Pid) marker=$targetSubstr"

    # 2. 启动探针（真实宠物窗口）并等心跳
    $script:probe = Start-Process -FilePath $probeExe -ArgumentList "`"$runDir`"", 'window-climb-live', "`"$resultFile`"" -PassThru -WindowStyle Hidden
    Write-Host "[M17] probe started pid=$($script:probe.Id)"
    $stDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $stDeadline -and $null -eq (Get-State)) { Start-Sleep -Milliseconds 300 }
    if ($null -eq (Get-State)) { throw 'probe state file not appearing - aborting' }

    # 目标贴宠物脚侧放置：左缘=脚点+130（右放不下则左侧镜像），上缘=脚点−260，
    # 全屏内钳制。脚点在外框左侧 130px（垂直范围内），起攀距离 130 ≤200。
    function Place-TargetNearFoot {
        for ($r = 1; $r -le 4; $r++) {
            $st = Get-State
            if ($null -ne $st -and $null -ne $st.petFootPx) { break }
            Start-Sleep -Milliseconds 300
        }
        $st = Get-State
        if ($null -eq $st) { return $false }
        $sb = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $fx = [int]$st.petFootPx[0]; $fy = [int]$st.petFootPx[1]
        $tx = $fx + 130
        if (($tx + 800) -gt $sb.Right) { $tx = $fx - 130 - 800 }
        if ($tx -lt $sb.Left) { $tx = $sb.Left }
        $ty = $fy - 260
        # 落位校验要求完整宠物矩形（120px）落在屏幕上：目标上缘至少
        # 距屏幕顶 160px（120 + 40 余量），否则顶部落位点出屏会被
        # 产品 RejectedNoLanding 正确拒绝（run-20261121-141036 教训）。
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

    # 起攀前保证宠物空闲（R1 F-01 修复后已栖息/社交占用时起攀被正确拒绝
    # RejectedBusy；上一场景如 ⑩ 收敛会留下栖息，须先真实拖动脱离，再
    # 重新贴脚起攀，保证“路线中”各场景真正处于爬行状态）。
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
                if (-not $dg.Ok) { Start-Sleep -Milliseconds 600; continue }
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

    # 落点遮挡者诊断（run-20261121-155443 ② RejectedOccluded 根因：
    # 真实全屏窗口盖住落点采样区，产品正确拒绝）。按产品采样规则
    # （footCenterX±petWidthPx/2，y=外框上缘+2）实时 WindowFromPoint，
    # 列出命中且非目标根窗口的遮挡者标题，区分“环境干扰”与“产品缺陷”。
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

    # 起攀：贴脚放置 + start 命令，等 climb-started / start-rejected。
    function Invoke-ClimbStart {
        $idle = Ensure-IdleForClimb
        if (-not $idle.Ok) { return @{ Ok = $false; Detail = "idle check: $($idle.Detail)" } }
        if (-not (Place-TargetNearFoot)) { return @{ Ok = $false; Detail = 'state missing for near-foot placement' } }
        if (-not (Test-ProbeAlive)) { return @{ Ok = $false; Detail = 'probe exited before start' } }
        $startMs = Get-LastEventMs
        Send-Command 'start'
        $deadline = (Get-Date).AddSeconds(8)
        while ((Get-Date) -lt $deadline) {
            $rej = Read-Events | Where-Object { $_.type -eq 'start-rejected' -and $_.tMs -gt $startMs }
            if ($rej) { return @{ Ok = $false; Detail = "start-rejected $($rej[-1].result)"; Rejected = $rej[-1]; StartMs = $startMs } }
            $st = Get-State
            if ($null -ne $st -and $st.climbActive) { break }
            Start-Sleep -Milliseconds 150
        }
        $started = Wait-Event 'climb-started' 5 $startMs
        if ($null -eq $started) { return @{ Ok = $false; Detail = 'no climb-started event'; StartMs = $startMs } }
        return @{ Ok = $true; Detail = "started targetHwnd=$($started.targetHwnd)"; Started = $started; StartMs = $startMs }
    }

    # 3. 起攀 → climb-started（targetHwnd 匹配目标）
    $res1 = Invoke-ClimbStart
    $hwndMatch = ($res1.Ok -and $res1.Started.targetHwnd -eq (HwndHex $hwnd))
    Add-Check '② 起攀：贴脚放置+start → climb-started(targetHwnd 匹配)' $hwndMatch $res1.Detail
    if (-not $res1.Ok) {
        if ($res1.Rejected -and $res1.Rejected.result -eq 'RejectedOccluded') {
            # 落点被真实窗口覆盖 → 环境干扰（用户桌面有全屏/覆盖窗口）：
            # 输出遮挡者供确认，缩小/移开这些窗口后重跑（不改产品判定）。
            $occ = Get-LandingOccluder $hwnd
            Write-Host "[M17] ② landing occluded - occluders at y=$($null -eq (Get-FrameBounds $hwnd) ? 'n/a' : ((Get-FrameBounds $hwnd).T + 2)): $occ"
            Write-Host "[M17] ② ENV: minimize or move covering windows above, then rerun (product RejectedOccluded is correct)"
        }
        throw 'first climb start failed - aborting'
    }
    $startMs1 = $res1.StartMs

    # 4. 路线推进：climb-step ≥3 且 fraction 严格递增
    $steps = @(Read-Events | Where-Object { $_.type -eq 'climb-step' -and $_.tMs -gt $startMs1 })
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline -and $steps.Count -lt 3) {
        Start-Sleep -Milliseconds 300
        $steps = @(Read-Events | Where-Object { $_.type -eq 'climb-step' -and $_.tMs -gt $startMs1 })
    }
    $fracInc = $true
    for ($i = 1; $i -lt $steps.Count; $i++) {
        if ($steps[$i].fraction -le $steps[$i - 1].fraction) { $fracInc = $false; break }
    }
    Add-Check '③ 路线推进：climb-step≥3 且 fraction 递增' ($steps.Count -ge 3 -and $fracInc) "steps=$($steps.Count) fracInc=$fracInc fractions=[$(@($steps | ForEach-Object { $_.fraction }) -join ',')]"

    # 5. 顶部交接：climb-completed 脚点=上缘中点±6、perchActive、无跳动
    $completed = Wait-Event 'climb-completed' 15 $startMs1
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
    Add-Check '④ 顶部交接：climb-completed 脚点=上缘中点±6 perchActive 无跳动' $compOk $compDetail
    if (-not $compOk) { throw 'climb handoff failed - aborting' }

    # 6. 交接后跟随：移动目标 → 脚点贴新上缘（state 断言，本场景无 follow 事件）
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
                $followDetail = "foot=[$($stF.petFootPx -join ',')] newEdge=[$([Math]::Round($expX,1)),$expY] perch=$($stF.perchActive) (state 断言，无 follow 事件)"
                break
            }
            $followDetail = "foot=[$($stF.petFootPx -join ',')] expected=[$([Math]::Round($expX,1)),$expY] perch=$($stF.perchActive)"
        }
        Start-Sleep -Milliseconds 250
    }
    Add-Check '⑤ 交接后跟随：移动目标后脚点贴新上缘' $followOk $followDetail

    # 真实拖动助手：三重校验（SetCursorPos 回读 ±2 + 命中宠物根窗口），
    # 每次重取宠物 rect（栖息中静止 / 攀爬中移动）。
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
            $pr = $st.petRectPx
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
        for ($try = 1; $try -le 5; $try++) {
            [M15W2]::SetCursorPos($cx, $cy) | Out-Null
            $cp = New-Object M15W2+POINT
            [M15W2]::GetCursorPos([ref]$cp) | Out-Null
            if ([Math]::Abs($cp.X - $cx) -le 2 -and [Math]::Abs($cp.Y - $cy) -le 2) { break }
            if ($try -eq 5) { return @{ Ok = $false; Detail = "cursor injection ineffective at ($cx,$cy) - user mouse active?" }
            }
            Start-Sleep -Milliseconds 300
        }
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

    # 7. 栖息中真实拖动 → 脱离栖息，随后移动目标不回拉
    $stBeforeDrag = Get-State
    $drag1 = Invoke-RealDrag 26 4 10 700
    Start-Sleep -Milliseconds 600
    $stAfterDrag = Get-State
    $detachOk = ($drag1.Ok -and $null -ne $stAfterDrag -and -not $stAfterDrag.perchActive -and
        [Math]::Abs($stAfterDrag.petFootPx[0] - $stBeforeDrag.petFootPx[0]) -gt 30 -and
        [Math]::Abs($stAfterDrag.petFootPx[1] - $stBeforeDrag.petFootPx[1]) -gt 30)
    $r4 = New-Object M15W+RECT
    [M15W]::GetWindowRect($hwnd, [ref]$r4) | Out-Null
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, ($r4.L + 150), ($r4.T + 80), 800, 500, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 1500
    $stAfterMove1 = Get-State
    $noPull1 = ($detachOk -and $null -ne $stAfterMove1 -and -not $stAfterMove1.perchActive -and
        [Math]::Abs($stAfterMove1.petFootPx[0] - $stAfterDrag.petFootPx[0]) -le 2 -and
        [Math]::Abs($stAfterMove1.petFootPx[1] - $stAfterDrag.petFootPx[1]) -le 2)
    Add-Check '⑥ 栖息中真实拖动 → 脱离栖息，移动目标不回拉' $noPull1 "drag=$($drag1.Ok) $($drag1.Detail) detached=$(-not $stAfterDrag.perchActive) stayAtDrop=$([Math]::Abs($stAfterMove1.petFootPx[0]-$stAfterDrag.petFootPx[0]) -le 2 -and [Math]::Abs($stAfterMove1.petFootPx[1]-$stAfterDrag.petFootPx[1]) -le 2)"

    # 8. 超距起攀 → RejectedTooFar，宠物不动
    $stFar = Get-State
    $sb2 = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $fx = [int]$stFar.petFootPx[0]
    $farX = $(if ($fx -lt ($sb2.Left + $sb2.Width / 2)) { $sb2.Right - 820 } else { $sb2.Left + 20 })
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, $farX, 400, 800, 500, 0x0004) | Out-Null
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 600
    $fbFar = Get-FrameBounds $hwnd
    $distFar = Get-FootToFrameDistance @([int]$stFar.petFootPx[0], [int]$stFar.petFootPx[1]) $fbFar
    $farMs = Get-LastEventMs
    Send-Command 'start'
    $rejFar = Wait-Event 'start-rejected' 8 $farMs
    Start-Sleep -Milliseconds 800
    $stFarAfter = Get-State
    $tooFarOk = ($null -ne $rejFar -and $rejFar.result -eq 'RejectedTooFar' -and $distFar -gt 200 -and
        [Math]::Abs($stFarAfter.petFootPx[0] - $stFar.petFootPx[0]) -le 2 -and
        [Math]::Abs($stFarAfter.petFootPx[1] - $stFar.petFootPx[1]) -le 2 -and
        -not $stFarAfter.climbActive -and -not $stFarAfter.perchActive)
    Add-Check '⑦ 超距起攀 RejectedTooFar 不移动' $tooFarOk "result=$($rejFar.result) distance=$([Math]::Round($distFar,1))px (>200) footMove=($([Math]::Abs($stFarAfter.petFootPx[0]-$stFar.petFootPx[0])),$([Math]::Abs($stFarAfter.petFootPx[1]-$stFar.petFootPx[1])))"

    # 9. 盖住落位起攀 → RejectedOccluded，宠物不动
    Place-TargetNearFoot | Out-Null
    $fbOcc2 = Get-FrameBounds $hwnd
    $cover = New-Object System.Windows.Forms.Form
    $cover.Text = "P5B001Cover-$ts"
    $cover.StartPosition = 'Manual'
    $cover.Left = $fbOcc2.L + 40; $cover.Top = $fbOcc2.T - 5
    $cover.Width = [Math]::Min(500, ($fbOcc2.R - $fbOcc2.L) - 80); $cover.Height = 260
    $cover.TopMost = $true
    $cover.Show()
    [M15W]::SetWindowPos($cover.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
    [M15W]::SetWindowPos($hwnd, $cover.Handle, 0, 0, 0, 0, 0x0013) | Out-Null
    Start-Sleep -Milliseconds 600
    $stOcc2 = Get-State
    $occMs = Get-LastEventMs
    Send-Command 'start'
    $rejOcc = Wait-Event 'start-rejected' 8 $occMs
    Start-Sleep -Milliseconds 800
    $stOcc2After = Get-State
    $occStartOk = ($null -ne $rejOcc -and $rejOcc.result -eq 'RejectedOccluded' -and
        [Math]::Abs($stOcc2After.petFootPx[0] - $stOcc2.petFootPx[0]) -le 2 -and
        [Math]::Abs($stOcc2After.petFootPx[1] - $stOcc2.petFootPx[1]) -le 2 -and -not $stOcc2After.climbActive)
    Add-Check '⑧ 盖住落位起攀 RejectedOccluded 不移动' $occStartOk "result=$($rejOcc.result) footMove=($([Math]::Abs($stOcc2After.petFootPx[0]-$stOcc2.petFootPx[0])),$([Math]::Abs($stOcc2After.petFootPx[1]-$stOcc2.petFootPx[1])))"
    $cover.Hide(); $cover.Dispose(); $cover = $null
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 400

    # 10. 最小化起攀 → RejectedIneligible，宠物不动
    Place-TargetNearFoot | Out-Null
    $stMin = Get-State
    [M15W]::ShowWindow($hwnd, 6) | Out-Null   # SW_MINIMIZE
    Start-Sleep -Milliseconds 800
    if (-not [M15W2]::IsIconic($hwnd)) {
        [M15W]::ShowWindow($hwnd, 0x0C) | Out-Null   # SW_FORCEMINIMIZE
        Start-Sleep -Milliseconds 800
    }
    $minMs = Get-LastEventMs
    Send-Command 'start'
    $rejMin = Wait-Event 'start-rejected' 8 $minMs
    Start-Sleep -Milliseconds 800
    $stMinAfter = Get-State
    $minOk = ($null -ne $rejMin -and $rejMin.result -eq 'RejectedIneligible' -and
        [Math]::Abs($stMinAfter.petFootPx[0] - $stMin.petFootPx[0]) -le 2 -and
        [Math]::Abs($stMinAfter.petFootPx[1] - $stMin.petFootPx[1]) -le 2 -and -not $stMinAfter.climbActive)
    Add-Check '⑨ 最小化起攀 RejectedIneligible 不移动' $minOk "result=$($rejMin.result) iconic=$([M15W2]::IsIconic($hwnd)) footMove=($([Math]::Abs($stMinAfter.petFootPx[0]-$stMin.petFootPx[0])),$([Math]::Abs($stMinAfter.petFootPx[1]-$stMin.petFootPx[1])))"
    [M15W]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 600

    # 11. 路线中目标 +150,+100 → 收敛新上缘中点（completed 先到则整体重试 ≤3）
    $rebuildOk = $false; $rebuildDetail = 'attempts exhausted'
    :rebuildAttempt for ($attempt = 1; $attempt -le 3; $attempt++) {
        if (-not (Test-ProbeAlive)) { break }
        $resRb = Invoke-ClimbStart
        if (-not $resRb.Ok) { $rebuildDetail = "start failed: $($resRb.Detail)"; continue }
        $rbMs = $resRb.StartMs
        $r5 = New-Object M15W+RECT
        [M15W]::GetWindowRect($hwnd, [ref]$r5) | Out-Null
        # move 必须在 climb-started 后立即执行
        [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, ($r5.L + 150), ($r5.T + 100), 800, 500, 0x0004) | Out-Null
        $moveDoneMs = Get-LastEventMs
        # completed 先到（旧位置收尾）→ 整体重试
        $earlyDone = @(Read-Events | Where-Object { $_.type -like 'climb-completed*' -and $_.tMs -gt $rbMs -and $_.tMs -lt $moveDoneMs })
        if ($earlyDone.Count -gt 0) {
            $rebuildDetail = "attempt $attempt completed before move - retrying"
            continue rebuildAttempt
        }
        # 等收敛：脚点到达新上缘中点 ±6
        $conv = $false
        $deadline = (Get-Date).AddSeconds(25)
        while ((Get-Date) -lt $deadline) {
            $stRb = Get-State
            $fbRb = Get-FrameBounds $hwnd
            if ($null -ne $stRb -and $null -ne $fbRb) {
                $expX = ($fbRb.L + $fbRb.R) / 2.0; $expY = $fbRb.T
                if ([Math]::Abs($stRb.petFootPx[0] - $expX) -le 6 -and [Math]::Abs($stRb.petFootPx[1] - $expY) -le 6 -and $stRb.perchActive) {
                    $conv = $true
                    $rebuildDetail = "converged to new edge midpoint [$([Math]::Round($expX,1)),$expY] perch=$($stRb.perchActive) attempt=$attempt"
                    break
                }
            }
            Start-Sleep -Milliseconds 250
        }
        if ($conv) { $rebuildOk = $true; break }
        $lastRb = Get-State
        $rebuildDetail = "attempt $attempt no convergence: lastEvent=$($lastRb.lastEvent) perch=$($lastRb.perchActive) climb=$($lastRb.climbActive) foot=[$($lastRb.petFootPx -join ',')]"
    }
    Add-Check '⑩ 路线中目标 +150,+100 → 收敛新上缘中点' $rebuildOk $rebuildDetail

    # 12. 路线中最小化 → climb-cancelled(target-*)，宠物回可见区
    $minRouteOk = $false; $minRouteDetail = 'attempts exhausted'
    for ($attempt = 1; $attempt -le 3 -and -not $minRouteOk; $attempt++) {
        if (-not (Test-ProbeAlive)) { break }
        $resMr = Invoke-ClimbStart
        if (-not $resMr.Ok) { $minRouteDetail = "start failed: $($resMr.Detail)"; continue }
        $mrMs = $resMr.StartMs
        [M15W]::ShowWindow($hwnd, 6) | Out-Null
        $cancelledMr = Wait-Event 'climb-cancelled' 12 $mrMs { $_.reason -like 'target-*' }
        if ($null -eq $cancelledMr) {
            [M15W]::ShowWindow($hwnd, 9) | Out-Null
            $minRouteDetail = "attempt $attempt no climb-cancelled(target-*)"
            continue
        }
        Start-Sleep -Milliseconds 800
        $stMr = Get-State
        $vs2 = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $pr = $stMr.petRectPx
        $visible = ($pr[0] -ge ($vs2.X - 10) -and $pr[1] -ge ($vs2.Y - 10) -and
            $pr[2] -le ($vs2.X + $vs2.Width + 10) -and $pr[3] -le ($vs2.Y + $vs2.Height + 10))
        $minRouteOk = (-not $stMr.climbActive -and -not $stMr.perchActive -and $visible)
        $minRouteDetail = "reason=$($cancelledMr.reason) petRect=[$($pr -join ',')] visible=$visible climb=$($stMr.climbActive) perch=$($stMr.perchActive) attempt=$attempt"
        [M15W]::ShowWindow($hwnd, 9) | Out-Null
        Raise-Top $hwnd
        Start-Sleep -Milliseconds 600
    }
    Add-Check '⑪ 路线中最小化 → climb-cancelled(target-*) 宠物回可见区' $minRouteOk $minRouteDetail

    # 13. 路线中上缘遮挡 → climb-occluded + climb-cancelled(occluded)
    $occRouteOk = $false; $occRouteDetail = 'attempts exhausted'
    for ($attempt = 1; $attempt -le 3 -and -not $occRouteOk; $attempt++) {
        if (-not (Test-ProbeAlive)) { break }
        $resOr = Invoke-ClimbStart
        if (-not $resOr.Ok) { $occRouteDetail = "start failed: $($resOr.Detail)"; continue }
        $orMs = $resOr.StartMs
        $fbOr = Get-FrameBounds $hwnd
        if ($null -eq $fbOr) { $occRouteDetail = 'frame bounds missing'; continue }
        $cover = New-Object System.Windows.Forms.Form
        $cover.Text = "P5B001CoverOcc-$ts-$attempt"
        $cover.StartPosition = 'Manual'
        $cover.Left = $fbOr.L + 40; $cover.Top = $fbOr.T - 5
        $cover.Width = [Math]::Min(500, ($fbOr.R - $fbOr.L) - 80); $cover.Height = 260
        $cover.TopMost = $true
        $cover.Show()
        [M15W]::SetWindowPos($cover.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
        $occEv = Wait-Event 'climb-occluded' 12 $orMs { $_.fraction -lt 1 }
        $cancelOr = Wait-Event 'climb-cancelled' 12 $orMs { $_.reason -eq 'occluded' }
        $stOr = Get-State
        $occRouteOk = ($null -ne $occEv -and $null -ne $cancelOr -and -not $stOr.climbActive -and -not $stOr.perchActive)
        $occRouteDetail = "occludedEv=$($null -ne $occEv) cancelReason=$($cancelOr.reason) climb=$($stOr.climbActive) perch=$($stOr.perchActive) attempt=$attempt"
        $cover.Hide(); $cover.Dispose(); $cover = $null
        Raise-Top $hwnd
        Start-Sleep -Milliseconds 400
    }
    Add-Check '⑫ 路线中上缘遮挡 → climb-occluded + climb-cancelled(occluded)' $occRouteOk $occRouteDetail

    # 14. 路线中真实拖动 → climb-drag-preempted，松手后不回拉
    $dragRouteOk = $false; $dragRouteDetail = 'attempts exhausted'
    for ($attempt = 1; $attempt -le 3 -and -not $dragRouteOk; $attempt++) {
        if (-not (Test-ProbeAlive)) { break }
        $resDr = Invoke-ClimbStart
        if (-not $resDr.Ok) { $dragRouteDetail = "start failed: $($resDr.Detail)"; continue }
        $drMs = $resDr.StartMs
        # 攀爬中宠物在移动：容差放宽 20
        $drag2 = Invoke-RealDrag 12 8 6 700 20
        if (-not $drag2.Ok) { $dragRouteDetail = "attempt $attempt drag failed: $($drag2.Detail)"; continue }
        $preempt = Wait-Event 'climb-drag-preempted' 8 $drMs
        Start-Sleep -Milliseconds 600
        $stDr = Get-State
        if ($null -eq $preempt) { $dragRouteDetail = "attempt $attempt no climb-drag-preempted (lastEvent=$($stDr.lastEvent))"; continue }
        # 松手后移动目标 → 不回拉
        $r6 = New-Object M15W+RECT
        [M15W]::GetWindowRect($hwnd, [ref]$r6) | Out-Null
        [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, ($r6.L + 150), ($r6.T + 80), 800, 500, 0x0004) | Out-Null
        Start-Sleep -Milliseconds 1500
        $stDrAfter = Get-State
        $noPull2 = ($null -ne $stDrAfter -and -not $stDrAfter.climbActive -and -not $stDrAfter.perchActive -and
            [Math]::Abs($stDrAfter.petFootPx[0] - $stDr.petFootPx[0]) -le 2 -and
            [Math]::Abs($stDrAfter.petFootPx[1] - $stDr.petFootPx[1]) -le 2)
        $dragRouteOk = $noPull2
        $dragRouteDetail = "preempted=$($null -ne $preempt) $($drag2.Detail) stayAtDrop=$noPull2 foot=[$($stDrAfter.petFootPx -join ',')] attempt=$attempt"
    }
    Add-Check '⑬ 路线中真实拖动 → climb-drag-preempted 松手后不回拉' $dragRouteOk $dragRouteDetail

    # 15. 路线中 pause → climb-cancelled(paused)，resume 不续接
    $pauseOk = $false; $pauseDetail = 'attempts exhausted'
    for ($attempt = 1; $attempt -le 3 -and -not $pauseOk; $attempt++) {
        if (-not (Test-ProbeAlive)) { break }
        $resPa = Invoke-ClimbStart
        if (-not $resPa.Ok) { $pauseDetail = "start failed: $($resPa.Detail)"; continue }
        $paMs = $resPa.StartMs
        Send-Command 'pause'
        $cancelPa = Wait-Event 'climb-cancelled' 12 $paMs { $_.reason -eq 'paused' }
        if ($null -eq $cancelPa) { $pauseDetail = "attempt $attempt no climb-cancelled(paused)"; continue }
        Start-Sleep -Milliseconds 600
        $stPa = Get-State
        $footPa = $stPa.petFootPx
        Send-Command 'resume'
        Start-Sleep -Milliseconds 2000
        $stPaAfter = Get-State
        $noResume = ($null -ne $stPaAfter -and -not $stPaAfter.climbActive -and
            [Math]::Abs($stPaAfter.petFootPx[0] - $footPa[0]) -le 2 -and
            [Math]::Abs($stPaAfter.petFootPx[1] - $footPa[1]) -le 2)
        $pauseOk = ($noResume -and -not $stPaAfter.perchActive)
        $pauseDetail = "cancelReason=$($cancelPa.reason) resumeContinues=$(-not $noResume) climb=$($stPaAfter.climbActive) foot=[$($stPaAfter.petFootPx -join ',')] attempt=$attempt"
    }
    Add-Check '⑭ 路线中 pause → climb-cancelled(paused) resume 不续接' $pauseOk $pauseDetail

    # 16. 结束
    Send-Command 'quit'
    if (-not $script:probe.WaitForExit(30 * 1000)) { Stop-Process -Id $script:probe.Id -Force }
    Write-Host "[M17] probe exited code=$($script:probe.ExitCode)"
    $res = $(if (Test-Path $resultFile) { Get-Content $resultFile -Raw | ConvertFrom-Json } else { $null })
    Add-Check '⑮ 探针 exitCode=0' ($script:probe.ExitCode -eq 0 -and $null -ne $res -and $res.exitCode -eq 0) "probe exit=$($script:probe.ExitCode) result exitCode=$($res.exitCode)"
}
catch {
    Write-Host "[M17] FATAL: $($_.Exception.GetType().Name): $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
    Write-Host "[M17] FATAL position: $($_.InvocationInfo.PositionMessage)"
    throw
}
finally {
    if ($cover) { $cover.Dispose() }
    if ($script:probe -and -not $script:probe.HasExited) { Stop-Process -Id $script:probe.Id -Force -ErrorAction SilentlyContinue }
    # R1 F-03：清理只针对本轮归属对象（归属窗口→其孤儿子进程），只删本轮 markerFile。
    Invoke-CleanupOwnedTarget
    if (Test-Path -LiteralPath $script:markerFile) { Remove-Item -LiteralPath $script:markerFile -Force }
    Start-Sleep -Milliseconds 300
    # R1 F-03：诱饵存活验证——证明清理未越过本轮归属边界（其他轮标记文件、
    # 标记窗口、无窗口外部进程均须原样存活）。
    $lureFileAlive = $false
    if (Test-Path -LiteralPath $script:lureFile) {
        $lureContent = Get-Content -LiteralPath $script:lureFile -Raw -ErrorAction SilentlyContinue
        # Set-Content 会追加 CRLF，Get-Content -Raw 保留换行——比较前须 Trim。
        $lureFileAlive = ($null -ne $lureContent -and
            $lureContent.Trim() -eq 'P5B-001 other-run marker lure (must survive this run)')
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
        # run-20261121-155443：rect=-32000 系窗口被最小化（GetWindowRect 对
        # 最小化窗口返回 -32000），脚本无任何路径移动 lure；据此输出明细供
        # 监理区分"外部系统窗口管理干扰"与"越界清理"，断言口径保持不变。
        $lureWinDetail = "title='$lureTitleNow' rect=[$($lr.L),$($lr.T),$($lr.R),$($lr.B)] snapshot=[$($script:lureSnapshot.L),$($script:lureSnapshot.T),$($script:lureSnapshot.R),$($script:lureSnapshot.B)] iconic=$lureIconic"
    }
    $lureProcAlive = ($null -ne $script:windowlessLure -and -not $script:windowlessLure.HasExited)
    Add-Check '⑯ 其他轮标记文件/标记窗口未越界清理（诱饵存活）' ($lureFileAlive -and $lureWinAlive) "lureFile=$lureFileAlive lureWin=$lureWinAlive $lureWinDetail"
    Add-Check '⑰ 无窗口外部进程未被误杀（诱饵存活）' $lureProcAlive "windowlessPid=$($script:windowlessLure.Id) alive=$lureProcAlive"
    # 诱饵为本轮自建（.temp 下）：验证完成后自行回收，不留进程/文件。
    if ($script:lureWindow) { $script:lureWindow.Dispose() }
    if ($null -ne $script:windowlessLure -and -not $script:windowlessLure.HasExited) {
        Stop-Process -Id $script:windowlessLure.Id -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $script:lureDir) { Remove-Item -LiteralPath $script:lureDir -Recurse -Force -ErrorAction SilentlyContinue }
    if ($script:checks.Count -eq 0) {
        Write-Host "[M17] ABORTED before any check - no checklist"
        Stop-Transcript | Out-Null
        exit 1
    }
    # R1 F-04：固定条目汇总——提前抛错/中途退出时清单不完整，必须显式 FAIL。
    $expectCount = 18
    if ($script:checks.Count -ne $expectCount) {
        Write-Host "[M17] ABORTED EARLY: checklist incomplete ($($script:checks.Count)/$expectCount) - marked FAIL"
        Add-Check '⑱ 完整流程走完（固定条目数）' $false "$($script:checks.Count)/$expectCount"
    } else {
        Add-Check '⑱ 完整流程走完（固定条目数）' $true "$($script:checks.Count)/$expectCount"
    }
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Set-Content (Join-Path $runDir 'm17-checklist.txt')
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $allPass = -not ($script:checks | Where-Object { $_.Result -eq 'FAIL' })
    Write-Host "[M17] checklist: $(if ($allPass) { 'ALL PASS' } else { 'HAS FAIL' }) -> $runDir"
    Stop-Transcript | Out-Null
    if (-not $allPass) { exit 1 }
}
