# P5A-003 / TESTING M-16: 窗口栖息正式入口隔离预览驱动。
# 证据写入 .temp\p5a-003\entry\run-<时间戳>-<目标>\。
#
# 与 M-15 的区别：不再用探针 select 命令直接选窗，全部经正式入口：
#   pick-entry（右键菜单「Perch on Window」同路径）→ 真实注入左键点击
#   目标 → 生产拾取轮询（GetAsyncKeyState/GetCursorPos/WindowFromPoint）
#   解析并选中 → landed。
# 操作序列（每步断言探针事件与状态）：
#   1 pick-entry+真实左键点击 → landed（脚点=目标可见外框上缘中点）
#   2 移动目标 → follow（脚点随新上缘）
#   3 盖住落脚区 → occluded-detach（宠物停在原地）
#   4 pick-entry 重选 → landed
#   5 关闭目标 → detached(WindowGone/IsWindow=false，宠物停在原地)
#   6 重开目标 + pick-entry → landed
#   7 真实鼠标拖动宠物 → drag-preempted；随后移动目标 → 无 follow 不回拉
#   8 pick-entry 再选（显式选择不受拖动黑名单限制）→ landed
#   9 leave-entry（右键菜单「Leave Perch」同路径）→ detached(user-left)，
#     宠物留在当前位置；随后移动目标 → 无 follow
#   10 quit → 探针 exitCode=0。
#
# 目标锁定/鼠标注入纪律与 M-15 相同（不触碰用户窗口；注入前光标回读 +
# 命中 + 前台三重前置校验；归属不明只读跳过）。
#
# 用法（仓库根目录，pwsh 7 -File 独立进程运行）：
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5A003EntryPreview.ps1 -TargetKind notepad
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5A003EntryPreview.ps1 -TargetKind edge
#
# -RealMenu（R1 F-02 补证）：pick/leave 不再走探针测试命令，改为在宠物
# 窗口注入真实右键（WM_RBUTTONUP → 生产 ShowNativeContextMenu/TrackPopupMenuEx），
# 用 UIA 定位弹出的原生菜单项「Perch on Window」/「Leave Perch」后注入
# 真实左键选中，走完整生产链。清单仍 12 项，Detail 注明经真实菜单。
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5A003EntryPreview.ps1 -TargetKind notepad -RealMenu

param(
    [Parameter(Mandatory = $true)][ValidateSet('notepad', 'edge')][string]$TargetKind,
    [int]$DurationSec = 300,
    [switch]$RealMenu
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:t0 = Get-Date
$runDir = Join-Path $repo ".temp\p5a-003\entry\run-$ts-$TargetKind"
$scenarioDir = Join-Path $runDir 'window-perch-live'
New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
$resultFile = Join-Path $runDir 'probe-result.json'
$eventsFile = Join-Path $scenarioDir 'perch-live-events.jsonl'
$stateFile = Join-Path $scenarioDir 'perch-live-state.json'
$commandsFile = Join-Path $scenarioDir 'perch-live-commands.txt'
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
Write-Host "[$TargetKind] driver thread DPI awareness = $threadAwareness (2=PerMonitor; required for physical px)"
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
  // run-180459/185949 诊断：前台归属 + 最后真实输入年龄
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
  // run-180459 教训：系统前台锁被其它应用持有时，生产的
  // SetForegroundWindow(宠物) 被拒绝后静默返回（菜单不开）。
  // 注入一次 Alt press/release 是解锁 SetForegroundWindow 的
  // 经典做法（让系统认为当前前台已收到输入，随后允许切换前台）。
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
if (-not ([System.Management.Automation.PSTypeName]'M16K2').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M16K2 {
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static void AltN() {
    keybd_event(0x12, 0, 0, UIntPtr.Zero);      // ALT down
    keybd_event(0x4E, 0, 0, UIntPtr.Zero);      // N down
    keybd_event(0x4E, 0, 2, UIntPtr.Zero);      // N up
    keybd_event(0x12, 0, 2, UIntPtr.Zero);      // ALT up
  }
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
function Get-Tops { [M15W]::Tops() }
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
function Get-FrameBounds([IntPtr]$h) {
    $r = New-Object M15W2+RECT
    $hr = [M15W2]::DwmGetWindowAttribute($h, 9, [ref]$r, [System.Runtime.InteropServices.Marshal]::SizeOf([type][M15W2+RECT]))
    if ($hr -ne 0) { return $null }
    $r
}
function Raise-Top([IntPtr]$h) {
    [M15W]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
}
# 原生弹出菜单窗口（类 #32768 且归属探针进程），无则 Zero。
# run-130109 step10 教训：UIA RootElement.FindAll 单次调用可能跨进程
# 阻塞十几秒（WPS/VS Code/Nahimic provider 卡顿），菜单出现/关闭的
# 探测循环里反复 UIA 调用会击穿 deadline。菜单出现与关闭判定全部改用
# 本函数（纯 FindWindowEx，微秒级）；UIA 只保留给选中前的项索引定位。
function Find-ProbeMenuHwnd {
    $mh = [M16M]::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, '#32768', $null)
    while ($mh -ne [IntPtr]::Zero) {
        $mp = [uint32]0
        [M16M]::GetWindowThreadProcessId($mh, [ref]$mp) | Out-Null
        if ($mp -eq [uint32]$probe.Id) { return $mh }
        $mh = [M16M]::FindWindowEx([IntPtr]::Zero, $mh, '#32768', $null)
    }
    return [IntPtr]::Zero
}
function Test-TopEdgeVisible([IntPtr]$h) {
    $r = New-Object M15W+RECT
    if (-not [M15W]::GetWindowRect($h, [ref]$r)) { return $false }
    $y = $r.T + 2
    $w = $r.R - $r.L
    $blocked = 0
    for ($i = 0; $i -lt 9; $i++) {
        $p = New-Object M15Hit+POINT
        $p.X = $r.L + [int][Math]::Round($w * ($i + 0.5) / 9)
        $p.Y = $y
        $root = [M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($p), 2)
        if ($root -eq $h) { continue }
        # 宠物自身悬停在目标上缘属正常状态（如遮挡脱离后停在原落脚处），
        # 不算遮挡；探针启动前 $probe 为 null，此时任何阻挡都算。
        if ($null -ne $probe) {
            $rootPid = [uint32]0
            [M15W2]::GetWindowThreadProcessId($root, [ref]$rootPid) | Out-Null
            if ($rootPid -eq [uint32]$probe.Id) { continue }
        }
        return $false
    }
    return $true
}
$script:candidatePos = @(
    @(1450, 800), @(1200, 300), @(2200, 300), @(500, 300), @(500, 900),
    @(1200, 1000), @(2000, 1000), @(100, 100), @(2000, 100),
    @(700, 250), @(1000, 300), @(100, 250), @(300, 500), @(700, 500), @(150, 520), @(900, 520)
)
$script:anchorX = 0; $script:anchorY = 0
function Test-SpotFitsScreen([int]$x, [int]$y) {
    $sb = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $tw = 800; $th = 500; $dx = 100; $dy = 50; $petMargin = 200
    if ($y - $petMargin -lt $sb.Y) { return $false }
    if ($x -lt $sb.X -or ($x + $tw) -gt ($sb.X + $sb.Width)) { return $false }
    if ($y -lt $sb.Y -or ($y + $th) -gt ($sb.Y + $sb.Height)) { return $false }
    if (($x + $dx + $tw) -gt ($sb.X + $sb.Width)) { return $false }
    if (($y + $dy + $th) -gt ($sb.Y + $sb.Height)) { return $false }
    return $true
}
function Find-ClearSpot([IntPtr]$h) {
    foreach ($c in $script:candidatePos) {
        if (-not (Test-SpotFitsScreen $c[0] $c[1])) { continue }
        [M15W]::SetWindowPos($h, [IntPtr]::Zero, $c[0], $c[1], 800, 500, 0x0004) | Out-Null
        Raise-Top $h
        Start-Sleep -Milliseconds 400
        if (Test-TopEdgeVisible $h) {
            $script:anchorX = $c[0]; $script:anchorY = $c[1]
            Write-Host "[$TargetKind] clear spot found: ($($c[0]),$($c[1]))"
            return $true
        }
    }
    return $false
}
function Ensure-TargetVisible([IntPtr]$h) {
    if ([M15W2]::IsIconic($h)) {
        [M15W]::ShowWindow($h, 1) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    for ($try = 1; $try -le 3; $try++) {
        Raise-Top $h
        Start-Sleep -Milliseconds 400
        if (Test-TopEdgeVisible $h) { return $true }
    }
    if ([M15W2]::IsIconic($h)) {
        [M15W]::ShowWindow($h, 1) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    return (Find-ClearSpot $h)
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
        if ($where) { $ev = $ev | Where-Object -FilterScript $where }        if ($ev) { return $ev[-1] }
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
    # run-130109 step11 教训：探针每 200ms File.Move 覆写状态文件，驱动
    # Get-Content 撞上 Move 瞬间会抛 IOException 被 catch 成 $null，下游
    # 把「读失败」误当「找不到宠物窗口」。读失败短退避重试。
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
function Test-FootOnTopEdge([IntPtr]$target) {
    $st = Get-State
    $fb = Get-FrameBounds $target
    if ($null -eq $st -or $null -eq $fb) { return @{ Ok = $false; Detail = 'state or frame bounds missing' } }
    $expX = ($fb.L + $fb.R) / 2.0
    $expY = $fb.T
    $dx = [Math]::Abs($st.petFootPx[0] - $expX)
    $dy = [Math]::Abs($st.petFootPx[1] - $expY)
    @{ Ok = ($dx -le 6 -and $dy -le 6); Detail = "petFoot=[$($st.petFootPx -join ',')] expected=[$([Math]::Round($expX,1)),$expY] dx=$dx dy=$dy" }
}
# 等探针心跳出现指定拾取状态（pick-entry 经 BeginInvoke 异步生效）。
function Wait-PickStatus([string[]]$statuses, [int]$timeoutSec = 8) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $st = Get-State
        if ($null -ne $st -and $null -ne $st.PSObject.Properties['perchPickStatus'] -and
            $statuses -contains $st.perchPickStatus) { return $st }
        Start-Sleep -Milliseconds 150
    }
    return $null
}
# 屏内判定（run-124115 教训）：SetCursorPos 会把屏幕外坐标钳制到屏内
# 最近点，「先 WindowFromPoint 校验再 SetCursorPos 点击」的点若在屏外，
# 校验像素≠点击像素，点击可能落进透明区穿透。所有注入点必须屏内。
function Test-PointOnScreen([int]$x, [int]$y) {
    $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
    return ($x -ge $vs.X -and $x -lt ($vs.X + $vs.Width) -and
        $y -ge $vs.Y -and $y -lt ($vs.Y + $vs.Height))
}
# 按探针 pid + state.petRectPx 定位宠物窗口（步骤 9 同规则，抽成函数）。
function Find-PetHwnd {
    $st = Get-State
    if ($null -eq $st -or $null -eq $st.petRectPx) { return [IntPtr]::Zero }
    $petRect = $st.petRectPx
    foreach ($h in Get-Tops) {
        $p = [uint32]0
        [M15W2]::GetWindowThreadProcessId([IntPtr]$h, [ref]$p) | Out-Null
        if ($p -ne [uint32]$probe.Id) { continue }
        $pr = New-Object M15W+RECT
        [M15W]::GetWindowRect([IntPtr]$h, [ref]$pr) | Out-Null
        if ([Math]::Abs($pr.L - $petRect[0]) -le 3 -and [Math]::Abs($pr.T - $petRect[1]) -le 3) { return [IntPtr]$h }
    }
    return [IntPtr]::Zero
}
# 真实右键菜单操作（R1 F-02）：在宠物身上注入真实右键触发生产
# WndProc(WM_RBUTTONUP) → ShowNativeContextMenu → TrackPopupMenuEx，
# 再用 UIA 定位弹出的原生菜单项并注入真实左键选中。
function Invoke-RealContextMenu([string]$itemName) {
    # run-141054 step11 教训：单次 Find-PetHwnd 失败（状态读竞态或瞬态
    # rect 不匹配）直接放弃会让后续链路全挂；短退避重试 4 次。
    $petHwnd = [IntPtr]::Zero
    for ($fp = 0; $fp -lt 4 -and $petHwnd -eq [IntPtr]::Zero; $fp++) {
        $petHwnd = Find-PetHwnd
        if ($petHwnd -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
    }
    if ($petHwnd -eq [IntPtr]::Zero) { return @{ Ok = $false; Detail = 'pet hwnd not found by probe pid' } }
    $st = Get-State
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
    $posOk = $true
    for ($try = 1; $try -le 5; $try++) {
        [M15W2]::SetCursorPos($click[0], $click[1]) | Out-Null
        $cp = New-Object M15W2+POINT
        [M15W2]::GetCursorPos([ref]$cp) | Out-Null
        if ([Math]::Abs($cp.X - $click[0]) -le 2 -and [Math]::Abs($cp.Y - $click[1]) -le 2) { break }
        $posOk = $false; Start-Sleep -Milliseconds 300
    }
    if (-not $posOk) { return @{ Ok = $false; Detail = "cursor injection ineffective (user mouse active?) at $($click -join ',')" } }
    $hitPt = New-Object M15Hit+POINT
    $hitPt.X = $click[0]; $hitPt.Y = $click[1]
    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hitPt), 2) -ne $petHwnd) {
        return @{ Ok = $false; Detail = 'hit check failed at right-click point' }
    }
    # 右键注入 + 菜单探测 + 键盘选中。两个已实测的失败模式：
    # (1) 生产 ShowNativeContextMenu 依赖 SetForegroundWindow(宠物窗口)
    #     成功，被系统拒绝时静默返回（无菜单）——093603/094705 轮均出现，
    #     重试右键注入即可恢复；
    # (2) 菜单已弹出但前台不是宠物窗口（094705 pick4：fg=未知窗口），
    #     此时方向键/回车进不了菜单，菜单卡在模态循环阻塞探针 UI 线程，
    #     表现为 pick 模式不激活、后续 Leave Perch 找到的是残留菜单。
    #     故选中前校验前台==宠物窗口，选中后校验菜单已关闭，否则 Esc 重试。
    $selected = $false
    $selDetail = ''
    $lastFg = [IntPtr]::Zero
    for ($rc = 1; $rc -le 4 -and -not $selected; $rc++) {
        $menuEl = $null
        $fgBefore = [M15W2]::GetForegroundWindow()
        # run-180459/185949 诊断：no menu 时必须知道前台被谁持有
        # （进程名/标题/最后真实输入年龄），才能判断前台锁拒绝原因。
        $fgPid = [uint32]0
        [M15W2]::GetWindowThreadProcessId($fgBefore, [ref]$fgPid) | Out-Null
        $fgProcName = ''
        try { $fgProcName = (Get-Process -Id $fgPid -ErrorAction Stop).ProcessName } catch { }
        $lii = New-Object M15W2+LASTINPUTINFO
        $lii.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($lii)
        [M15W2]::GetLastInputInfo([ref]$lii) | Out-Null
        $idleSec = [Math]::Round(([Environment]::TickCount - [int]$lii.dwTime) / 1000.0, 1)
        Write-Host "[$TargetKind] real-menu attempt $rc pre-click: fg=0x$($fgBefore.ToString('X')) pid=$fgPid proc='$fgProcName' title='$(Get-Title $fgBefore)' lastInput=${idleSec}s ago"
        [M15W2]::SetCursorPos($click[0], $click[1]) | Out-Null
        [M15W2]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)   # RIGHTDOWN
        Start-Sleep -Milliseconds 120
        [M15W2]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)   # RIGHTUP
        # 原生通道先行：纯 #32768 扫描等菜单出现（快，不受 UIA 阻塞影响）。
        $menuNative = [IntPtr]::Zero
        $deadline = (Get-Date).AddSeconds(3)
        while ((Get-Date) -lt $deadline -and $menuNative -eq [IntPtr]::Zero) {
            $menuNative = Find-ProbeMenuHwnd
            if ($menuNative -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
        }
        if ($menuNative -eq [IntPtr]::Zero) {
            $fgAfter = [M15W2]::GetForegroundWindow()
            Write-Host "[$TargetKind] real-menu attempt $rc : no menu (fgBefore=0x$($fgBefore.ToString('X')) fgAfter=0x$($fgAfter.ToString('X')) petHwnd=0x$($petHwnd.ToString('X')))"
            Start-Sleep -Milliseconds 600
            continue
        }
        # 键盘焦点守卫：菜单模态循环只有在其宿主线程是前台时才吃键盘。
        $lastFg = [M15W2]::GetForegroundWindow()
        if ($lastFg -ne $petHwnd) {
            Write-Host "[$TargetKind] real-menu attempt $rc : menu open but foreground=0x$($lastFg.ToString('X')) != pet; dismiss & retry"
            [M16K]::Esc()
            Start-Sleep -Milliseconds 400
            continue
        }
        # UIA 通道：探针进程的 Menu 元素（键盘选中需要 UIA 索引）。
        # 菜单已原生确认弹出，此处 UIA 只为取索引，失败重试 5 次。
        $menuEl = $null
        for ($ui = 0; $ui -lt 5 -and $null -eq $menuEl; $ui++) {
            $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$probe.Id)
            $elems = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
            foreach ($el in $elems) {
                if ($el.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu) { $menuEl = $el; break }
            }
            if ($null -eq $menuEl) { Start-Sleep -Milliseconds 150 }
        }
        if ($null -eq $menuEl) {
            Write-Host "[$TargetKind] real-menu attempt $rc : native menu open but UIA menu element missing; dismiss & retry"
            [M16K]::Esc()
            Start-Sleep -Milliseconds 400
            continue
        }
        $nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $itemName)
        $item = $menuEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
        if ($null -eq $item) {
            [M16K]::Esc()
            return @{ Ok = $false; Detail = "menu item '$itemName' not found in native menu" }
        }
        # 选中改用键盘注入：菜单模态循环期间系统会覆盖外部 SetCursorPos
        # （M-16 RealMenu 前两轮实测：菜单已弹出、UIA 已定位到 (394,332)，
        # 光标注入 5 次回读全部失败），而 keybd_event 的方向键/回车由
        # TrackPopupMenuEx 模态循环完整处理，选中链仍是真实生产菜单路径。
        $itemsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
        $allItems = $menuEl.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemsCond)
        $idx = -1
        for ($i = 0; $i -lt $allItems.Count; $i++) {
            if ($allItems[$i].Current.Name -eq $itemName) { $idx = $i; break }
        }
        if ($idx -lt 0) {
            [M16K]::Esc()
            return @{ Ok = $false; Detail = "menu item '$itemName' has no UIA index" }
        }
        # 打开时无高亮项：第 1 次 DOWN 高亮首项，故目标第 idx+1 项需 idx+1 次 DOWN。
        for ($k = 0; $k -le $idx; $k++) {
            [M16K]::Down()
            Start-Sleep -Milliseconds 70
        }
        Start-Sleep -Milliseconds 150
        [M16K]::Enter()
        # 选中生效守卫：菜单必须实际关闭（命令被分发）。若 Enter 没被
        # 菜单吃掉，菜单残留会阻塞探针线程——Esc 关闭后整体重试。
        # run-130109 step10 根因：Enter 之后做 UIA FindAll 可能单次跨进程
        # 阻塞 >15s，击穿拾取 1.5s 关闭 deadline。Enter 后零 UIA，
        # 关闭判定纯 Find-ProbeMenuHwnd（#32768 归探针进程，微秒级）。
        $closed = $false
        $deadlineClose = (Get-Date).AddMilliseconds(1500)
        while ((Get-Date) -lt $deadlineClose) {
            Start-Sleep -Milliseconds 100
            if ((Find-ProbeMenuHwnd) -eq [IntPtr]::Zero) { $closed = $true; break }
        }
        if (-not $closed) {
            Write-Host "[$TargetKind] real-menu attempt $rc : menu did not close after Enter (keyboard focus lost); dismiss & retry"
            [M16K]::Esc()
            Start-Sleep -Milliseconds 400
            continue
        }
        $selected = $true
        $selDetail = "real menu '$itemName' selected: rightClick=[$($click -join ',')] itemIndex=$idx totalItems=$($allItems.Count) viaKeyboard petHwnd=0x$($petHwnd.ToString('X'))"
    }
    if (-not $selected) {
        [M16K]::Esc()
        return @{ Ok = $false; Detail = "real menu '$itemName' not selectable (menu open/foreground/keyboard focus flaky; last fg=0x$($lastFg.ToString('X')) petHwnd=0x$($petHwnd.ToString('X')))" }
    }
    return @{ Ok = $true; Detail = $selDetail }
}

# P5A-003 正式入口选窗：pick-entry 进入拾取模式，真实注入左键点击目标。
# 注入前门控：目标前台 + 光标回读 ±2 + 命中根窗口==目标（M-14 纪律）。
function Invoke-PickEntry([IntPtr]$h, [switch]$UseRealMenu) {
    if (-not (Ensure-TargetVisible $h)) { return @{ Ok = $false; Detail = 'target not visible' } }
    # 窗口管理操作（Raise-Top/移动）后立即发 pick-entry 会在探针处理命令的
    # 瞬间触发记事本窗口被系统最小化（M-16 实测：sent 时 iconic=False，
    # 340ms 内变 True）。加 1s 缓冲让窗口 z-order/位置稳定后再进入拾取。
    Start-Sleep -Milliseconds 1000
    # 发送前确认窗口未最小化（M-16 实测：Ensure-TargetVisible 后的 1s
    # 缓冲期间窗口仍可能被系统最小化）；最小化则恢复+抬升后重试。
    for ($r = 1; $r -le 3; $r++) {
        if (-not [M15W2]::IsIconic($h)) { break }
        [M15W]::ShowWindow($h, 1) | Out-Null
        Raise-Top $h
        Start-Sleep -Milliseconds 800
    }
    if ([M15W2]::IsIconic($h)) { return @{ Ok = $false; Detail = 'target iconic despite restore attempts' } }
    # 最多三轮：瞬态遮挡（如用户应用弹层短暂压住目标）会让资格校验
    # 正确返回 rejected-landing；重试前重新抬升目标再进入拾取。
    # run-130109 step10：UIA 阻塞导致 15s 拾取窗口耗尽成 cancelled-timeout，
    # 也纳入重试（每次重试重开真实菜单，拾取 15s 重新计时）。
    # run-141054 step10 教训：驱动进程可能被系统长时间停摆（实测 7m52s，
    # 显示休眠/系统挂起嫌疑），探针 300s 寿命在停摆中耗尽退出，后续
    # Find-PetHwnd 全部失败且报错误导。每个 attempt 前检查探针存活。
    if ($null -ne $probe -and $probe.HasExited) {
        return @{ Ok = $false; Detail = "probe exited early (pid $($probe.Id)) - driver stalled past probe lifetime; rerun with -DurationSec 1200" }
    }
    :pickAttempt for ($attempt = 1; $attempt -le 3; $attempt++) {
    if ($attempt -gt 1) {
        Raise-Top $h
        Start-Sleep -Milliseconds 1000
    }
    if ($UseRealMenu) {
        $menuRes = Invoke-RealContextMenu 'Perch on Window'
        Write-Host "[$TargetKind] real-menu Perch on Window: $($menuRes.Detail)"
        if (-not $menuRes.Ok) { return @{ Ok = $false; Detail = "real menu Perch on Window failed: $($menuRes.Detail)" } }
    } else {
        Send-Command 'pick-entry'
    }
    $tSend = Get-Date
    Write-Host "[$TargetKind] pick-entry sent at $($tSend.ToString('HH:mm:ss.fff')) windowIconic=$([M15W2]::IsIconic($h))"
    $stPick = $null
    $iconicAt = $null
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        $st = Get-State
        if ($null -ne $st -and $null -ne $st.PSObject.Properties['perchPickStatus'] -and
            $st.perchPickStatus -eq 'active') { $stPick = $st; break }
        if ($null -eq $iconicAt -and [M15W2]::IsIconic($h)) {
            $iconicAt = Get-Date
            $deltaMs = [int]($iconicAt - $tSend).TotalMilliseconds
            $iconicStr = $iconicAt.ToString('HH:mm:ss.fff')
            Write-Host "[$TargetKind] WINDOW BECAME ICONIC at $iconicStr (+${deltaMs}ms after send)"
        }
        Start-Sleep -Milliseconds 150
    }
    if ($null -ne $iconicAt) {
        Write-Host "[$TargetKind] window iconic detected; probe state: pickActive=$(if($stPick){$stPick.perchPickActive}else{'?'}) status=$(if($stPick){$stPick.perchPickStatus}else{'?'})"
    }
    if ($null -eq $stPick) { return @{ Ok = $false; Detail = 'pick mode not activated (no active status)' } }
    $activeStr = (Get-Date).ToString('HH:mm:ss.fff')
    Write-Host "[$TargetKind] active seen at $activeStr windowIconic=$([M15W2]::IsIconic($h))"

    # pick 模式激活后窗口仍可能被系统最小化（M-16 多轮实测：send 后
    # 数百 ms 内变 iconic，点击点会算成 -32000 负坐标）。拾取 15s 窗口
    # 内恢复并抬升即可继续点击。
    for ($r2 = 1; $r2 -le 3; $r2++) {
        if (-not [M15W2]::IsIconic($h)) { break }
        Write-Host "[$TargetKind] target iconic after activation; restoring (try $r2)"
        [M15W]::ShowWindow($h, 1) | Out-Null
        Raise-Top $h
        Start-Sleep -Milliseconds 800
    }
    if ([M15W2]::IsIconic($h)) { return @{ Ok = $false; Detail = 'target iconic after pick activation' } }

    # 找一个 WindowFromPoint 根窗口==目标 的点击点（标题区优先，避开边框 8px 拖拽带）。
    # run-124115 step10 教训：拖动后目标 z-order 被压低或瞬态 iconic 时
    # 35 候选可能全落空；扫描失败先 Raise-Top+恢复 iconic 再重试 2 次。
    $fb = $null
    $click = $null
    Write-Host "[$TargetKind] click-scan start at $((Get-Date).ToString('HH:mm:ss.fff'))"
    for ($scanTry = 1; $scanTry -le 3 -and -not $click; $scanTry++) {
        if ($scanTry -gt 1) {
            Write-Host "[$TargetKind] click-point scan failed (try $($scanTry - 1)) at $((Get-Date).ToString('HH:mm:ss.fff')); Raise-Top + restore iconic + rescan"
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
                if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($pt), 2) -eq $h) { $click = @($cx, $cy); break }
            }
            if ($click) { break }
        }
    }
    if ($null -eq $fb) { return @{ Ok = $false; Detail = 'frame bounds missing' } }
    if (-not $click) { return @{ Ok = $false; Detail = 'no click point resolving to target root' } }
    Write-Host "[$TargetKind] click found [$($click -join ',')] at $((Get-Date).ToString('HH:mm:ss.fff')) (scan try $scanTry)"

    Write-Host "[$TargetKind] fg/cursor phase start at $((Get-Date).ToString('HH:mm:ss.fff'))"
    [M15W]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 150
    $ok = $true
    for ($try = 1; $try -le 5; $try++) {
        [M15W2]::SetCursorPos($click[0], $click[1]) | Out-Null
        $cp = New-Object M15W2+POINT
        [M15W2]::GetCursorPos([ref]$cp) | Out-Null
        if ([Math]::Abs($cp.X - $click[0]) -le 2 -and [Math]::Abs($cp.Y - $click[1]) -le 2) { break }
        $ok = $false; Start-Sleep -Milliseconds 300
    }
    if (-not $ok) { return @{ Ok = $false; Detail = "cursor injection ineffective (user mouse active?) click=$($click -join ',')" } }
    $hitPt = New-Object M15Hit+POINT
    $hitPt.X = $click[0]; $hitPt.Y = $click[1]
    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hitPt), 2) -ne $h) {
        return @{ Ok = $false; Detail = 'hit check failed at click point' }
    }

    # 按住 ~300ms 保证 30fps 拾取轮询捕获按下状态，再释放。
    $pre = Get-State
    Write-Host ("[$TargetKind] pre-inject at {0}: awaitRelease={1} leftDown={2} pickActive={3} status={4}" -f `
        (Get-Date).ToString('HH:mm:ss.fff'), $pre.perchPickAwaitRelease, $pre.perchPickLeftDown, $pre.perchPickActive, $pre.perchPickStatus)
    # 注入前拾取新鲜度门（run-130109/141054 step10 根因修复）：拾取 15s 从
    # 菜单 Enter 时刻计时，驱动侧任何阻塞（UIA/进程启动）都可能让注入时
    # pickActive 已过期。注入 DOWN 前轮询 ≤2.5s 确认拾取仍活跃
    # （status=active 且 perchPickActive）；先观察到终态且 attempt<3 则
    # 跳到 attempt 层整体重试（141054 教训：continue 不能绑到本内层循环，
    # 会原地刷屏耗尽门限而不重试），否则本 attempt 失败返回。
    $gateOk = $false
    $gateTerminal = $null
    $gateDeadline = (Get-Date).AddMilliseconds(2500)
    while ((Get-Date) -lt $gateDeadline) {
        $gst = Get-State
        if ($null -ne $gst -and $null -ne $gst.PSObject.Properties['perchPickStatus']) {
            if ($gst.perchPickStatus -eq 'active' -and $gst.perchPickActive) { $gateOk = $true; break }
            if ($null -eq $gateTerminal -and
                @('cancelled-timeout', 'cancelled', 'cancelled-self', 'rejected-busy') -contains $gst.perchPickStatus) {
                $gateTerminal = $gst.perchPickStatus
            }
        }
        Start-Sleep -Milliseconds 150
    }
    if (-not $gateOk) {
        if ($null -ne $gateTerminal) {
            Write-Host "[$TargetKind] pre-inject gate at $((Get-Date).ToString('HH:mm:ss.fff')): pick expired before inject, status=$gateTerminal"
            if ($attempt -lt 3) { continue pickAttempt }
            return @{ Ok = $false; Detail = "pick expired before inject: status=$gateTerminal" }
        }
        return @{ Ok = $false; Detail = 'pre-inject gate: pick not active within 2.5s' }
    }
    Write-Host "[$TargetKind] gate passed at $((Get-Date).ToString('HH:mm:ss.fff'))"
    [M15W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
    $downSeen = @()
    for ($i = 0; $i -lt 6; $i++) {
        Start-Sleep -Milliseconds 50
        $downSeen += [TW]::GetAsyncKeyState(0x01) -band 0x8000
    }
    Write-Host "[$TargetKind] injected DOWN self-samples=[$($downSeen -join ',')]"
    Start-Sleep -Milliseconds 100
    [M15W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
    Start-Sleep -Milliseconds 300
    $stAfter = Wait-PickStatus @('selected', 'rejected-ineligible', 'rejected-landing', 'cancelled-self', 'cancelled-timeout', 'cancelled') 8
    if ($null -eq $stAfter) { return @{ Ok = $false; Detail = 'no terminal pick status observed' } }
    if ($stAfter.perchPickStatus -ne 'selected') {
        if ($attempt -lt 3 -and $stAfter.perchPickStatus -in @('rejected-landing', 'rejected-ineligible', 'cancelled-timeout')) { continue }
        return @{ Ok = $false; Detail = "pick status=$($stAfter.perchPickStatus) click=$($click -join ',') occBy=$(Get-OccludersAtPoint $click[0] $click[1])" }
    }
    return @{ Ok = $true; Detail = "click=[$($click -join ',')] status=selected" }
    }
    return @{ Ok = $false; Detail = 'pick attempts exhausted' }
}

$script:checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$name, [bool]$pass, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Item = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} : {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

# ---- 目标题材 ----
$script:markerFile = $null
if ($TargetKind -eq 'notepad') {
    $script:markerFile = Join-Path $runDir "P5A003NP-$ts.txt"
    Set-Content -LiteralPath $script:markerFile -Value "P5A-003 M-16 marker file (run $ts)" -Encoding UTF8
    $targetSubstr = "P5A003NP-$ts"
    Get-ChildItem (Split-Path $runDir -Parent) -Directory -Filter 'run-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -File -Filter 'P5A003NP-*.txt' -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -ne $script:markerFile } |
        Remove-Item -Force -ErrorAction SilentlyContinue
} else {
    $targetSubstr = "P5A003Browser-$ts"
    $htmlFile = Join-Path $runDir 'p5a003-page.html'
    Set-Content -Path $htmlFile -Value "<!doctype html><html><head><title>$targetSubstr</title></head><body><h1>P5A-003 M-16 perch entry target</h1></body></html>" -Encoding UTF8
}
function Start-Target {
    if ($TargetKind -eq 'notepad') {
        Start-Process notepad -ArgumentList "`"$script:markerFile`"" -PassThru
    } else {
        Start-Process msedge -ArgumentList "--user-data-dir=`"$runDir\edge-profile`"", '--no-first-run', '--window-size=900,600', "`"file:///$htmlFile`"" -PassThru
    }
}
function Test-EdgeHostOwned([uint32]$procId) {
    $profileArg = (Join-Path $runDir 'edge-profile') + '"'
    $cp = Get-CimInstance Win32_Process -Filter "ProcessId=$procId" -ErrorAction SilentlyContinue
    return ($null -ne $cp -and $cp.CommandLine -and $cp.CommandLine.Contains($profileArg))
}

# ---- 残留标记窗口清理 ----
# Win11 记事本单实例 + 会话恢复会把历史轮残留标签并入新实例；关闭单个
# 标签不会销毁窗口（产品正确不触发 WindowGone），脏标签的 WM_CLOSE 还会
# 弹保存对话框而失效。故：先 WM_CLOSE，仍在时若该进程全部可见顶层窗口
# 都是标记标签/保存对话框，则结束该记事本进程（不碰含外来窗口的进程）。
function Invoke-CleanupMarkerWindows {
    foreach ($h64 in (Get-Tops)) {
        $h = [IntPtr]$h64
        $t = Get-Title $h
        if ($t -notlike '*P5A*NP-*.txt*') { continue }
        [M15W]::SendMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 600
        if (-not [M15W2]::IsWindow($h)) {
            Write-Host "[$TargetKind] closed leftover marker window 0x$($h.ToString('X')) '$t'"
            continue
        }
        # 脏标签关闭时 Win11 记事本会弹应用内「保存/不保存/取消」提示，
        # 挡住 WM_CLOSE。若提示已让该窗口处于前台，发送 Alt+N（不保存）
        # 关闭标签；前台不是该窗口时不发按键（避免打进用户窗口）。
        $dlgDeadline = (Get-Date).AddSeconds(2)
        while ((Get-Date) -lt $dlgDeadline -and [M15W2]::IsWindow($h)) {
            if ([M15W2]::GetForegroundWindow() -eq $h) {
                [M16K2]::AltN()
                Start-Sleep -Milliseconds 600
            }
            Start-Sleep -Milliseconds 200
        }
        $p = [uint32]0
        [M15W2]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
        $allOurs = $true
        foreach ($h264 in (Get-Tops)) {
            $h2 = [IntPtr]$h264
            $p2 = [uint32]0
            [M15W2]::GetWindowThreadProcessId($h2, [ref]$p2) | Out-Null
            if ($p2 -ne $p) { continue }
            $t2 = Get-Title $h2
            # 「无标题/Untitled」空标签来自此前被强杀的测试记事本会话恢复
            # （该进程由本轮测试启动），同样视为可清理；进程 0x1016FA 案例
            # 实测：杀掉 094705 标签后只剩恢复的无标题空标签。
            if ($t2 -and $t2 -notlike '*P5A*NP-*' -and $t2 -ne 'Notepad' -and $t2 -ne '记事本' -and
                $t2 -notlike '无标题*' -and $t2 -notlike 'Untitled*') { $allOurs = $false; break }
        }
        if ($allOurs -and $p -ne 0) {
            Stop-Process -Id $p -Force -ErrorAction SilentlyContinue
            Write-Host "[$TargetKind] killed leftover notepad pid=$p (dirty-tab save prompt blocked WM_CLOSE; process held only marker tabs)"
        } else {
            Write-Host "[$TargetKind] WARNING: leftover marker window 0x$($h.ToString('X')) still open; process has foreign windows - left untouched"
        }
    }
}

# ---- 安全预检 ----
# 无窗口的记事本残留进程：单实例会把新标签并入该进程，且其会话恢复会带回
# 历史脏标签（run-115352 实测：100501 残留进程的脏标签挡住 WM_CLOSE 导致
# 失效脱离 FAIL + 重开 FATAL）。用户记事本必有可见顶层窗口（存在时下面会
# throw），故无任何顶层窗口的 Notepad 进程可安全视为本测试历史残留并结束。
if ($TargetKind -eq 'notepad') {
    foreach ($np in @(Get-Process -Name Notepad -ErrorAction SilentlyContinue)) {
        $hasWindow = $false
        foreach ($h in Get-Tops) {
            $wp = [uint32]0
            [M15W2]::GetWindowThreadProcessId([IntPtr]$h, [ref]$wp) | Out-Null
            if ($wp -eq $np.Id) { $hasWindow = $true; break }
        }
        if (-not $hasWindow) {
            Stop-Process -Id $np.Id -Force -ErrorAction SilentlyContinue
            Write-Host "[$TargetKind] preflight: killed windowless leftover Notepad pid=$($np.Id) (session-restore source of stale dirty tabs)"
        }
    }
}
$preflightLeftover = @(Get-Tops | Where-Object { (Get-Title ([IntPtr]$_)) -like '*P5A*NP-*' })
if ($preflightLeftover.Count -gt 0) {
    Write-Host "[$TargetKind] preflight: cleaning $($preflightLeftover.Count) leftover P5A marker window(s) from previous runs"
    Invoke-CleanupMarkerWindows
    Start-Sleep -Milliseconds 500
    if (@(Get-Tops | Where-Object { (Get-Title ([IntPtr]$_)) -like '*P5A*NP-*' }).Count -gt 0) {
        throw "leftover P5A marker window still present after cleanup - abort"
    }
}
if ($TargetKind -eq 'notepad') {
    $existingNp = @(Get-Tops | ForEach-Object { Get-Title ([IntPtr]$_) } | Where-Object { $_ -like '*- Notepad' })
    if ($existingNp.Count -gt 0) {
        throw "existing Notepad window(s) found: [$($existingNp -join '; ')] - notepad target unsafe; use -TargetKind edge or close user Notepad first"
    }
}
Write-Host "[$TargetKind] run dir: $runDir"
Write-Host "[$TargetKind] targetSubstr='$targetSubstr' (unique; user windows cannot match)"

@{ targetTitleSubstr = $targetSubstr; durationSec = $DurationSec } |
    ConvertTo-Json | Set-Content (Join-Path $scenarioDir 'window-perch-live-config.json') -Encoding UTF8

# ---- 发现并归属确认目标（复用 M-14 规则；零写入直到归属确认）----
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
            $owned = $(if ($TargetKind -eq 'notepad') {
                           $null -ne $cProc -and $cProc.ProcessName -eq 'Notepad' -and
                           $cPid -ne $PID -and $cProc.StartTime -ge $script:t0.AddSeconds(-2)
                       }
                       else {
                           $null -ne $cProc -and $cPid -ne $PID -and (Test-EdgeHostOwned $cPid) -and
                           $cProc.StartTime -ge $script:t0.AddSeconds(-2)
                       })
            if (-not $owned) { continue }
            return @{ Hwnd = [IntPtr]$h; Pid = $cPid }
        }
    }
    return $null
}

$probe = $null
$cover = $null
try {
    # 1. 启动目标
    $targetProc = Start-Target
    Write-Host "[$TargetKind] target start issued pid=$($targetProc.Id)"
    $found = Find-OwnedTarget @()
    if ($null -eq $found) { throw 'no marker-owned target window appeared - aborting (user windows untouched)' }
    $hwnd = $found.Hwnd
    Start-Sleep -Milliseconds 1500
    if ((Get-Title $hwnd) -notlike "*$targetSubstr*") {
        throw "target title drifted off this run's marker - aborting (user windows untouched)"
    }
    Write-Host "[$TargetKind] target ownership verified: hwnd=$(HwndHex $hwnd) pid=$($found.Pid)"
    Add-Check '目标标记与归属确认（驱动侧零写入前提）' $true "hwnd=$(HwndHex $hwnd) pid=$($found.Pid) marker=$targetSubstr"

    if (-not (Find-ClearSpot $hwnd)) {
        throw 'no clear spot for target top edge (user topmost windows cover all candidates) - aborting'
    }

    # 2. 启动探针（真实宠物窗口）并等心跳
    $probe = Start-Process -FilePath $probeExe -ArgumentList "`"$runDir`"", 'window-perch-live', "`"$resultFile`"" -PassThru -WindowStyle Hidden
    Write-Host "[$TargetKind] probe started pid=$($probe.Id)"
    $stDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $stDeadline -and $null -eq (Get-State)) { Start-Sleep -Milliseconds 300 }
    if ($null -eq (Get-State)) { throw 'probe state file not appearing - aborting' }

    # 3. 正式入口选窗：pick-entry + 真实左键点击 → landed，脚点贴上缘中点
    # 基线必须在选窗前取：landed 事件在 pick 成功瞬间即写入，
    # 选窗后才取基线会把已发生的事件排除掉（M-16 实测时序缺陷）。
    $baseMs1 = Get-LastEventMs
    $pick1 = Invoke-PickEntry $hwnd -UseRealMenu:$RealMenu
    $landed1 = Wait-Event 'landed' 15 $baseMs1
    $foot1 = Test-FootOnTopEdge $hwnd
    Add-Check '正式入口选窗：pick-entry+真实左键点击 → landed 脚点贴上缘中点' ($pick1.Ok -and $null -ne $landed1 -and $foot1.Ok) "pick=$($pick1.Ok) $($pick1.Detail) landed=$($null -ne $landed1) $($foot1.Detail)"
    if (-not $pick1.Ok -or $null -eq $landed1) { throw 'first pick-entry failed - aborting' }

    # 4. 移动目标 → follow（正式入口选中的绑定与 P5A-001 跟随语义一致）
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, ($script:anchorX + 100), ($script:anchorY + 50), 800, 500, 0x0004) | Out-Null
    $wm = Get-LastEventMs
    $follow = Wait-Event 'follow' 12 $wm
    Start-Sleep -Milliseconds 600
    $foot2 = Test-FootOnTopEdge $hwnd
    Add-Check '跟随：目标移动后脚点贴新上缘' ($null -ne $follow -and $foot2.Ok) "follow=$($null -ne $follow) $($foot2.Detail)"

    # 5. 盖住落脚区 → occluded-detach，宠物停在原地
    $stBeforeOcc = Get-State
    $r2 = New-Object M15W+RECT
    [M15W]::GetWindowRect($hwnd, [ref]$r2) | Out-Null
    $cover = New-Object System.Windows.Forms.Form
    $cover.Text = "P5A003Cover-$ts"
    $cover.StartPosition = 'Manual'
    $cover.Left = $r2.L + 40; $cover.Top = $r2.T - 5
    $cover.Width = [Math]::Min(500, ($r2.R - $r2.L) - 80); $cover.Height = 260
    $cover.TopMost = $true
    $wm = Get-LastEventMs
    $cover.Show()
    [M15W]::SetWindowPos($cover.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
    [M15W]::SetWindowPos($hwnd, $cover.Handle, 0, 0, 0, 0, 0x0013) | Out-Null
    $occ = Wait-Event 'occluded-detach' 12 $wm
    Start-Sleep -Milliseconds 600
    $stAfterOcc = Get-State
    $stayOcc = ($null -ne $stBeforeOcc -and $null -ne $stAfterOcc -and
        [Math]::Abs($stAfterOcc.petFootPx[0] - $stBeforeOcc.petFootPx[0]) -le 2 -and
        [Math]::Abs($stAfterOcc.petFootPx[1] - $stBeforeOcc.petFootPx[1]) -le 2)
    Add-Check '遮挡脱离：落脚处被盖 occluded-detach 且宠物停在原地' ($null -ne $occ -and $stayOcc -and -not $stAfterOcc.perchActive) "fraction=$($occ.fraction) stay=$stayOcc active=$($stAfterOcc.perchActive)"
    $cover.Hide(); $cover.Dispose(); $cover = $null
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 600

    # 6. 正式入口重选 → landed
    $baseMs2 = Get-LastEventMs
    $pick2 = Invoke-PickEntry $hwnd -UseRealMenu:$RealMenu
    $landed2 = Wait-Event 'landed' 15 $baseMs2
    $foot3 = Test-FootOnTopEdge $hwnd
    Add-Check '遮挡解除后正式入口重选落位' ($pick2.Ok -and $null -ne $landed2 -and $foot3.Ok) "pick=$($pick2.Ok) $($pick2.Detail) landed=$($null -ne $landed2) $($foot3.Detail)"

    # 7. 关闭目标 → detached(WindowGone)，宠物停在原地
    $stBeforeClose = Get-State
    $wm = Get-LastEventMs
    [M15W]::SendMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    # 脏标签的行内保存提示会挡住 WM_CLOSE（run-115352 实测）；仅当提示把该
    # 窗口置于前台时发送 Alt+N（不保存），避免按键打进用户窗口。
    $closeDeadline = (Get-Date).AddSeconds(2.5)
    while ((Get-Date) -lt $closeDeadline -and [M15W2]::IsWindow($hwnd)) {
        if ([M15W2]::GetForegroundWindow() -ne $hwnd) {
            [M15W]::SetForegroundWindow($hwnd) | Out-Null
        }
        if ([M15W2]::GetForegroundWindow() -eq $hwnd) { [M16K2]::AltN() }
        Start-Sleep -Milliseconds 400
    }
    $detGone = Wait-Event 'detached' 12 $wm
    $goneOk = $false; $goneDetail = 'no detached event'
    if ($detGone) {
        $goneDetail = "reason=$($detGone.reason)"
        $goneOk = ($detGone.reason -eq 'WindowGone')
        if (-not $goneOk) {
            $deadlineGone = (Get-Date).AddSeconds(8)
            while ((Get-Date) -lt $deadlineGone) {
                if (-not [M15W2]::IsWindow($hwnd)) { $goneOk = $true; $goneDetail += ' + IsWindow=false'; break }
                Start-Sleep -Milliseconds 200
            }
        }
    }
    Start-Sleep -Milliseconds 600
    $stAfterClose = Get-State
    $stayClose = ($null -ne $stBeforeClose -and $null -ne $stAfterClose -and
        [Math]::Abs($stAfterClose.petFootPx[0] - $stBeforeClose.petFootPx[0]) -le 2 -and
        [Math]::Abs($stAfterClose.petFootPx[1] - $stBeforeClose.petFootPx[1]) -le 2)
    Add-Check '失效脱离：关闭 detached(WindowGone) 且宠物停在原地' ($goneOk -and $stayClose -and -not $stAfterClose.perchActive) "$goneDetail stay=$stayClose active=$($stAfterClose.perchActive)"

    # 8. 重开目标（标记 + 归属确认）+ 正式入口选窗 → landed
    # 关闭若仍失败（保存提示未消），先清掉卡住的标记目标再重开，避免整轮
    # 在此 FATAL 中止（run-115352 教训：残留导致后续 5 项全部丢失）。
    if ([M15W2]::IsWindow($hwnd)) {
        Write-Host "[$TargetKind] step8: target still open after close attempt - running marker cleanup before reopen"
        Invoke-CleanupMarkerWindows
        Start-Sleep -Milliseconds 500
    }
    Start-Target | Out-Null
    $found2 = Find-OwnedTarget @($hwnd.ToInt64())
    if ($null -eq $found2) { throw 'no marker-owned reopened window - aborting' }
    $hwnd2 = $found2.Hwnd
    Write-Host "[$TargetKind] reopened target verified: hwnd=$(HwndHex $hwnd2) pid=$($found2.Pid)"
    [M15W]::SetWindowPos($hwnd2, [IntPtr]::Zero, $script:anchorX, $script:anchorY, 800, 500, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 600
    $baseMs3 = Get-LastEventMs
    $pick3 = Invoke-PickEntry $hwnd2 -UseRealMenu:$RealMenu
    $landed3 = Wait-Event 'landed' 15 $baseMs3
    $foot4 = Test-FootOnTopEdge $hwnd2
    Add-Check '重开后正式入口选窗落位' ($pick3.Ok -and $null -ne $landed3 -and $foot4.Ok) "pick=$($pick3.Ok) $($pick3.Detail) landed=$($null -ne $landed3) $($foot4.Detail)"

    # 9. 真实鼠标拖动宠物 → drag-preempted；随后移动目标 → 无 follow 不回拉
    $stDragBase = Get-State
    $petRect = $stDragBase.petRectPx
    $petHwnd = [IntPtr]::Zero
    foreach ($h in Get-Tops) {
        $p = [uint32]0
        [M15W2]::GetWindowThreadProcessId([IntPtr]$h, [ref]$p) | Out-Null
        if ($p -ne [uint32]$probe.Id) { continue }
        $pr = New-Object M15W+RECT
        [M15W]::GetWindowRect([IntPtr]$h, [ref]$pr) | Out-Null
        if ([Math]::Abs($pr.L - $petRect[0]) -le 3 -and [Math]::Abs($pr.T - $petRect[1]) -le 3) { $petHwnd = [IntPtr]$h; break }
    }
    if ($petHwnd -eq [IntPtr]::Zero) {
        Add-Check '拖动接管：真实鼠标拖动宠物 drag-preempted（拖走后不回拉）' $false 'pet window not found by probe pid'
    } else {
        function Find-PetGrabPoint {
            $petW = $petRect[2] - $petRect[0]; $petH = $petRect[3] - $petRect[1]
            $cands = @()
            foreach ($fy in 0.85, 0.75, 0.65, 0.55, 0.45, 0.35, 0.25, 0.15) {
                foreach ($fx in 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8) {
                    $cands += ,@(($petRect[0] + [int]($petW * $fx)), ($petRect[1] + [int]($petH * $fy)))
                }
            }
            foreach ($c in $cands) {
                if (-not (Test-PointOnScreen $c[0] $c[1])) { continue }
                $tp = New-Object M15Hit+POINT
                $tp.X = $c[0]; $tp.Y = $c[1]
                if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($tp), 2) -eq $petHwnd) { return $c }
            }
            for ($gy = $petRect[1] + 5; $gy -lt $petRect[3] - 5; $gy += 10) {
                for ($gx = $petRect[0] + 5; $gx -lt $petRect[2] - 5; $gx += 10) {
                    if (-not (Test-PointOnScreen $gx $gy)) { continue }
                    $tp = New-Object M15Hit+POINT
                    $tp.X = $gx; $tp.Y = $gy
                    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($tp), 2) -eq $petHwnd) { return @($gx, $gy) }
                }
            }
            return $null
        }
        $grab = Find-PetGrabPoint
        $posOk = ($null -ne $grab)
        if ($posOk) {
            $grabX = $grab[0]; $grabY = $grab[1]
            for ($try = 1; $try -le 5; $try++) {
                [M15W2]::SetCursorPos($grabX, $grabY) | Out-Null
                $cp = New-Object M15W2+POINT
                [M15W2]::GetCursorPos([ref]$cp) | Out-Null
                if ([Math]::Abs($cp.X - $grabX) -le 2 -and [Math]::Abs($cp.Y - $grabY) -le 2) { break }
                $posOk = $false; Start-Sleep -Milliseconds 300
            }
        }
        if ($posOk) {
            $hitPt2 = New-Object M15Hit+POINT
            $hitPt2.X = $grabX; $hitPt2.Y = $grabY
            if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hitPt2), 2) -ne $petHwnd) { $posOk = $false }
        }
        if (-not $posOk) {
            Add-Check '拖动接管：真实鼠标拖动宠物 drag-preempted（拖走后不回拉）' $false "skipped: cursor injection ineffective (grab=$($grab -join ',')) - user mouse active or pet body blocked"
        } else {
            $wm = Get-LastEventMs
            [M15W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
            Start-Sleep -Milliseconds 120
            # 慢拖（P2-004 纪律）：40ms/14px 的大步会被判定为甩出，
            # 松手后 P2-002 物理继续滑行，footFinal 偏离松手点
            # （094705 轮实测：拖 168px 滑到 -1331px 外）。改为小步慢移，
            # 松手前静止 800ms 让速度衰减为零，确保宠物停在松手处。
            for ($i = 1; $i -le 10; $i++) {
                [M15W2]::SetCursorPos(($grabX + $i * 8), ($grabY + $i * 6)) | Out-Null
                Start-Sleep -Milliseconds 90
            }
            Start-Sleep -Milliseconds 800
            [M15W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
            $dragEv = Wait-Event 'drag-preempted' 12 $wm
            Start-Sleep -Milliseconds 1000
            $stAfterDrag = Get-State
            $moved = ($null -ne $stAfterDrag -and
                [Math]::Abs($stAfterDrag.petFootPx[0] - $stDragBase.petFootPx[0]) -gt 10)
            $cancelOk = ($stAfterDrag.dragCancelledHwnd -eq (HwndHex $hwnd2))
            Add-Check '拖动接管：真实鼠标拖动宠物 drag-preempted' ($null -ne $dragEv -and -not $stAfterDrag.perchActive -and $cancelOk -and $moved) "dragEv=$($null -ne $dragEv) active=$($stAfterDrag.perchActive) dragCancelled=$($stAfterDrag.dragCancelledHwnd) target=$(HwndHex $hwnd2) moved=$moved"

            # 移动目标 → 无 follow、脚点不回拉
            [M15W]::SetWindowPos($hwnd2, [IntPtr]::Zero, 1600, 900, 800, 500, 0x0004) | Out-Null
            Start-Sleep -Milliseconds 1600
            $stFinal = Get-State
            $pullBack = @(Read-Events | Where-Object { $_.tMs -gt $wm -and ($_.type -eq 'follow' -or $_.type -eq 'landed') })
            $noPull = ($pullBack.Count -eq 0 -and $null -ne $stFinal -and
                [Math]::Abs($stFinal.petFootPx[0] - $stAfterDrag.petFootPx[0]) -le 2 -and
                [Math]::Abs($stFinal.petFootPx[1] - $stAfterDrag.petFootPx[1]) -le 2 -and
                -not $stFinal.perchActive)
            Add-Check '拖走后不回拉：移动目标后无 follow 且宠物留在松手处' $noPull "followAfterDrag=$($pullBack.Count) footFinal=[$($stFinal.petFootPx -join ',')] footAtDrop=[$($stAfterDrag.petFootPx -join ',')] active=$($stFinal.perchActive)"
        }
    }

    # 10. 正式入口再选（显式新选择不受拖动黑名单限制）→ landed
    # 第 9 步把目标移到 (1600,900) 大半屏外，落点（上缘中点）在屏幕
    # 外会被产品按规格拒绝（rejected-landing 正确行为）；再选前把
    # 目标移回锚点在屏位置。
    [M15W]::SetWindowPos($hwnd2, [IntPtr]::Zero, $script:anchorX, $script:anchorY, 800, 500, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 600
    $baseMs4 = Get-LastEventMs
    $pick4 = Invoke-PickEntry $hwnd2 -UseRealMenu:$RealMenu
    $landed4 = Wait-Event 'landed' 15 $baseMs4
    $foot5 = Test-FootOnTopEdge $hwnd2
    Add-Check '拖动后正式入口再选落位（显式新选择不受黑名单限制）' ($pick4.Ok -and $null -ne $landed4 -and $foot5.Ok) "pick=$($pick4.Ok) $($pick4.Detail) landed=$($null -ne $landed4) $($foot5.Detail)"

    # 11. leave-entry → detached(user-left)，宠物留在当前位置；随后移动目标 → 无 follow
    $stBeforeLeave = Get-State
    $baseMsLeave = Get-LastEventMs
    if ($RealMenu) {
        $menuLeave = Invoke-RealContextMenu 'Leave Perch'
        Write-Host "[$TargetKind] real-menu Leave Perch: $($menuLeave.Detail)"
        if (-not $menuLeave.Ok) {
            Add-Check '主动离开：真实菜单 Leave Perch 执行失败' $false $menuLeave.Detail
        }
    } else {
        Send-Command 'leave-entry'
    }
    $leaveDet = Wait-Event 'detached' 12 $baseMsLeave { $_.reason -eq 'user-left' }
    Start-Sleep -Milliseconds 600
    $stAfterLeave = Get-State
    $stayLeave = ($null -ne $stBeforeLeave -and $null -ne $stAfterLeave -and
        [Math]::Abs($stAfterLeave.petFootPx[0] - $stBeforeLeave.petFootPx[0]) -le 2 -and
        [Math]::Abs($stAfterLeave.petFootPx[1] - $stBeforeLeave.petFootPx[1]) -le 2)
    $wmLeave = Get-LastEventMs
    [M15W]::SetWindowPos($hwnd2, [IntPtr]::Zero, ($script:anchorX + 200), ($script:anchorY + 100), 800, 500, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 1600
    $stLeaveMove = Get-State
    $followAfterLeave = @(Read-Events | Where-Object { $_.tMs -gt $wmLeave -and ($_.type -eq 'follow' -or $_.type -eq 'landed') })
    $leaveNoPull = ($followAfterLeave.Count -eq 0 -and $null -ne $stLeaveMove -and
        [Math]::Abs($stLeaveMove.petFootPx[0] - $stAfterLeave.petFootPx[0]) -le 2 -and
        [Math]::Abs($stLeaveMove.petFootPx[1] - $stAfterLeave.petFootPx[1]) -le 2)
    Add-Check '主动离开：leave detached(user-left) 宠物留在原地，目标移动不拉走' ($null -ne $leaveDet -and $stayLeave -and -not $stAfterLeave.perchActive -and $leaveNoPull) "$(if ($RealMenu) {'viaRealMenu'} else {'viaCommand'}) det=$($null -ne $leaveDet) stay=$stayLeave active=$($stAfterLeave.perchActive) followAfterLeave=$($followAfterLeave.Count)"

    # 12. 结束
    Send-Command 'quit'
    if (-not $probe.WaitForExit(30 * 1000)) { Stop-Process -Id $probe.Id -Force }
    Write-Host "[$TargetKind] probe exited code=$($probe.ExitCode)"
    $res = $(if (Test-Path $resultFile) { Get-Content $resultFile -Raw | ConvertFrom-Json } else { $null })
    Add-Check '探针 exitCode=0' ($probe.ExitCode -eq 0 -and $null -ne $res -and $res.exitCode -eq 0) "probe exit=$($probe.ExitCode) result exitCode=$($res.exitCode)"
}
catch {
    Write-Host "[$TargetKind] FATAL: $($_.Exception.GetType().Name): $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
    Write-Host "[$TargetKind] FATAL position: $($_.InvocationInfo.PositionMessage)"
    throw
}
finally {
    if ($cover) { $cover.Dispose() }
    if ($probe -and -not $probe.HasExited) { Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue }
    Invoke-CleanupMarkerWindows
    $profilePath = Join-Path $runDir 'edge-profile'
    $edgeProcs = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profilePath) })
    foreach ($p in $edgeProcs) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    if ($edgeProcs.Count -gt 0) { Write-Host "[$TargetKind] cleaned $($edgeProcs.Count) msedge process(es) of our profile" }
    if ($script:checks.Count -eq 0) {
        Write-Host "[$TargetKind] ABORTED before any check - no checklist"
        Stop-Transcript | Out-Null
        exit 1
    }
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Set-Content (Join-Path $runDir 'm16-checklist.txt')
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $allPass = -not ($script:checks | Where-Object { $_.Result -eq 'FAIL' })
    Write-Host "[$TargetKind] checklist: $(if ($allPass) { 'ALL PASS' } else { 'HAS FAIL' }) -> $runDir"
    Stop-Transcript | Out-Null
    if (-not $allPass) { exit 1 }
}
