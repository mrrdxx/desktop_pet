# P5B-003 / R9 阶段二：攀爬动作/镜像观测接线实机验证驱动（针对性，不重跑 M-17/M-18 全套）。
# 证据写入 .temp\p5b-003\r9\run-<时间戳>-fixture\。
#
# 目的（对应 docs\reviews\P5B-003-review.md R9 段 205 行）：
#   在真实宠物窗口（生产 CreatePetWindow 路径）下验证左右侧接近→贴边→上缘→land 正常
#   回 idle 的完整动作/镜像链：
#     · 阶段动作观测：心跳 currentAction（climb_side/climb_edge/climb_land → 非 climb_*）
#     · 朝向镜像观测：心跳 climbMirrorScaleX（目标在左=1 不镜像；在右=-1 镜像）
#     · 路线/栖息状态：climbActive/progress/climbTargetHwnd/perchActive/perchTargetHwnd
#     · 必要渲染帧：capture 命令落 frames/*.png（120×120 默认尺寸，RenderTargetBitmap）
#     · hover 不抢占、land 期间 暂停/拖动/找回/目标失效 清理
# 证据分层：探针事件/ 心跳字段/ 渲染帧 = 生产路径实机观测；对应故障组合（如目标失效、
# hover 正反例）已在自动测试 Reviewer_* 覆盖，实机项只做真实触发观察并逐项标注来源，
# 不把自动测试现象伪装成真实鼠标现象。
#
# 命令流（探针 window-climb-live）：start（按标题找合格快照起攀）/ pause / resume /
#   recover（生产 RecoverPet）/ capture <name> / quit 。起攀入口用生产真实右键菜单
#   「Climb on Window」+ 真实左键点击（TrackPopupMenuEx 生产路径），与 M-18 一致。
#
# 目标锁定纪律与真实鼠标注入纪律同 M-18：本轮独有标记 + 精确启动 PID + StartTime
# 归属确认；SetCursorPos 回读±2 + WindowFromPoint 根窗口前置校验；每个 attempt 前查
# 探针存活；诱饵存活证明清理范围不越界。占鼠标前须用户明确允许。
#
# 用法（仓库根目录，pwsh 7 -File 独立进程运行）：
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003R9Live.ps1 -DurationSec 900

param(
    [string]$FixtureTitle,
    [string]$ProbePath,
    [int]$DurationSec = 900,
    [switch]$SelfTest,
    # R15：窄范围复验入口。逗号分隔检查编号，如 "-OnlyChecks 3,4,7" 仅跑侧段
    # 与 land 拖动关键链（①②底盘、⑩收尾与诱饵纪律始终执行），跳过其他检查；
    # 摘要/退出码明确标记 narrow，不伪装成原生 10/10。留空=全量 10 项。
    [string]$OnlyChecks = ''
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
$runDir = Join-Path $repo ".temp\p5b-003\r9\run-$ts-fixture"
$scenarioDir = Join-Path $runDir 'window-climb-live'
$resultFile = Join-Path $runDir 'probe-result.json'
$eventsFile = Join-Path $scenarioDir 'climb-live-events.jsonl'
$stateFile = Join-Path $scenarioDir 'climb-live-state.json'
$commandsFile = Join-Path $scenarioDir 'climb-live-commands.txt'
$framesDir = Join-Path $scenarioDir 'frames'
$probeExe = if ($ProbePath) { (Resolve-Path -LiteralPath $ProbePath).Path } else { Join-Path $repo 'tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe' }
# SelfTest 模式：不占鼠标、不建 run 产物目录，也不启动 transcript（避免空证据目录噪音）。
if (-not $SelfTest) {
    New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
    New-Item -ItemType Directory -Force -Path $framesDir | Out-Null
    Start-Transcript -Path (Join-Path $runDir 'driver-transcript.log') | Out-Null
}

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
Write-Host "[R9] driver thread DPI awareness = $threadAwareness (2=PerMonitor; required for physical px)"
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
# R16：取最近一次起攀（climb-started，目标==$targetHex）的 chainId；无则 $null。
# 驱动 ⑦ 以此关联 land 拖动后的 perch-detached 空目标取消（不依赖取消事件
# 的 targetHwnd，合法取消时其为 null）。
function Get-LastStartedChainId([string]$targetHex) {
    $started = @(Read-Events | Where-Object {
        $_.type -eq 'climb-started' -and
        $null -ne $_.PSObject.Properties['chainId'] -and
        $_.targetHwnd -eq $targetHex
    })
    if ($started.Count -eq 0) { return $null }
    $started[-1].chainId
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
# R18：找自有安全鼠位（避开宠物与目标，悬停宠物会触发 curious 等临时表达）。
# 候选点按"虚拟屏右上、左上、右下"依次尝试，命中非宠物/非目标区即返回。
# R19：每个坐标算式分别加括号，确保返回恰好两个标量数值（旧式逗号+算术
# 会被 PowerShell 解释为数组参与减法，抛 op_Subtraction 异常）。
function Get-SafeCursorPoint {
    $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $cands = @(
        @(($vs.X + $vs.Width - 8), ($vs.Y + 8)),
        @(($vs.X + 8), ($vs.Y + 8)),
        @(($vs.X + $vs.Width - 8), ($vs.Y + $vs.Height - 8))
    )
    $st = Get-State
    $petR = if ($null -ne $st -and $null -ne $st.petRectPx) { @($st.petRectPx) } else { $null }
    $fb = $null
    if ($null -ne $script:ownedTargetHwnd -and $script:ownedTargetHwnd -ne [IntPtr]::Zero) { $fb = Get-FrameBounds $script:ownedTargetHwnd }
    foreach ($c in $cands) {
        $inPet = $null -ne $petR -and $c[0] -ge $petR[0] -and $c[0] -le $petR[2] -and $c[1] -ge $petR[1] -and $c[1] -le $petR[3]
        $inTgt = $null -ne $fb -and $c[0] -ge $fb.L -and $c[0] -le $fb.R -and $c[1] -ge $fb.T -and $c[1] -le $fb.B
        if (-not $inPet -and -not $inTgt) { return $c }
    }
    $cands[0]
}
function Test-ProbeAlive {
    if ($null -ne $script:probe -and $script:probe.HasExited) {
        Write-Host "[R9] probe exited early (pid $($script:probe.Id)) - driver stalled past probe lifetime"
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
function Add-Check([string]$name, [bool]$pass, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Item = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} : {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $name, $detail)
}
# 动作/镜像采样摘要（R9 新增字段）——detail 即证据。
function Format-ActionState($st) {
    if ($null -eq $st) { return 'state=n/a' }
    $act = $st.currentAction
    $mir = $st.climbMirrorScaleX
    if ($null -eq $act) { $act = 'n/a(旧探针?)' }
    if ($null -eq $mir) { $mir = 'n/a(旧探针?)' }
    "currentAction=$act climbMirrorScaleX=$mir climbActive=$($st.climbActive) perchActive=$($st.perchActive) progress=$($st.progress) foot=[$($st.petFootPx -join ',')]"
}
# 等心跳里 currentAction 变为集合内某值（含 climb_* 前缀匹配）。
function Wait-Action([scriptblock]$match, [int]$timeoutSec = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $st = Get-State
        if ($null -ne $st -and $null -ne $st.currentAction) {
            if (& $match $st.currentAction) { return $st }
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}
function Test-ClimbAction([string]$a) { $a -like 'climb_*' }

# ================= R10 F-06：纯判定函数（可被 -SelfTest 无鼠标受控验证） =================
# 这些函数无副作用，只对传入状态/清单作判定；真实驱动与 SelfTest 共用同一实现，
# 避免"受控检查"与"实机判定"两套逻辑不一致。

# 新鲜度：心跳 tMs 必须严格大于调用方已知的最新 tMs，否则按旧心跳处理（必失败）。
function Test-FreshState($st, [long]$AfterMs) {
    if ($null -eq $st -or $null -eq $st.tMs) { return @{ Ok = $false; Reason = 'no tMs' } }
    if ([long]$st.tMs -le $AfterMs) { return @{ Ok = $false; Reason = "stale tMs=$($st.tMs) <= $AfterMs" } }
    @{ Ok = $true; Reason = 'fresh' }
}
# 等待新鲜心跳：返回 tMs > AfterMs 的状态；窗口内未出现则返回 null。
function Wait-FreshState([long]$AfterMs, [int]$TimeoutSec = 5) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $st = Get-State
        if ((Test-FreshState $st $AfterMs).Ok) { return $st }
        Start-Sleep -Milliseconds 150
    }
    return $null
}
# 干净空闲终态：字段齐全（缺失即失败）、非 climb_*、镜像==1、climb/perch 均不激活。
# R10：currentAction=null 或 climbMirrorScaleX=null 一律 FAIL，不再按"可接受"解释。
function Test-NonClimbIdle($st) {
    if ($null -eq $st) { return @{ Ok = $false; Reason = 'state null' } }
    if ($null -eq $st.currentAction) { return @{ Ok = $false; Reason = 'currentAction missing' } }
    if ($null -eq $st.climbMirrorScaleX) { return @{ Ok = $false; Reason = 'climbMirrorScaleX missing' } }
    if ($st.climbActive) { return @{ Ok = $false; Reason = "still climbActive ($($st.currentAction))" } }
    if ($st.perchActive) { return @{ Ok = $false; Reason = 'still perchActive' } }
    if (Test-ClimbAction $st.currentAction) { return @{ Ok = $false; Reason = "action still climb_* ($($st.currentAction))" } }
    if ([double]$st.climbMirrorScaleX -ne 1) { return @{ Ok = $false; Reason = "mirror=$($st.climbMirrorScaleX) != 1" } }
    @{ Ok = $true; Reason = 'clean idle' }
}
# 对一次心跳给出 land 判定：in-land / land-finished / fields-missing / not-climbing / other-climb。
# -RequireInLand 时 land-finished（动作已自然结束）由调用方按失败处理（未覆盖，不得放行）。
function Get-LandVerdict($st, [switch]$RequireInLand) {
    if ($null -eq $st) { return 'no-state' }
    if ($null -eq $st.currentAction -or $null -eq $st.climbMirrorScaleX) { return 'fields-missing' }
    if ($st.currentAction -eq 'climb_land') { return 'in-land' }
    if (-not (Test-ClimbAction $st.currentAction)) {
        if ($RequireInLand -and -not $st.climbActive) { return 'land-finished' }
        return 'not-climbing'
    }
    'other-climb'
}
# R15：land 拖动清理事件判据（纯判定，驱动与 SelfTest 共用）。
# 生产正确取消路径 StartDragging→Perch.CancelForDrag→DetachInternal→
# ReleaseClimbForPerchDetach 会先清掉 land（IsPresentingClimb 变 false），随后
# Climb.CancelForDrag 不再发 climb-drag-preempted（R15 实机证：t=71021
# sidecar land → t=71099 climb-cancelled("perch-detached")）。因此接受两类
# 同步取消证据：
#   A) climb-drag-preempted（仍保留的旧路径）；
#   B) 本次拖动窗口（sinceMs=land 确认心跳之后）内 climb-cancelled(reason=perch-detached)。
# 拒绝：旧取消（tMs<=sinceMs 的历史事件）、缺取消。只接受 reason=perch-detached，
# 不接受 recovered/paused 等其他取消冒充。
# R16：目标/链上下文关联——真实合法取消（perch-detached）在 land 交接 tracker
# 释放后 targetHwnd=null（R15 实机 4 个空目标取消），不能全局接纳空句柄取消：
# 非空 targetHwnd 仍须通过 $targetWhere（记名 owned）；空 targetHwnd 只接受
# chainId 命中本次链 $chainContext 的事件；chainId 缺失（旧探针事件）保守拒绝。
function Test-LegalDragCancel($events, [long]$sinceMs, [scriptblock]$targetWhere = $null, [object]$chainContext = $null) {
    if ($null -eq $events) { return @{ Ok = $false; Kind = 'none'; Detail = 'no events' } }
    $cand = @($events | Where-Object {
        ($_.type -eq 'climb-drag-preempted' -or
            ($_.type -eq 'climb-cancelled' -and $_.reason -eq 'perch-detached')) -and
        [long]$_.tMs -gt $sinceMs -and
        (Test-DragCancelContextMatch $_ $targetWhere $chainContext)
    })
    if ($cand.Count -eq 0) {
        $stale = @($events | Where-Object {
            ($_.type -eq 'climb-drag-preempted' -or
                ($_.type -eq 'climb-cancelled' -and $_.reason -eq 'perch-detached')) -and
            [long]$_.tMs -le $sinceMs
        })
        if ($stale.Count -gt 0) {
            return @{ Ok = $false; Kind = 'stale'; Detail = "only stale cancel: type=$($stale[-1].type) reason=$($stale[-1].reason) tMs=$($stale[-1].tMs) <= since=$sinceMs" }
        }
        return @{ Ok = $false; Kind = 'none'; Detail = "no legal drag cancel (preempted/perch-detached) after tMs>$sinceMs" }
    }
    $latest = $cand[-1]
    if ($latest.type -eq 'climb-drag-preempted') {
        return @{ Ok = $true; Kind = 'preempted'; Detail = "climb-drag-preempted @t=$($latest.tMs)" }
    }
    @{ Ok = $true; Kind = 'perch-detached'; Detail = "climb-cancelled(perch-detached) @t=$($latest.tMs) reason=$($latest.reason)" }
}
# R16：单事件目标/链上下文匹配——非空 targetHwnd 须过 $targetWhere（记名 owned）；
# 空 targetHwnd（land 交接 tracker 释放后的合法取消）只接受 chainId==本次链，
# 属性缺失（旧探针事件）一律拒绝；绝不全局接纳所有空句柄取消。
function Test-DragCancelContextMatch($ev, [scriptblock]$targetWhere, [object]$chainContext) {
    if (-not [string]::IsNullOrEmpty($ev.targetHwnd)) {
        if ($null -eq $targetWhere) { return $true }
        return [bool](& $targetWhere $ev)
    }
    if ($null -eq $chainContext) { return $false }
    $prop = $ev.PSObject.Properties['chainId']
    if ($null -eq $prop -or $null -eq $prop.Value) { return $false }
    return ([string]$prop.Value -eq [string]$chainContext)
}
# R13：与实际本地钟一致的夜间窗口判定（产品 NightSchedule 口径：23:30 含 → 06:00 不含）。
# 驱动与生产共用系统本地钟，不改系统时钟；边界按判定时刻判断。
function Test-IsNightAt([DateTime]$LocalNow) {
    $t = [TimeOnly]::FromDateTime($LocalNow)
    return ($t -ge [TimeOnly]::new(23, 30) -or $t -lt [TimeOnly]::new(6, 0))
}
# R18：起攀前置空闲判定（纯判定，驱动 Ensure-IdleForClimb 与 SelfTest 共用）。
# 只接受 idle 或"实际本地夜间"的 sleep；curious/drag 等其他动作一律拒绝；
# 路线（climbActive）与栖息（perchActive）均必须未激活；字段缺失拒绝。
# 注意：本函数只判单条状态，新鲜性（tMs 前进、非旧心跳）由调用方经
# Wait-FreshState/Test-FreshState 保证——旧心跳必须被上层新鲜检查拦下，
# 不得凭旧心跳"冒充空闲"通过本判定。
function Test-ClimbPrereqIdle($st, [DateTime]$LocalNow = [DateTime]::MinValue) {
    if ($LocalNow -eq [DateTime]::MinValue) { $LocalNow = Get-Date }
    if ($null -eq $st) { return @{ Ok = $false; Reason = 'state null' } }
    if ($null -eq $st.currentAction) { return @{ Ok = $false; Reason = 'currentAction missing' } }
    if ($null -eq $st.climbMirrorScaleX) { return @{ Ok = $false; Reason = 'climbMirrorScaleX missing' } }
    if ($st.climbActive) { return @{ Ok = $false; Reason = "route still active ($($st.currentAction))" } }
    if ($st.perchActive) { return @{ Ok = $false; Reason = "perch still active ($($st.currentAction))" } }
    if ($st.currentAction -eq 'idle') {
        return @{ Ok = $true; Reason = 'prereq idle' }
    }
    if ($st.currentAction -eq 'sleep') {
        if (-not (Test-IsNightAt $LocalNow)) {
            return @{ Ok = $false; Reason = "daytime sleep rejected (localNow=$($LocalNow.ToString('HH:mm')))" }
        }
        return @{ Ok = $true; Reason = 'prereq night sleep (local night)' }
    }
    @{ Ok = $false; Reason = "action=$($st.currentAction) not free baseline idle/night-sleep" }
}
# R11/R13：正常落位收尾判定——land 播完后的干净终态。
# 底态限定（R13 收紧）：只接受 idle，或在"实际本地夜间"接受 sleep；curious/drag 等
# 其他动作一律拒绝（sleep 不能替代进行中的攀爬阶段，也不能在白天冒充空闲底态）。
# 时段依据：未显式提供 $LocalNow 时按采样判定时刻的实际本地钟（Test-IsNightAt）；
# 镜像==1、climbActive=false（路线 inactive）与 perchActive=true（栖息仍活跃）保留。
# 与 Test-NonClimbIdle（中断清理，要求 perchActive=false）互补：正常链不允许为了过测试
# 而解除合法栖息，也不允许把仍在 land 或栖息丢失当作收尾终态；side/edge/land 观测
# 由调用方 Wait-NormalLandingIdle 两阶段保证，不在此放宽。
function Test-NormalLandingIdle($st, [DateTime]$LocalNow = [DateTime]::MinValue) {
    if ($LocalNow -eq [DateTime]::MinValue) { $LocalNow = Get-Date }
    if ($null -eq $st) { return @{ Ok = $false; Reason = 'state null' } }
    if ($null -eq $st.currentAction) { return @{ Ok = $false; Reason = 'currentAction missing' } }
    if ($null -eq $st.climbMirrorScaleX) { return @{ Ok = $false; Reason = 'climbMirrorScaleX missing' } }
    if ($st.climbActive) { return @{ Ok = $false; Reason = "still climbActive ($($st.currentAction))" } }
    if (Test-ClimbAction $st.currentAction) { return @{ Ok = $false; Reason = "action still climb_* ($($st.currentAction))" } }
    if (-not $st.perchActive) { return @{ Ok = $false; Reason = 'perch lost (正常落位后栖息应仍活跃)' } }
    if ([double]$st.climbMirrorScaleX -ne 1) { return @{ Ok = $false; Reason = "mirror=$($st.climbMirrorScaleX) != 1" } }
    if ($st.currentAction -eq 'idle') { return @{ Ok = $true; Reason = 'normal landing idle (perch active)' } }
    if ($st.currentAction -eq 'sleep') {
        if (-not (Test-IsNightAt $LocalNow)) {
            return @{ Ok = $false; Reason = "daytime sleep rejected (localNow=$($LocalNow.ToString('HH:mm')))" }
        }
        return @{ Ok = $true; Reason = 'normal landing sleep (night, perch active)' }
    }
    return @{ Ok = $false; Reason = "action=$($st.currentAction) not free baseline idle/night-sleep" }
}
# R11：正常链/hover 收尾的有界等待，两阶段：
#   阶段1：先见到 climb_land 新鲜心跳（land 真正开始播放）。调用方采样循环已在首个
#     climb_land 处 break（land 仍在播放中），可经 $LandSeenHint 直接进入阶段2；
#     若未提供 hint 却先见到"非 climb_* 且 -not climbActive"（land 未观察到就结束）→ FAIL。
#   阶段2：等一条非 climb_* 且 climbActive=false 的新鲜心跳，要求镜像==1 且栖息仍活跃
#     （正常落位后栖息不脱离），底态须为 idle 或实际本地夜间的 sleep（R13 收紧，由
#     Test-NormalLandingIdle 把关）；栖息丢失/镜像未复位/动作非空闲底态 → 立即 FAIL；
#     一直停在 land → 超时 FAIL。
# 不用 Wait-Land 普通模式的"见非 climb_* 就 break"，避免 land 未播完时提前判终态。
function Wait-NormalLandingIdle([long]$AfterMs, [bool]$LandSeenHint = $false, [int]$TimeoutSec = 10) {
    $landSeen = $LandSeenHint
    $afterMs = $AfterMs
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $st = Wait-FreshState $afterMs 1
        if ($null -ne $st) {
            $afterMs = [long]$st.tMs
            if ($null -eq $st.currentAction -or $null -eq $st.climbMirrorScaleX) {
                return @{ Ok = $false; St = $st; Detail = "fields missing at $(Format-ActionState $st)" }
            }
            if (-not $landSeen) {
                if ($st.currentAction -eq 'climb_land') { $landSeen = $true; continue }
                if (-not (Test-ClimbAction $st.currentAction) -and -not $st.climbActive) {
                    return @{ Ok = $false; St = $st; Detail = "land finished before observed: $(Format-ActionState $st)" }
                }
                continue
            }
            if (Test-ClimbAction $st.currentAction -or $st.climbActive) { continue }
            $j = Test-NormalLandingIdle $st
            if ($j.Ok) { return @{ Ok = $true; St = $st; Detail = "after land -> $(Format-ActionState $st)" } }
            return @{ Ok = $false; St = $st; Detail = "after land -> $(Format-ActionState $st) ($($j.Reason))" }
        }
        Start-Sleep -Milliseconds 150
    }
    if (-not $landSeen) {
        return @{ Ok = $false; St = $null; Detail = "no climb_land heartbeat within ${TimeoutSec}s (afterMs=$AfterMs)" }
    }
    $last = Get-State
    return @{ Ok = $false; St = $last; Detail = "no clean normal landing within ${TimeoutSec}s (last=$(Format-ActionState $last))" }
}
# 固定 10 项唯一完整性 + 全部 PASS。
function Test-CompleteChecks($checks) {
    if ($null -eq $checks) { return @{ Ok = $false; Reason = 'no checks' } }
    $items = @($checks | ForEach-Object { $_.Item })
    if ($items.Count -ne 10) { return @{ Ok = $false; Reason = "checks=$($items.Count) != 10" } }
    if (@($items | Select-Object -Unique).Count -ne 10) { return @{ Ok = $false; Reason = 'item names not unique' } }
    if (@($checks | Where-Object { $_.Result -ne 'PASS' }).Count -gt 0) { return @{ Ok = $false; Reason = 'not all PASS' } }
    @{ Ok = $true; Reason = '10 unique checks, all PASS' }
}
# R15：窄范围复验完整性——仅要求被选编号（③④⑦ 等）与始终执行的 ①②⑩ 底盘
# 存在且全 PASS；未选编号不加入清单，摘要明确标记 narrow，不伪装成原生 10/10。
# 注意：需要 Ordinal 比较——ICU 文化比较会把 ①(U+2460) 与 ⑩(U+2469) 视为
# 等价圈号数字（默认 StartsWith 会误判 '⑩ 探针' 也以 ① 开头）。
function Get-CheckNumber([string]$itemName) {
    $map = @('①', '②', '③', '④', '⑤', '⑥', '⑦', '⑧', '⑨')
    for ($i = 0; $i -lt 9; $i++) {
        if ($itemName.StartsWith($map[$i], [System.StringComparison]::Ordinal)) { return ($i + 1) }
    }
    if ($itemName.StartsWith('⑩', [System.StringComparison]::Ordinal)) { return 10 }
    0
}
function Test-NarrowChecks($checks, $onlySet) {
    if ($null -eq $checks) { return @{ Ok = $false; Reason = 'no checks' } }
    $required = @(@($onlySet | ForEach-Object { [int]$_ }) + @(1, 2, 10) | Select-Object -Unique)
    $missing = @(); $bad = @()
    foreach ($n in $required) {
        $hits = @($checks | Where-Object { (Get-CheckNumber $_.Item) -eq $n })
        if ($hits.Count -eq 0) { $missing += $n; continue }
        if (@($hits | Where-Object { $_.Result -ne 'PASS' }).Count -gt 0) { $bad += $n }
    }
    if ($missing.Count -eq 0 -and $bad.Count -eq 0) {
        return @{ Ok = $true; Reason = "narrow checks=[$($onlySet -join ',')] + ①②⑩ 全 PASS" }
    }
    @{ Ok = $false; Reason = "narrow missing=[$($missing -join ',')] fail=[$($bad -join ',')]" }
}
# 等 capture 异步消费完成：sidecar frames/<name>.json 出现、字段齐全、tMs 新鲜。
# 记录采集瞬间的真实动作/镜像/时间（记录事实，不强制阶段——左右链/hover 用）。
function Wait-CaptureSidecar([string]$name, [long]$AfterMs, [int]$TimeoutSec = 8) {
    $sidecarPath = Join-Path $framesDir ($name + '.json')
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $sidecarPath) {
            try {
                $sc = Get-Content $sidecarPath -Raw | ConvertFrom-Json
                if ($null -ne $sc.action -and $null -ne $sc.mirror -and [long]$sc.tMs -gt $AfterMs) {
                    return @{ Ok = $true; Sidecar = $sc; Path = $sidecarPath; Detail = "sidecar $name action=$($sc.action) mirror=$($sc.mirror) tMs=$($sc.tMs)" }
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 150
    }
    return @{ Ok = $false; Sidecar = $null; Path = $sidecarPath; Detail = "no valid sidecar for $name within ${TimeoutSec}s" }
}
# 中断项专用：sidecar 必须证明采集瞬间动作确为 climb_land（不能只靠文件名 land-before-*）。
function Wait-CaptureInLand([string]$name, [long]$AfterMs, [int]$TimeoutSec = 8) {
    $sidecarPath = Join-Path $framesDir ($name + '.json')
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $sidecarPath) {
            try {
                $sc = Get-Content $sidecarPath -Raw | ConvertFrom-Json
                if ($sc.action -eq 'climb_land' -and $null -ne $sc.mirror -and [long]$sc.tMs -gt $AfterMs) {
                    return @{ Ok = $true; Sidecar = $sc; Path = $sidecarPath; Detail = "sidecar $name action=$($sc.action) mirror=$($sc.mirror) tMs=$($sc.tMs)" }
                }
                if ($sc.action -ne 'climb_land') {
                    return @{ Ok = $false; Sidecar = $sc; Path = $sidecarPath; Detail = "sidecar $name captured action=$($sc.action) (expected climb_land) - frame did NOT catch land" }
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 150
    }
    return @{ Ok = $false; Sidecar = $null; Path = $sidecarPath; Detail = "no climb_land sidecar for $name within ${TimeoutSec}s" }
}

# ---- SelfTest：无鼠标受控负例检查（R10 F-06 要求，"land过期/字段缺失/暂停仍land/
#      不完整清单"等负例），全部通过返回 0，否则 1。不启动进程/窗口、不注入输入。 ----
function Invoke-SelfTest {
    $script:stTotal = 0; $script:stFail = 0
    function Assert-St([string]$name, [bool]$ok, [string]$expect) {
        $script:stTotal++
        if (-not $ok) { $script:stFail++ }
        $res = $(if ($ok) { 'PASS' } else { 'FAIL' })
        Write-Host "[R9-SELFTEST] $name : $res (expect $expect)"
    }
    function Mk($tMs, $action, $mirror, $climbActive, $perchActive) {
        [pscustomobject]@{ tMs = $tMs; currentAction = $action; climbMirrorScaleX = $mirror; climbActive = $climbActive; perchActive = $perchActive }
    }
    # 1 正例：completed 后新鲜心跳 climb_land → in-land（RequireInLand 允许）
    $v1 = Get-LandVerdict (Mk 200 'climb_land' (-1.0) $true $true) -RequireInLand
    Assert-St 'positive in-land verdict' ($v1 -eq 'in-land') 'in-land'
    # 2 负例：动作已自然结束回 idle（land 过期）→ land-finished，RequireInLand 不得放行
    $v2 = Get-LandVerdict (Mk 200 'idle' 1.0 $false $false) -RequireInLand
    Assert-St 'negative land-expired (idle after completed)' ($v2 -eq 'land-finished') 'land-finished'
    # 3 负例：关键字段缺失（currentAction=null / mirror=null）→ fields-missing，不得当成功
    $v3 = Get-LandVerdict (Mk 200 $null $null $true $false) -RequireInLand
    Assert-St 'negative fields-missing verdict' ($v3 -eq 'fields-missing') 'fields-missing'
    # 4 负例：暂停仍需在 land（旧实现"暂停冻结 land"）→ Test-NonClimbIdle 必须 FAIL
    $j4 = Test-NonClimbIdle (Mk 200 'climb_land' (-1.0) $true $true)
    Assert-St 'negative pause-still-land rejected' (-not $j4.Ok) "FAIL ($($j4.Reason))"
    # 5 负例：镜像未复位 / climb 仍激活 → FAIL
    $j5 = Test-NonClimbIdle (Mk 200 'idle' (-1.0) $true $false)
    Assert-St 'negative not-cleaned rejected (mirror=-1, climbActive)' (-not $j5.Ok) "FAIL ($($j5.Reason))"
    # 6 正例：干净空闲终态 → PASS
    $j6 = Test-NonClimbIdle (Mk 200 'idle' 1.0 $false $false)
    Assert-St 'positive clean idle' ($j6.Ok) 'PASS'
    # 7 负例：旧心跳（tMs 未前进）→ fresh 校验失败
    $f7 = Test-FreshState (Mk 100 'idle' 1.0 $false $false) 200
    Assert-St 'negative stale-heartbeat rejected' (-not $f7.Ok) "FAIL ($($f7.Reason))"
    # 8 正例：心跳前进 → fresh 通过
    $f8 = Test-FreshState (Mk 300 'idle' 1.0 $false $false) 200
    Assert-St 'positive fresh-heartbeat' ($f8.Ok) 'PASS'
    # 9 负例：10 项清单缺 1 项 → 不完整，返回失败
    $c9 = @(1..9 | ForEach-Object { [pscustomobject]@{ Item = "第$($_)项"; Result = 'PASS' } })
    $j9 = Test-CompleteChecks $c9
    Assert-St 'negative incomplete checklist (9/10)' (-not $j9.Ok) "FAIL ($($j9.Reason))"
    # 10 负例：10 项但含 FAIL → 返回失败
    $c10 = @(1..10 | ForEach-Object { [pscustomobject]@{ Item = "第$($_)项"; Result = $(if ($_ -eq 5) { 'FAIL' } else { 'PASS' }) } })
    $j10 = Test-CompleteChecks $c10
    Assert-St 'negative checklist with FAIL item' (-not $j10.Ok) 'FAIL'
    # 11 正例：10 项唯一全 PASS → 返回成功
    $c11 = @(1..10 | ForEach-Object { [pscustomobject]@{ Item = "第$($_)项"; Result = 'PASS' } })
    $j11 = Test-CompleteChecks $c11
    Assert-St 'positive complete checklist (10 PASS)' ($j11.Ok) 'PASS'
    # 12 正例：正常落位收尾——idle + 镜像1 + 栖息仍活跃（perchActive=true）→ PASS
    $j12 = Test-NormalLandingIdle (Mk 200 'idle' 1.0 $false $true)
    Assert-St 'positive normal-landing idle (perch active)' ($j12.Ok) "PASS ($($j12.Reason))"
    # 13 负例：仍在 land（climb_land 播放中）→ 不得提前判终态
    $j13 = Test-NormalLandingIdle (Mk 200 'climb_land' (-1.0) $true $true)
    Assert-St 'negative still-in-land rejected' (-not $j13.Ok) "FAIL ($($j13.Reason))"
    # 14 负例：正常栖息丢失（perchActive=false）→ 与中断清理区分，不得放行
    $j14 = Test-NormalLandingIdle (Mk 200 'idle' 1.0 $false $false)
    Assert-St 'negative perch-lost rejected' (-not $j14.Ok) "FAIL ($($j14.Reason))"
    # 15 负例：curious 非空闲底态（白天，perch 活跃）→ 正常收尾不得放行（R13：拒绝其他动作）
    $j15 = Test-NormalLandingIdle (Mk 200 'curious' 1.0 $false $true) ([datetime]'2026-11-24 12:00:00')
    Assert-St 'negative curious rejected (day, perch active)' (-not $j15.Ok) "FAIL ($($j15.Reason))"
    # 16 负例：drag 非空闲底态（白天，perch 活跃）→ 正常收尾不得放行（R13：拒绝其他动作）
    $j16 = Test-NormalLandingIdle (Mk 200 'drag' 1.0 $false $true) ([datetime]'2026-11-24 12:00:00')
    Assert-St 'negative drag rejected (day, perch active)' (-not $j16.Ok) "FAIL ($($j16.Reason))"
    # 17 负例：白天 sleep 不得作为正常底态 → 拒绝（R13：仅实际本地夜间接受 sleep）
    $j17 = Test-NormalLandingIdle (Mk 200 'sleep' 1.0 $false $true) ([datetime]'2026-11-24 12:00:00')
    Assert-St 'negative daytime-sleep rejected' (-not $j17.Ok) "FAIL ($($j17.Reason))"
    # 18 正例：夜间 00:30 sleep + 镜像1 + climbActive=false + perchActive=true → PASS（R13）
    $j18 = Test-NormalLandingIdle (Mk 200 'sleep' 1.0 $false $true) ([datetime]'2026-11-24 00:30:00')
    Assert-St 'positive night-sleep accepted (perch active)' ($j18.Ok) "PASS ($($j18.Reason))"
    # ---- R15：Test-LegalDragCancel 受控正反例（land 拖动清理判据） ----
    function MkEv($tMs, $type, $reason, $targetHwnd, $chainId) {
        $s = [pscustomobject]@{ tMs = $tMs; type = $type; reason = $reason; targetHwnd = $targetHwnd }
        if ($null -ne $chainId) { $s | Add-Member -NotePropertyName chainId -NotePropertyValue $chainId -Force }
        $s
    }
    # 19 正例：本次拖动窗口内 climb-cancelled(reason=perch-detached)（非空目标）→ 接受（生产正确路径）
    $c19 = Test-LegalDragCancel @(MkEv 300 'climb-cancelled' 'perch-detached' '0xABC'), (MkEv 200 'climb-completed' $null '0xABC') 250
    Assert-St 'positive perch-detached cancel accepted' ($c19.Ok -and $c19.Kind -eq 'perch-detached') "PASS kind=perch-detached ($($c19.Detail))"
    # 20 正例：climb-drag-preempted（旧路径仍接受）→ 接受
    $c20 = Test-LegalDragCancel @(MkEv 300 'climb-drag-preempted' $null '0xABC') 250
    Assert-St 'positive climb-drag-preempted accepted' ($c20.Ok -and $c20.Kind -eq 'preempted') "PASS kind=preempted ($($c20.Detail))"
    # 21 负例：取消事件早于 land 确认（旧事件）→ 拒绝（不得只接受任意历史取消）
    $c21 = Test-LegalDragCancel (MkEv 200 'climb-cancelled' 'perch-detached' '0xABC'), (MkEv 210 'climb-completed' $null '0xABC') 250
    Assert-St 'negative stale cancel rejected' (-not $c21.Ok -and $c21.Kind -eq 'stale') "FAIL kind=stale ($($c21.Detail))"
    # 22 负例：完全缺取消事件 → 拒绝
    $c22 = Test-LegalDragCancel (MkEv 210 'climb-completed' $null '0xABC') 200
    Assert-St 'negative missing cancel rejected' (-not $c22.Ok -and $c22.Kind -eq 'none') "FAIL kind=none ($($c22.Detail))"
    # 23 负例：窗口内只有其他 reason 的取消（非 perch-detached）→ 拒绝
    $c23 = Test-LegalDragCancel (MkEv 300 'climb-cancelled' 'paused' '0xABC') 250
    Assert-St 'negative wrong-reason cancel rejected' (-not $c23.Ok) "FAIL ($($c23.Detail))"
    # ---- R16：空目标取消的链上下文关联（监理唯一阻断） ----
    # 24 正例：R15 原始样本参数化（production 同款：sinceMs=land 确认 t=71021、
    #   记名 owned 目标 0x31444、链 chainId=5）——t=71099 perch-detached 取消
    #   targetHwnd=null 但 chainId 命中本次链 → 接受（真实合法取消被关联，监理样本）。
    $ev24 = @(
        (MkEv 71099 'climb-cancelled' 'perch-detached' $null 5),
        (MkEv 71021 'climb-completed' $null '0x31444')
    )
    $c24 = Test-LegalDragCancel $ev24 71021 { $_.targetHwnd -eq '0x31444' } 5
    Assert-St 'positive R15 empty-target same-chain cancel accepted' ($c24.Ok -and $c24.Kind -eq 'perch-detached') "PASS kind=perch-detached ($($c24.Detail))"
    # 25 负例：空目标取消但 chainId 属其他链 → 拒绝（不全局接纳空句柄取消）
    $ev25 = @(
        (MkEv 71099 'climb-cancelled' 'perch-detached' $null 4),
        (MkEv 71021 'climb-completed' $null '0x31444')
    )
    $c25 = Test-LegalDragCancel $ev25 71021 { $_.targetHwnd -eq '0x31444' } 5
    Assert-St 'negative other-chain empty-target cancel rejected' (-not $c25.Ok) "FAIL ($($c25.Detail))"
    # 26 负例：非空 targetHwnd 但不是记名 owned 目标 → 拒绝（既有语义保留）
    $ev26 = @(
        (MkEv 71099 'climb-cancelled' 'perch-detached' '0xBAD'),
        (MkEv 71021 'climb-completed' $null '0x31444')
    )
    $c26 = Test-LegalDragCancel $ev26 71021 { $_.targetHwnd -eq '0x31444' } 5
    Assert-St 'negative wrong-target cancel rejected' (-not $c26.Ok) "FAIL ($($c26.Detail))"
    # 27 负例：空目标取消且事件无 chainId（旧探针事件流）→ 保守拒绝
    $ev27 = @(
        (MkEv 71099 'climb-cancelled' 'perch-detached' $null),
        (MkEv 71021 'climb-completed' $null '0x31444')
    )
    $c27 = Test-LegalDragCancel $ev27 71021 { $_.targetHwnd -eq '0x31444' } 5
    Assert-St 'negative chainId-missing empty cancel rejected' (-not $c27.Ok) "FAIL ($($c27.Detail))"
    # ---- R18：起攀前置空闲判定受控正反例（curious/drag、旧心跳不能冒充空闲） ----
    # 28 正例：idle 且路线/栖息均未激活 → 允许起攀
    $p28 = Test-ClimbPrereqIdle (Mk 200 'idle' 1.0 $false $false) ([datetime]'2026-11-25 12:00:00')
    Assert-St 'positive prereq idle accepted' ($p28.Ok) "PASS ($($p28.Reason))"
    # 29 正例：实际本地夜间 sleep 且路线/栖息未激活 → 允许起攀
    $p29 = Test-ClimbPrereqIdle (Mk 200 'sleep' 1.0 $false $false) ([datetime]'2026-11-25 00:30:00')
    Assert-St 'positive prereq night-sleep accepted' ($p29.Ok) "PASS ($($p29.Reason))"
    # 30 负例：curious 不能冒充空闲（无路线/栖息）→ 拒绝
    $p30 = Test-ClimbPrereqIdle (Mk 200 'curious' 1.0 $false $false) ([datetime]'2026-11-25 12:00:00')
    Assert-St 'negative curious not free baseline rejected' (-not $p30.Ok) "FAIL ($($p30.Reason))"
    # 31 负例：drag 不能冒充空闲 → 拒绝
    $p31 = Test-ClimbPrereqIdle (Mk 200 'drag' 1.0 $false $false) ([datetime]'2026-11-25 12:00:00')
    Assert-St 'negative drag not free baseline rejected' (-not $p31.Ok) "FAIL ($($p31.Reason))"
    # 32 负例：白天 sleep 不能冒充空闲 → 拒绝（R13 口径：仅实际本地夜间接受 sleep）
    $p32 = Test-ClimbPrereqIdle (Mk 200 'sleep' 1.0 $false $false) ([datetime]'2026-11-25 12:00:00')
    Assert-St 'negative daytime-sleep rejected' (-not $p32.Ok) "FAIL ($($p32.Reason))"
    # 33 负例：路线仍激活（climbActive=true，即使 action=idle）→ 拒绝
    $p33 = Test-ClimbPrereqIdle (Mk 200 'idle' 1.0 $true $false) ([datetime]'2026-11-25 12:00:00')
    Assert-St 'negative route-active rejected' (-not $p33.Ok) "FAIL ($($p33.Reason))"
    # 34 负例：栖息仍激活（perchActive=true）→ 拒绝（起攀前不得栖息/绑定）
    $p34 = Test-ClimbPrereqIdle (Mk 200 'idle' 1.0 $false $true) ([datetime]'2026-11-25 12:00:00')
    Assert-St 'negative perch-active rejected' (-not $p34.Ok) "FAIL ($($p34.Reason))"
    # 35 负例：旧心跳（tMs 无前进）不能凭 "action=idle" 冒充空闲——入口新鲜校验必须拒绝；
    #   即使 Test-ClimbPrereqIdle 单条判定可过，Ensure-IdleForClimb 入口也要 Wait-FreshState
    $f35 = Test-FreshState (Mk 100 'idle' 1.0 $false $false) 200
    $p35 = Test-ClimbPrereqIdle (Mk 100 'idle' 1.0 $false $false) ([datetime]'2026-11-25 12:00:00')
    Assert-St 'negative stale heartbeat cannot masquerade idle' (-not $f35.Ok -and $p35.Ok) "FAIL fresh=(stale hidden by freshness check while single-state prereq alone would pass)"
    # ---- R19：Get-SafeCursorPoint 无鼠标实际调用（坐标结构 + 避开宠物/目标区域） ----
    # 36 正例：无宠物/目标遮挡（SelfTest 无 stateFile、ownedTargetHwnd=null）→ 返回右上
    #   候选，恰好两个数值标量且落在虚拟屏内（旧 bug：逗号+算术被当数组参与减法会抛异常）。
    $vs36 = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $p36 = Get-SafeCursorPoint
    $ok36 = ($p36.Count -eq 2) -and ($p36[0] -is [int]) -and ($p36[1] -is [int]) -and
        ($p36[0] -eq ($vs36.X + $vs36.Width - 8)) -and ($p36[1] -eq ($vs36.Y + 8)) -and
        (Test-PointOnScreen $p36[0] $p36[1])
    Assert-St 'positive safe-point returns 2 numeric scalars top-right (no occluder)' $ok36 "PASS p=[$($p36 -join ',')]"
    # 37 正例：宠物矩形覆盖右上候选、目标帧覆盖左上候选 → 必须避开两区域返回右下候选；
    #   stub 局部 Get-State/Get-FrameBounds（仅本调用生效），验证后删除不残留。
    function Get-State { [pscustomobject]@{ petRectPx = @(($vs36.X + $vs36.Width - 60), ($vs36.Y - 40), ($vs36.X + $vs36.Width + 60), ($vs36.Y + 40)) } }
    function Get-FrameBounds([IntPtr]$h) { [pscustomobject]@{ L = ($vs36.X - 60); T = ($vs36.Y - 40); R = ($vs36.X + 60); B = ($vs36.Y + 40) } }
    $ownSaved = $script:ownedTargetHwnd
    $script:ownedTargetHwnd = [IntPtr]1
    $p37 = Get-SafeCursorPoint
    $script:ownedTargetHwnd = $ownSaved
    Remove-Item function:Get-State, function:Get-FrameBounds -ErrorAction SilentlyContinue
    $inPet37 = $p37[0] -ge ($vs36.X + $vs36.Width - 60) -and $p37[0] -le ($vs36.X + $vs36.Width + 60) -and
        $p37[1] -ge ($vs36.Y - 40) -and $p37[1] -le ($vs36.Y + 40)
    $inTgt37 = $p37[0] -ge ($vs36.X - 60) -and $p37[0] -le ($vs36.X + 60) -and
        $p37[1] -ge ($vs36.Y - 40) -and $p37[1] -le ($vs36.Y + 40)
    $ok37 = ($p37.Count -eq 2) -and ($p37[0] -is [int]) -and ($p37[1] -is [int]) -and
        ($p37[0] -eq ($vs36.X + $vs36.Width - 8)) -and ($p37[1] -eq ($vs36.Y + $vs36.Height - 8)) -and
        (-not $inPet37) -and (-not $inTgt37)
    Assert-St 'positive safe-point avoids known pet rect and target frame (bottom-right)' $ok37 "PASS p=[$($p37 -join ',')]"
    Write-Host "[R9-SELFTEST] total=$script:stTotal failed=$script:stFail"
    if ($script:stFail -gt 0) { Write-Host '[R9-SELFTEST] RESULT=FAIL'; return 1 }
    Write-Host '[R9-SELFTEST] RESULT=PASS'
    return 0
}
if ($SelfTest) { exit (Invoke-SelfTest) }

# ---- R15：-OnlyChecks 窄范围复验入口解析与守卫 ----
# 用法：-OnlyChecks "3,4,7" 仅跑侧段（③④）与 land 拖动（⑦）关键链，加上始终
# 执行的 ①② 底盘与 ⑩ 收尾/诱饵纪律；摘要与退出码明确标注 narrow，不伪装 10/10。
$script:narrowMode = $false
$script:onlyChecksSet = $null
$script:modeLabel = 'FULL-10'
if (-not [string]::IsNullOrWhiteSpace($OnlyChecks)) {
    $script:onlyChecksSet = @()
    # R16：逐 token 严格校验——任一非法/空 token（如 '3,garbage'、'3,,4'）一律报错
    # exit 2，不再静默丢弃；合法编号仅支持 1-9 与 10。
    $badTokens = @($OnlyChecks -split '[,]' | ForEach-Object { $_.Trim() } | Where-Object {
        $_ -notmatch '^([1-9]|10)$' -or [string]::IsNullOrWhiteSpace($_) })
    if ($badTokens.Count -gt 0) {
        $shown = (($badTokens | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) -join ',')
        if ([string]::IsNullOrWhiteSpace($shown)) { $shown = '<空 token>' }
        Write-Host "[R9] ERROR: -OnlyChecks 含非法编号 '$shown'（原始输入 '$OnlyChecks'；仅支持 1-10 逗号分隔，如 '3,4,7'）" -ForegroundColor Red
        exit 2
    }
    $script:onlyChecksSet = @($OnlyChecks -split '[,]' | ForEach-Object { $_.Trim() } | ForEach-Object { [int]$_ } | Sort-Object -Unique)
    if ($script:onlyChecksSet.Count -eq 0) {
        Write-Host "[R9] ERROR: -OnlyChecks 未提供合法编号 '$OnlyChecks'（仅支持 1-10 逗号分隔）" -ForegroundColor Red
        exit 2
    }
    $script:narrowMode = $true
    $script:modeLabel = "R15-NARROW[$($script:onlyChecksSet -join ',')]"
    Write-Host "[R9] NARROW mode: 仅跑检查 [$($script:onlyChecksSet -join ',')]，①② 底盘与 ⑩ 收尾/诱饵纪律始终执行"
}
function Test-CheckEnabled([int]$n) {
    if (-not $script:narrowMode) { return $true }
    # ①②⑩ 底盘始终执行，不因窄轮跳过；守卫只用于 ③-⑨
    if ($n -eq 1 -or $n -eq 2 -or $n -eq 10) { return $true }
    return $script:onlyChecksSet.Contains($n)
}

# ---- 目标题材（独立 WinForms 夹具，M-17 手法）----
$script:markerFile = Join-Path $runDir "P5B003R9-$ts.txt"
Set-Content -LiteralPath $script:markerFile -Value "P5B-003 R9 marker file (run $ts)" -Encoding UTF8
$targetSubstr = "P5B003R9-$ts"
$script:otherRunMarkerFiles = @(
    Get-ChildItem (Split-Path $runDir -Parent) -Directory -Filter 'run-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -File -Filter 'P5B003R9-*.txt' -ErrorAction SilentlyContinue } |
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
$script:lureFile = Join-Path $script:lureDir "P5B003R9-lure-$ts.txt"
Set-Content -LiteralPath $script:lureFile -Value "P5B-003 R9 other-run marker lure (must survive this run)" -Encoding UTF8
$script:lureWindow = New-Object System.Windows.Forms.Form
$script:lureWindow.Text = "P5B003R9-lure-$ts.txt"
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
Write-Host "[R9] lures set: file=$script:lureFile window='$($script:lureWindow.Text)' @($($lureR.L),$($lureR.T)) windowlessPid=$($script:windowlessLure.Id)"

Write-Host "[R9] run dir: $runDir"
Write-Host "[R9] targetSubstr='$targetSubstr' (unique; user windows cannot match)"
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

# 真实右键注入 + 生产 TrackPopupMenuEx 菜单定位 + UIA 索引 + 键盘选中（同 M-18）。
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
    # R11 实机轮第 5 轮修正：Place-TargetNearFoot 翻转条件修复后，左侧链目标
    # （800×500，放脚侧左侧 110,180）会覆盖宠物窗口（初始在屏左 150..330），
    # 而放置回读流程把目标 Raise-Top 到宠物之前 → 右键点扫描全部被目标挡住
    # （第 4 轮 'no right-click point resolving to pet root'）。先把宠物窗口
    # 提回最前（恢复产品置顶语义），再扫描右键点。
    Raise-Top $petHwnd
    Start-Sleep -Milliseconds 400
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
        Write-Host "[R9] real-menu attempt $rc pre-click: fg=0x$($fgBefore.ToString('X')) title='$(Get-Title $fgBefore)' lastInput=${idleSec}s ago"
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
            Write-Host "[R9] real-menu attempt $rc : no menu (fgBefore=0x$($fgBefore.ToString('X')) petHwnd=0x$($petHwnd.ToString('X')))"
            [M16K]::AltTap()
            Start-Sleep -Milliseconds 600
            continue
        }
        $lastFg = [M15W2]::GetForegroundWindow()
        if ($lastFg -ne $petHwnd) {
            Write-Host "[R9] real-menu attempt $rc : menu open but foreground=0x$($lastFg.ToString('X')) != pet; dismiss & retry"
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
            Write-Host "[R9] real-menu attempt $rc : native menu open but UIA menu element missing; dismiss & retry"
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
            Write-Host "[R9] real-menu attempt $rc : menu did not close after Enter; dismiss & retry"
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

# R18：把测试鼠标移到安全位置并回读实际落点（注入受限时如实记录，不作成功门槛）。
function Move-CursorToSafeSpot {
    $p = Get-SafeCursorPoint
    [M15W2]::SetCursorPos($p[0], $p[1]) | Out-Null
    Start-Sleep -Milliseconds 200
    $cp = New-Object M15W2+POINT
    [M15W2]::GetCursorPos([ref]$cp) | Out-Null
    @{ Want = $p; X = $cp.X; Y = $cp.Y }
}

# 起攀前保证宠物空闲（未栖息/未攀爬）。
# R18：先把测试鼠标移到自有安全位置（避免悬停触发 curious 等临时表达），
# 再以新鲜心跳（tMs 前进）确认动作是 idle 或实际本地夜间 sleep、且路线
# （climbActive）与栖息（perchActive）均未激活才算空闲；禁止 non-idle 仍
# 返回成功；超时如实失败。栖息在但不在攀爬时拖动解绑，解绑后继续等回
# idle/夜间 sleep；不通过强制播放 idle、清内部标志或放宽产品守卫解决。
function Ensure-IdleForClimb {
    $cursor = Move-CursorToSafeSpot
    $safeInfo = "cursor want=[$($cursor.Want -join ',')] actual=[$($cursor.X),$($cursor.Y)]"
    $afterMs = 0
    $st0 = Get-State
    if ($null -ne $st0 -and $null -ne $st0.tMs) { $afterMs = [long]$st0.tMs }
    $detach = $null
    $lastDetail = 'no fresh state'
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline) {
        $st = Wait-FreshState $afterMs 2
        if ($null -ne $st) {
            $afterMs = [long]$st.tMs
            $lastDetail = Format-ActionState $st
            $j = Test-ClimbPrereqIdle $st
            if ($j.Ok) {
                return @{ Ok = $true; Detail = "$($j.Reason) [$safeInfo] $lastDetail" }
            }
            # 栖息在但不在攀爬 → 拖动解绑（每轮至多一次，解绑后回到安全位继续等）
            if ($st.perchActive -and -not $st.climbActive) {
                $detach = Invoke-RealDrag 26 4 10 700
                Start-Sleep -Milliseconds 800
                $cursor = Move-CursorToSafeSpot
                $safeInfo = "cursor want=[$($cursor.Want -join ',')] actual=[$($cursor.X),$($cursor.Y)]"
            }
        }
        Start-Sleep -Milliseconds 200
    }
    $d = "safe=[$safeInfo]"
    if ($null -ne $detach) { $d += " detach=$($detach.Detail)" }
    @{ Ok = $false; Detail = "pet busy and could not free to idle/night-sleep ($d) last=$lastDetail" }
}

# 落点遮挡者诊断（同 M-17）。
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

# 真实拖动（同 M-18 三重校验）。
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

# 目标贴宠物脚侧放置（side=+1 右脚侧 / -1 左脚侧；M-17 手法，右 130px，放不下镜像）。
function Place-TargetNearFoot([int]$side = 1) {
    for ($r = 1; $r -le 4; $r++) {
        $st = Get-State
        if ($null -ne $st -and $null -ne $st.petFootPx) { break }
        Start-Sleep -Milliseconds 300
    }
    $st = Get-State
    if ($null -eq $st -or $null -eq $st.petFootPx) { return $false }
    $sb = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $fx = [int]$st.petFootPx[0]; $fy = [int]$st.petFootPx[1]
    # R11 实机轮修正：目标顶部必须保留一个宠物高的净空，否则顶部落位
    # 时宠物窗口顶部出屏（TryComputeLanding 的完整可见性校验拒绝 →
    # RejectedNoLanding）。宠物高取当前心跳 petRectPx，缺失时兜底 180。
    $petH = [int]($st.petRectPx[3] - $st.petRectPx[1])
    if ($petH -lt 1) { $petH = 180 }
    $tx = $fx + 130 * $side
    # R11 实机轮第 4 轮修正：翻转仅在目标矩形（tx..tx+800）放不下时发生。
    # 旧写法 `($tx - 800) -lt $sb.Left` 恒真（如 tx=110 时 -690＜0），
    # 左侧链被永远翻到宠物右侧 → 产品镜像 -1 而驱动期望 +1 判 FAIL（第 2/3 轮）。
    if ($side -gt 0 -and ((($tx + 800) -gt $sb.Right) -or ($tx -lt $sb.Left))) { $tx = $fx - 130 - 800 }
    if ($side -lt 0 -and (($tx -lt $sb.Left) -or (($tx + 800) -gt $sb.Right))) { $tx = $fx + 130 }
    if ($tx -lt $sb.Left) { $tx = $sb.Left }
    $ty = $fy - 260
    $minTy = [Math]::Max($sb.Top + 160, $petH)
    if ($ty -lt $minTy) { $ty = $minTy }
    if (($ty + 500) -gt $sb.Bottom) { $ty = $sb.Bottom - 500 }
    # R11 实机轮加固：放置后回读 DWM 扩展帧校验（与探针快照同源）；
    # 目标窗口就绪前偶发异步重置会让 SetWindowPos 被覆盖（实测一次
    # left 从 110 漂到 382、目标实际落到宠物右侧导致镜像判据错位），
    # 故不符自动重放，最多 4 轮，仍不符才失败。
    for ($pr = 1; $pr -le 4; $pr++) {
        [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, $tx, $ty, 800, 500, 0x0004) | Out-Null
        Raise-Top $hwnd
        Start-Sleep -Milliseconds 600
        if ([M15W2]::IsIconic($hwnd)) {
            [M15W]::ShowWindow($hwnd, 9) | Out-Null
            Raise-Top $hwnd
            Start-Sleep -Milliseconds 600
        }
        $fb = Get-FrameBounds $hwnd
        if ($null -ne $fb -and [Math]::Abs($fb.L - $tx) -le 35 -and [Math]::Abs($fb.T - $ty) -le 35) {
            return $true
        }
        Write-Host "[R9] target place retry $($pr): actual=[$($fb.L),$($fb.T),$($fb.R),$($fb.B)] want=($tx,$ty,800,500)"
    }
    Write-Host "[R9] FAIL: target place not verifiable at ($tx,$ty) side=$side retry=4"
    return $false
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

# 真实左键点击指定物理点（同 M-18 前置校验）。
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

# 进入攀爬拾取：先把目标重放到宠物脚侧（side 控制左右），再真实右键
# 菜单「Climb on Window」→ 等 climbPickActive/active。
function Enter-ClimbPick([int]$side = 1) {
    if (-not (Place-TargetNearFoot $side)) { return @{ Ok = $false; Detail = 'target relocate to foot failed (no petFootPx)' } }
    $menuRes = Invoke-RealContextMenu 'Climb on Window'
    Write-Host "[R9] real-menu Climb on Window: $($menuRes.Detail)"
    if (-not $menuRes.Ok) { return @{ Ok = $false; Detail = "real menu Climb on Window failed: $($menuRes.Detail)" } }
    $stA = Wait-PickStatus @('active') 5
    if ($null -eq $stA) { return @{ Ok = $false; Detail = 'pick not active after real menu (no active status)' } }
    return @{ Ok = $true; Detail = $menuRes.Detail }
}

# 起攀执行（真实右键+真实点击）→ 等 climb-started + climbActive。
# side=1 右脚侧（期望镜像 -1），side=-1 左脚侧（期望镜像 1）。
# R18：记录真实菜单打开前（beforeMenu）、拾取后（afterPick）与起攀后（afterStart）
# 的实际动作时序；若菜单/交互触发 curious 等临时表达（产品守卫不覆盖），如实保留
# 证据并失败，不通过强制播放 idle、清内部标志或放宽产品守卫解决。
function Start-ClimbActual([int]$side) {
    $stBm = Get-State
    $bmDesc = if ($null -ne $stBm) { "beforeMenu=$(Format-ActionState $stBm)" } else { 'beforeMenu=n/a' }
    $pick = Enter-ClimbPick $side
    $stAp = Get-State
    $apDesc = if ($null -ne $stAp) { "afterPick=$(Format-ActionState $stAp)" } else { 'afterPick=n/a' }
    $seqBad = " | $bmDesc; $apDesc"
    if (-not $pick.Ok) { return @{ Ok = $false; Detail = "pick: $($pick.Detail)$seqBad" } }
    $hwnd = $script:ownedTargetHwnd
    $tgt = Find-TargetClickPoint $hwnd
    if (-not $tgt.Ok) { return @{ Ok = $false; Detail = "target click point: $($tgt.Detail)$seqBad" } }
    $gateOk = $false
    $gateDeadline = (Get-Date).AddMilliseconds(2500)
    while ((Get-Date) -lt $gateDeadline) {
        $gst = Get-State
        if ($null -ne $gst -and $gst.climbPickActive -and $gst.climbPickStatus -eq 'active') { $gateOk = $true; break }
        Start-Sleep -Milliseconds 150
    }
    if (-not $gateOk) { return @{ Ok = $false; Detail = "pre-inject gate: climb pick not active within 2.5s$seqBad" } }
    $baseMs = Get-LastEventMs
    $click = Invoke-RealLeftClick $tgt.Point $hwnd
    Start-Sleep -Milliseconds 300
    $started = Wait-Event 'climb-started' 8 $baseMs
    $st = Get-State
    $stDesc = if ($null -ne $st) { "afterStart=$(Format-ActionState $st)" } else { 'afterStart=n/a' }
    if (-not $click.Ok -or $null -eq $started -or $started.targetHwnd -ne (HwndHex $hwnd) -or
        $null -eq $st -or -not $st.climbActive) {
        $detail = "click=$($click.Ok) $($click.Detail) startedEv=$($null -ne $started) targetHwnd=$($started.targetHwnd) $stDesc$seqBad"
        if ($null -ne $st -and $st.climbPickStatus -eq 'rejected-occluded') {
            $detail += " | landingOccluder=$(Get-LandingOccluder $hwnd)"
        }
        return @{ Ok = $false; Detail = $detail }
    }
    return @{ Ok = $true; Detail = "started targetHwnd=$($started.targetHwnd) $stDesc$seqBad" }
}

# 等 land（climb-completed 交接）。
# -RequireInLand：必须在 completed 后的新鲜心跳中亲眼观察到 currentAction==climb_land 且
#   栖息曾活跃，才返回 Ok=true（允许发中断）；若 land 已自然结束（land-finished）或
#   心跳字段缺失（fields-missing）统一返回 Ok=false（未覆盖，不得按"可能已结束"放行）。
# 非 RequireInLand（左右链/正常回归）：同样记录 freshness，最终由调用方做干净空闲判定。
function Wait-Land([long]$AfterMs, [int]$TimeoutSec = 20, [switch]$RequireInLand) {
    $completed = Wait-Event 'climb-completed' $TimeoutSec $AfterMs
    if ($null -eq $completed) { return @{ Ok = $false; Verdict = 'no-completed'; St = $null; Detail = "no climb-completed after tMs>$AfterMs" } }
    $sinceMs = [long]$completed.tMs
    $perchSeen = $false
    $seenAny = $false
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        $st = Wait-FreshState $sinceMs 1
        if ($null -ne $st) {
            $sinceMs = [long]$st.tMs
            $seenAny = $true
            if ($st.perchActive) { $perchSeen = $true }
            $v = Get-LandVerdict $st -RequireInLand:$RequireInLand
            if ($v -eq 'in-land') {
                if (-not $RequireInLand) {
                    return @{ Ok = $true; Verdict = 'in-land'; St = $st; Detail = "observed land action=$($st.currentAction) mirror=$($st.climbMirrorScaleX) $(Format-ActionState $st)" }
                }
                # RequireInLand：补一个小窗口确认栖息交接完成（completed 后极短延迟）
                $perchDeadline = (Get-Date).AddSeconds(3)
                while ((Get-Date) -lt $perchDeadline -and -not $perchSeen) {
                    $st2 = Wait-FreshState $sinceMs 1
                    if ($null -ne $st2) {
                        $sinceMs = [long]$st2.tMs
                        if ($st2.perchActive) { $perchSeen = $true }
                    }
                }
                if (-not $perchSeen) {
                    return @{ Ok = $false; Verdict = 'no-perch-active'; St = $st; Detail = "climb_land observed but perch never active (handoff incomplete): $(Format-ActionState $st)" }
                }
                return @{ Ok = $true; Verdict = 'in-land'; St = $st; Detail = "inLand action=$($st.currentAction) mirror=$($st.climbMirrorScaleX) perchActive=$perchSeen $(Format-ActionState $st)" }
            }
            if ($v -eq 'fields-missing') {
                return @{ Ok = $false; Verdict = 'fields-missing'; St = $st; Detail = "heartbeat missing required fields: $(Format-ActionState $st)" }
            }
            if ($RequireInLand -and $v -eq 'land-finished') {
                return @{ Ok = $false; Verdict = 'land-finished'; St = $st; Detail = "land ended before driver saw climb_land (verdict=$v): $(Format-ActionState $st)" }
            }
        }
        if (-not $RequireInLand -and $seenAny) {
            # 正常回归模式：若已观察到非 climb_*（含已回 idle），结束轮询，交调用方判定
            $lastV = Get-LandVerdict (Get-State) -RequireInLand:$false
            if ($lastV -eq 'not-climbing') { break }
        }
        Start-Sleep -Milliseconds 180
    }
    $stF = Wait-FreshState $sinceMs 3
    if ($null -eq $stF) {
        return @{ Ok = $false; Verdict = 'timeout'; St = $null; Detail = 'no fresh state observed within land window' }
    }
    $vF = Get-LandVerdict $stF -RequireInLand:$RequireInLand
    if ($RequireInLand -and $vF -ne 'in-land') {
        return @{ Ok = $false; Verdict = $vF; St = $stF; Detail = "land not in progress when window closed (verdict=$vF): $(Format-ActionState $stF)" }
    }
    @{ Ok = $true; Verdict = $vF; St = $stF; Detail = "land window closed (verdict=$vF): $(Format-ActionState $stF)" }
}

$script:probe = $null
$script:driverExit = 0
$cover = $null
try {
    # 1. 启动目标并归属确认
    $targetProc = Start-Target
    Write-Host "[R9] target start issued pid=$($targetProc.Id)"
    $found = Find-OwnedTarget @()
    if ($null -eq $found) { throw 'no marker-owned target window appeared - aborting (user windows untouched)' }
    $hwnd = $found.Hwnd
    $script:ownedTargetHwnd = $found.Hwnd
    $script:ownedTargetPid = $found.Pid
    Start-Sleep -Milliseconds 1500
    if ((Get-Title $hwnd) -notlike "*$targetSubstr*") {
        throw "target title drifted off this run's marker - aborting (user windows untouched)"
    }
    Write-Host "[R9] target ownership verified: hwnd=$(HwndHex $hwnd) pid=$($found.Pid)"
    Add-Check '① 目标标记与归属确认（驱动侧零写入前提）' $true "hwnd=$(HwndHex $hwnd) pid=$($found.Pid) marker=$targetSubstr"

    # 2. 启动探针（真实宠物窗口）并等心跳
    $script:probe = Start-Process -FilePath $probeExe -ArgumentList "`"$runDir`"", 'window-climb-live', "`"$resultFile`"" -PassThru -WindowStyle Hidden
    Write-Host "[R9] probe started pid=$($script:probe.Id)"
    $stDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $stDeadline -and $null -eq (Get-State)) { Start-Sleep -Milliseconds 300 }
    if ($null -eq (Get-State)) { throw 'probe state file not appearing - aborting' }
    $stHeart = Get-State
    Add-Check '② 探针心跳含 new 观测字段 currentAction/climbMirrorScaleX' `
        ($null -ne $stHeart.currentAction -and $null -ne $stHeart.climbMirrorScaleX) `
        ($(Format-ActionState $stHeart) + " | 来源：心跳 climb-live-state.json")

    # 3. 左侧链：目标放宠物左侧 → 起攀 → 贴边 climb_side 镜像=1 → 上缘 climb_edge → land → 正常回 idle
    if (Test-CheckEnabled 3) {
    $idleL = Ensure-IdleForClimb
    if (-not $idleL.Ok) { throw "pet busy at left chain: $($idleL.Detail)" }
    if (-not (Test-ProbeAlive)) { throw 'probe exited before left chain' }
    # R15：限定本次链——起攀前基准时间，事件流只取本链 climb-started，不用旧事件
    $chainBaseL = Get-LastEventMs
    $startL = Start-ClimbActual -1
    $leftOk = $false; $leftDetail = $startL.Detail
    if ($startL.Ok) {
        $stepBase = Get-LastEventMs
        # 连续 fresh 采样直到 land 或回 idle；提取 side/edge/land 三段真实动作+镜像+时间
        $sampL = New-Object System.Collections.Generic.List[object]
        $sinceL = $stepBase
        $deadlineS = (Get-Date).AddSeconds(16)
        while ((Get-Date) -lt $deadlineS) {
            $ss = Wait-FreshState $sinceL 1
            if ($null -ne $ss) {
                $sinceL = [long]$ss.tMs
                $sampL.Add($ss)
                $curL = $ss
                if ($curL.currentAction -eq 'climb_land' -or
                    (-not (Test-ClimbAction $curL.currentAction) -and -not $curL.climbActive)) { break }
            }
            Start-Sleep -Milliseconds 180
        }
        $sideAt = $sampL | Where-Object { $_.currentAction -eq 'climb_side' } | Select-Object -First 1
        $edgeAt = $sampL | Where-Object { $_.currentAction -eq 'climb_edge' } | Select-Object -First 1
        $landAt = $sampL | Where-Object { $_.currentAction -eq 'climb_land' } | Select-Object -First 1
        $mirEdge = if ($null -ne $edgeAt) { [double]$edgeAt.climbMirrorScaleX } else { $null }
        $mirLand = if ($null -ne $landAt) { [double]$landAt.climbMirrorScaleX } else { $null }
        # R11 实机轮：side 镜像以事件流 climb-started 同步观测为准——TryStart 播
        # climb_side 后镜像即固定（sideX 相对脚点方向）；攀爬渲染高峰会饿死
        # Background 心跳（三次实测 side 段零心跳），心跳采样仅作回退。
        # R15：side 段判据必须以本次链的实际 climb_side 动作（事件 action 字段，
        # 与 sidecar/心跳同源）+ 镜像共同取证；mirror 初值可能 1，不能只凭镜像
        # 推断 side 播放。edge/land 镜像由路线方向决定（左侧链=沿上缘向右落位→
        # -1 是正确翻转），只记录证据、不作固定期望。
        # 限定本次链（时间/目标）：只取 climb-started 的 tMs 在本链起攀基准之后者，
        # 且目标 hwnd 为当前归属目标；不用旧事件。
        $startedEvL = @(Read-Events | Where-Object {
            $_.type -eq 'climb-started' -and $_.tMs -gt $chainBaseL -and
            $_.targetHwnd -eq (HwndHex $script:ownedTargetHwnd) }) | Select-Object -Last 1
        # R15：side 动作证据 = 事件 action 字段（实际 CurrentAction）；mirror 仍
        # 优先事件流（同步观测不饿死，R11 结论不改）作方向证据，但不再独立承担
        # "是否播放 side" 的证明——必须 action=climb_side 且镜像符合方向。
        $sideActL = if ($null -ne $startedEvL) { $startedEvL.action } else { $null }
        $mirSideSrc = if ($null -ne $startedEvL -and $null -ne $startedEvL.mirror) { 'startedEvt' } elseif ($null -ne $sideAt) { 'heartbeat' } else { 'none' }
        $mirSide = if ($null -ne $startedEvL -and $null -ne $startedEvL.mirror) { [double]$startedEvL.mirror } elseif ($null -ne $sideAt) { [double]$sideAt.climbMirrorScaleX } else { $null }
        $steps = @(Read-Events | Where-Object { $_.type -eq 'climb-step' -and $_.tMs -gt $stepBase })
        Send-Command 'capture left-sidelimb'
        $capL = Wait-CaptureSidecar 'left-sidelimb' $stepBase
        # R15：side 三要素——本链 climb-started 事件带实际 action=climb_side、
        # 镜像=1（目标放左侧不镜像）；无实际 side 动作即失败，不凭 mirror 独立推断。
        $leftStageOk = ($sideActL -eq 'climb_side' -and $null -ne $startedEvL -and
            $null -ne $edgeAt -and $null -ne $landAt -and
            $steps.Count -ge 1 -and $mirSide -eq 1)
        # R11：正常落位收尾——有界等待 land 真正播完（先见 climb_land，后见 idle+镜像1+栖息仍活跃）
        $landJudgeL = Wait-NormalLandingIdle $sinceL ($null -ne $landAt)
        $stAfterL = $landJudgeL.St
        $leftOk = ($leftStageOk -and $capL.Ok -and $landJudgeL.Ok)
        $leftDetail = "start=$($startL.Detail) | sideAction=$sideActL(evt@t=$($startedEvL.tMs)) mirSide=$mirSide(src=$mirSideSrc) edge=$($edgeAt.currentAction),mir=$mirEdge@t=$($edgeAt.tMs) land=$($landAt.currentAction),mir=$mirLand@t=$($landAt.tMs) steps=$($steps.Count) §$($capL.Detail) $($landJudgeL.Detail) | 帧=frames/left-sidelimb.png(+sidecar)"
    }
    Add-Check '③ 左侧链：贴边镜像=1 → 上缘 → land → 正常回 idle' $leftOk $leftDetail
    if (-not $leftOk) { throw 'left chain failed - aborting' }
    }  # R15:end Check-Enabled 3

    # 4. 右侧链：目标放宠物右侧 → 起攀 → 贴边 climb_side 镜像=-1 → 上缘 → land → 回 idle
    if (Test-CheckEnabled 4) {
    $idleR = Ensure-IdleForClimb
    if (-not $idleR.Ok) { throw "pet busy at right chain: $($idleR.Detail)" }
    if (-not (Test-ProbeAlive)) { throw 'probe exited before right chain' }
    # R15：限定本次链——起攀前基准时间，事件流只取本链 climb-started，不用旧事件
    $chainBaseR = Get-LastEventMs
    $startR = Start-ClimbActual 1
    $rightOk = $false; $rightDetail = $startR.Detail
    if ($startR.Ok) {
        $stepBaseR = Get-LastEventMs
        $sampR = New-Object System.Collections.Generic.List[object]
        $sinceR = $stepBaseR
        $deadlineSR = (Get-Date).AddSeconds(16)
        while ((Get-Date) -lt $deadlineSR) {
            $ssR = Wait-FreshState $sinceR 1
            if ($null -ne $ssR) {
                $sinceR = [long]$ssR.tMs
                $sampR.Add($ssR)
                $curR = $ssR
                if ($curR.currentAction -eq 'climb_land' -or
                    (-not (Test-ClimbAction $curR.currentAction) -and -not $curR.climbActive)) { break }
            }
            Start-Sleep -Milliseconds 180
        }
        $sideAtR = $sampR | Where-Object { $_.currentAction -eq 'climb_side' } | Select-Object -First 1
        $edgeAtR = $sampR | Where-Object { $_.currentAction -eq 'climb_edge' } | Select-Object -First 1
        $landAtR = $sampR | Where-Object { $_.currentAction -eq 'climb_land' } | Select-Object -First 1
        $mirEdgeR = if ($null -ne $edgeAtR) { [double]$edgeAtR.climbMirrorScaleX } else { $null }
        $mirLandR = if ($null -ne $landAtR) { [double]$landAtR.climbMirrorScaleX } else { $null }
        # R11 实机轮：side 镜像以事件流 climb-started（本链最后一个）同步观测为准；
        # 心跳采样仅作回退。edge/land 镜像为路线方向证据，不作固定期望。
        # R15：与左链相同——side 段判据必须以本次链实际 climb_side 动作（事件
        # action 字段）+ 镜像共同取证；限定本链时间/目标，不用旧事件；无实际
        # side 动作即失败，不凭 mirror 独立推断。
        $startedEvR = @(Read-Events | Where-Object {
            $_.type -eq 'climb-started' -and $_.tMs -gt $chainBaseR -and
            $_.targetHwnd -eq (HwndHex $script:ownedTargetHwnd) }) | Select-Object -Last 1
        $sideActR = if ($null -ne $startedEvR) { $startedEvR.action } else { $null }
        $mirSideSrcR = if ($null -ne $startedEvR -and $null -ne $startedEvR.mirror) { 'startedEvt' } elseif ($null -ne $sideAtR) { 'heartbeat' } else { 'none' }
        $mirSideR = if ($null -ne $startedEvR -and $null -ne $startedEvR.mirror) { [double]$startedEvR.mirror } elseif ($null -ne $sideAtR) { [double]$sideAtR.climbMirrorScaleX } else { $null }
        $stepsR = @(Read-Events | Where-Object { $_.type -eq 'climb-step' -and $_.tMs -gt $stepBaseR })
        Send-Command 'capture right-sidelimb'
        $capR = Wait-CaptureSidecar 'right-sidelimb' $stepBaseR
        $rightStageOk = ($sideActR -eq 'climb_side' -and $null -ne $startedEvR -and
            $null -ne $edgeAtR -and $null -ne $landAtR -and
            $stepsR.Count -ge 1 -and $mirSideR -eq -1)
        # R11：正常落位收尾——有界等待 land 真正播完（先见 climb_land，后见 idle+镜像1+栖息仍活跃）
        $landJudgeR = Wait-NormalLandingIdle $sinceR ($null -ne $landAtR)
        $stAfterR = $landJudgeR.St
        $rightOk = ($rightStageOk -and $capR.Ok -and $landJudgeR.Ok)
        $rightDetail = "start=$($startR.Detail) | sideAction=$sideActR(evt@t=$($startedEvR.tMs)) mirSide=$mirSideR(src=$mirSideSrcR) edge=$($edgeAtR.currentAction),mir=$mirEdgeR@t=$($edgeAtR.tMs) land=$($landAtR.currentAction),mir=$mirLandR@t=$($landAtR.tMs) steps=$($stepsR.Count) §$($capR.Detail) $($landJudgeR.Detail) | 帧=frames/right-sidelimb.png(+sidecar)"
    }
    Add-Check '④ 右侧链：贴边镜像=-1 → 上缘 → land → 正常回 idle' $rightOk $rightDetail
    if (-not $rightOk) { throw 'right chain failed - aborting' }
    }  # R15:end Check-Enabled 4

    # 5. hover 不抢占：真实悬停宠物（不点击），攀爬动作不得被好奇等覆盖。
    #    正反例已在自动测试覆盖（ClimbActionTests hover 生产 Tick）；此处为生产路径
    #    真实悬停观察，仅作补充证据。
    if (Test-CheckEnabled 5) {
    $idleH = Ensure-IdleForClimb
    $hoverOk = $false; $hoverDetail = "idle: $($idleH.Detail)"
    if ($idleH.Ok -and (Test-ProbeAlive)) {
        $startH = Start-ClimbActual -1
        if ($startH.Ok) {
            $petHwndH = Find-PetHwnd
            $stH0 = Get-State
            $petW = $stH0.petRectPx[2] - $stH0.petRectPx[0]; $petH = $stH0.petRectPx[3] - $stH0.petRectPx[1]
            $hoverPt = $null
            foreach ($fy in 0.5, 0.45, 0.55, 0.4) {
                foreach ($fx in 0.5, 0.45, 0.55, 0.4, 0.6) {
                    $hx = $stH0.petRectPx[0] + [int]($petW * $fx)
                    $hy = $stH0.petRectPx[1] + [int]($petH * $fy)
                    if (-not (Test-PointOnScreen $hx $hy)) { continue }
                    $hp0 = New-Object M15Hit+POINT
                    $hp0.X = $hx; $hp0.Y = $hy
                    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($hp0), 2) -eq $petHwndH) { $hoverPt = @($hx, $hy); break }
                }
                if ($hoverPt) { break }
            }
            if ($hoverPt) {
                [M15W2]::SetCursorPos($hoverPt[0], $hoverPt[1]) | Out-Null
                # 悬停 2.5s，期间 fresh 采样：climb 仍激活时动作必须保持 climb_*（好奇/委屈不抢占）
                $hoverStartMs = Get-LastEventMs
                $samples = New-Object System.Collections.Generic.List[string]
                $hoverKept = $true
                $hoverSeenLand = $false
                $sinceH = $hoverStartMs
                $hoverDeadline = (Get-Date).AddMilliseconds(2500)
                while ((Get-Date) -lt $hoverDeadline) {
                    Start-Sleep -Milliseconds 250
                    $sts = Wait-FreshState $sinceH 1
                    if ($null -ne $sts) {
                        $sinceH = [long]$sts.tMs
                        $samples.Add("$($sts.currentAction):$($sts.climbMirrorScaleX)")
                        if ($sts.currentAction -eq 'climb_land') { $hoverSeenLand = $true }
                        if ($sts.climbActive -and (-not (Test-ClimbAction $sts.currentAction) -or $null -eq $sts.currentAction)) { $hoverKept = $false }
                    }
                }
                # R11：移开鼠标，有界等待 land 真正播完（先见 climb_land，后见 idle+镜像1+栖息仍活跃）
                [M15W2]::SetCursorPos(($hoverPt[0] + 300), $hoverPt[1]) | Out-Null
                Send-Command 'capture hover-samples'
                $landJudgeH = Wait-NormalLandingIdle $hoverStartMs $hoverSeenLand
                $stHf = $landJudgeH.St
                $capH = Wait-CaptureSidecar 'hover-samples' $hoverStartMs
                $hoverOk = ($hoverKept -and $capH.Ok -and $landJudgeH.Ok)
                $hoverDetail = "samples=[$($samples -join ' ')] keptClimb=$hoverKept seenLand=$hoverSeenLand §$($capH.Detail) $($landJudgeH.Detail) | 帧=frames/hover-samples.png(+sidecar) | 自动测试已覆盖 hover 正反例，此为真实悬停补充观察"
            } else {
                $hoverDetail += " | no hover point resolving to pet root"
                Ensure-IdleForClimb | Out-Null
            }
        } else { $hoverDetail += " | start: $($startH.Detail)" }
    }
    Add-Check '⑤ hover 不抢占：真实悬停期间动作保持 climb_*' $hoverOk $hoverDetail
    }  # R15:end Check-Enabled 5

    # 6. land 期间暂停 → 暂停当下动作清理/镜像复位 → resume 后不得续播 → 回 idle
    #    （R10：旧"暂停冻结 land，恢复后自然播完"实现必须以暂停当下非攀爬动作+镜像1拒绝）
    if (Test-CheckEnabled 6) {
    $idleP = Ensure-IdleForClimb
    $pauseOk = $false; $pauseDetail = "idle: $($idleP.Detail)"
    if ($idleP.Ok -and (Test-ProbeAlive)) {
        $startP = Start-ClimbActual -1
        if ($startP.Ok) {
            $baseP = Get-LastEventMs
            $landP = Wait-Land $baseP -RequireInLand
            if ($landP.Ok) {
                $inLandMsP = [long]$landP.St.tMs
                Send-Command 'capture land-before-pause'
                $capP = Wait-CaptureInLand 'land-before-pause' $inLandMsP
                $stPre = Get-State
                $footPre = @($stPre.petFootPx)
                Send-Command 'pause'
                Start-Sleep -Milliseconds 900
                $stFrozen = Wait-FreshState $inLandMsP 4
                $footFrozen = if ($null -ne $stFrozen) { @($stFrozen.petFootPx) } else { $null }
                $frozen = ($null -ne $footFrozen -and
                    [Math]::Abs($footFrozen[0] - $footPre[0]) -le 1 -and
                    [Math]::Abs($footFrozen[1] - $footPre[1]) -le 1)
                $idleJudgeFrozen = if ($null -ne $stFrozen) { Test-NonClimbIdle $stFrozen } else { @{ Ok = $false; Reason = 'no frozen state' } }
                Send-Command 'resume'
                # resume 后短观察（2.5s）：不得续播 land（生命周期已取消），采样须保持非 climb_*
                $sinceP = [long]$stFrozen.tMs
                $noResume = $true
                $resumeSamples = New-Object System.Collections.Generic.List[string]
                $resumeDeadline = (Get-Date).AddSeconds(2.5)
                while ((Get-Date) -lt $resumeDeadline) {
                    Start-Sleep -Milliseconds 250
                    $sr = Wait-FreshState $sinceP 1
                    if ($null -ne $sr) {
                        $sinceP = [long]$sr.tMs
                        $resumeSamples.Add("$($sr.currentAction):$($sr.climbMirrorScaleX)")
                        if ($null -eq $sr.currentAction -or (Test-ClimbAction $sr.currentAction) -or $sr.climbActive) { $noResume = $false }
                    }
                }
                $stPf = Wait-FreshState $sinceP 8
                $idleJudgeP = if ($null -ne $stPf) { Test-NonClimbIdle $stPf } else { @{ Ok = $false; Reason = 'no final state' } }
                $pauseOk = ($capP.Ok -and $frozen -and $idleJudgeFrozen.Ok -and $noResume -and $idleJudgeP.Ok)
                $pauseDetail = "§$($capP.Detail) frozen=$frozen pre=[$($footPre -join ',')] frozenFoot=[$($footFrozen -join ',')] paused=$(Format-ActionState $stFrozen) pausedJudge=$($idleJudgeFrozen.Reason) resumeSamples=[$($resumeSamples -join ' ')] noResume=$noResume after=$(Format-ActionState $stPf) final=$($idleJudgeP.Reason) | 帧=frames/land-before-pause.png(+sidecar证明land)"
            } else { $pauseDetail += " | land(未覆盖): $($landP.Detail)" }
        } else { $pauseDetail += " | start: $($startP.Detail)" }
    }
    Add-Check '⑥ land 期间暂停→暂停清理→resume 不续播→回 idle' $pauseOk $pauseDetail
    }  # R15:end Check-Enabled 6

    # 7. land 期间拖动 → climb-drag-preempted / perch-detached → 松手回 idle、镜像复位、不回拉
    if (Test-CheckEnabled 7) {
    $idleD = Ensure-IdleForClimb
    $dragOk = $false; $dragDetail = "idle: $($idleD.Detail)"
    if ($idleD.Ok -and (Test-ProbeAlive)) {
        $startD = Start-ClimbActual 1
        if ($startD.Ok) {
            $baseD = Get-LastEventMs
            # R16：本链 chainId（本次起攀 climb-started）——land 交接 tracker 释放
            # 后 perch-detached 取消事件 targetHwnd=null，以此关联本次 land/拖动链；
            # 读取不到（旧探针）则空目标取消一律拒绝（Get-LastStartedChainId 返回
            # $null，Test-DragCancelContextMatch 对空目标且 chainId 缺失时拒绝）。
            $chainIdD = Get-LastStartedChainId (HwndHex $script:ownedTargetHwnd)
            $landD = Wait-Land $baseD -RequireInLand
            if ($landD.Ok) {
                $inLandMsD = [long]$landD.St.tMs
                Send-Command 'capture land-before-drag'
                $capD = Wait-CaptureInLand 'land-before-drag' $inLandMsD
                $dragRes = Invoke-RealDrag 26 -4 10 700
                # R15：接受 climb-drag-preempted（旧路径）或本次拖动窗口内
                # （land 确认心跳之后）climb-cancelled(reason=perch-detached)——
                # 生产正确路径先 Perch.CancelForDrag→DetachInternal→
                # ReleaseClimbForPerchDetach 清 land，Climb.CancelForDrag 不再发
                # preempted。拒绝旧取消（tMs<=land 确认）与缺取消；限定本链目标。
                $cancelD = $null
                $cancelDeadlineD = (Get-Date).AddSeconds(8)
                # R16：过滤器经 Test-DragCancelContextMatch 分流——非空 targetHwnd
                # 仍须等于 owned；空 targetHwnd（合法 perch-detached 取消）须 chainId
                # 命中本链 $chainIdD。非空错目标与其他链空取消均拒绝。
                while ((Get-Date) -lt $cancelDeadlineD) {
                    $vCancel = Test-LegalDragCancel (Read-Events) $inLandMsD `
                        { $_.targetHwnd -eq (HwndHex $script:ownedTargetHwnd) } $chainIdD
                    if ($vCancel.Ok) { $cancelD = $vCancel; break }
                    Start-Sleep -Milliseconds 300
                }
                if ($null -eq $cancelD) {
                    $cancelD = Test-LegalDragCancel (Read-Events) $inLandMsD `
                        { $_.targetHwnd -eq (HwndHex $script:ownedTargetHwnd) } $chainIdD
                }
                Start-Sleep -Milliseconds 600
                $sinceD = $inLandMsD
                $stD1 = Wait-FreshState $sinceD 4
                if ($null -ne $stD1) { $sinceD = [long]$stD1.tMs }
                $footD1 = if ($null -ne $stD1) { @($stD1.petFootPx) } else { $null }
                # 松手后位置稳定（不回拉）
                $stableD = $true
                for ($k = 0; $k -lt 4; $k++) {
                    Start-Sleep -Milliseconds 400
                    $stDk = Wait-FreshState $sinceD 1
                    if ($null -ne $stDk) {
                        $sinceD = [long]$stDk.tMs
                        if ($null -ne $footD1) {
                            if ([Math]::Abs($stDk.petFootPx[0] - $footD1[0]) -gt 3 -or [Math]::Abs($stDk.petFootPx[1] - $footD1[1]) -gt 3) { $stableD = $false }
                        }
                    }
                }
                $stDf = Wait-FreshState $sinceD 5
                $idleJudgeD = Test-NonClimbIdle $stDf
                $dragOk = ($capD.Ok -and $dragRes.Ok -and $cancelD.Ok -and $idleJudgeD.Ok -and $stableD)
                $dragDetail = "§$($capD.Detail) $($dragRes.Detail) cancel=$($cancelD.Kind)($($cancelD.Detail)) $(Format-ActionState $stDf) idle=$($idleJudgeD.Reason) stable=$stableD | 帧=frames/land-before-drag.png(+sidecar证明land) | 拖动手感细节已由 P2-004 覆盖，此处验证 land 中断清理"
            } else { $dragDetail += " | land(未覆盖): $($landD.Detail)" }
        } else { $dragDetail += " | start: $($startD.Detail)" }
    }
    Add-Check '⑦ land 期间拖动→preempted→回 idle 镜像复位不回拉' $dragOk $dragDetail
    }  # R15:end Check-Enabled 7

    # 8. land 期间找回（生产 RecoverPet：CancelForRecover + EndPerchPick）→ 清理回 idle
    #    （R10：必须先 RequireInLand 确认 fresh 心跳确为 climb_land 且栖息曾活跃，
    #      sidecar 证明采集瞬间也是 climb_land，再发 recover；最终 Test-NonClimbIdle）
    if (Test-CheckEnabled 8) {
    $idleV = Ensure-IdleForClimb
    $recvOk = $false; $recvDetail = "idle: $($idleV.Detail)"
    if ($idleV.Ok -and (Test-ProbeAlive)) {
        $startV = Start-ClimbActual -1
        if ($startV.Ok) {
            $baseV = Get-LastEventMs
            $landV = Wait-Land $baseV -RequireInLand
            if ($landV.Ok) {
                $inLandMsV = [long]$landV.St.tMs
                Send-Command 'capture land-before-recover'
                $capV = Wait-CaptureInLand 'land-before-recover' $inLandMsV
                Send-Command 'recover'
                Start-Sleep -Milliseconds 900
                $stV = Wait-FreshState $inLandMsV 8
                $idleJudgeV = if ($null -ne $stV) { Test-NonClimbIdle $stV } else { @{ Ok = $false; Reason = 'no state after recover' } }
                $recvOk = ($capV.Ok -and $idleJudgeV.Ok)
                $recvDetail = "§$($capV.Detail) after=$(Format-ActionState $stV) cleared=$($idleJudgeV.Ok)($($idleJudgeV.Reason)) | 帧=frames/land-before-recover.png(+sidecar证明land) | 来源：生产 RecoverPet→CancelForRecover 路径"
            } else { $recvDetail += " | land(未覆盖): $($landV.Detail)" }
        } else { $recvDetail += " | start: $($startV.Detail)" }
    }
    Add-Check '⑧ land 期间找回→RecoverPet 清理回 idle 镜像复位' $recvOk $recvDetail
    }  # R15:end Check-Enabled 8

    # 9. 目标失效：起攀至 land 播放中关闭目标 → DetachInternal→ReleaseClimbForPerchDetach 清理
    #    （R10：必须先 RequireInLand + sidecar 证明 land 仍激活，再关闭目标；最终 Test-NonClimbIdle）
    if (Test-CheckEnabled 9) {
    $idleT = Ensure-IdleForClimb
    $goneOk = $false; $goneDetail = "idle: $($idleT.Detail)"
    if ($idleT.Ok -and (Test-ProbeAlive)) {
        $startT = Start-ClimbActual 1
        if ($startT.Ok) {
            $baseT = Get-LastEventMs
            $landT = Wait-Land $baseT -RequireInLand
            if ($landT.Ok) {
                $inLandMsT = [long]$landT.St.tMs
                Send-Command 'capture land-before-close'
                $capT = Wait-CaptureInLand 'land-before-close' $inLandMsT
                # 关闭本次归属目标（SendMessage WM_CLOSE）：探针不触碰第三方窗口
                [M15W]::SendMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                # 等清理终态：climb/perch 均非激活（连续 fresh 心跳推进，防旧心跳误判）
                $sinceT = $inLandMsT
                $stG = $null
                $deadlineGone = (Get-Date).AddSeconds(10)
                while ((Get-Date) -lt $deadlineGone) {
                    $stGt = Wait-FreshState $sinceT 1
                    if ($null -ne $stGt) {
                        $sinceT = [long]$stGt.tMs
                        $stG = $stGt
                        if (-not $stG.climbActive -and -not $stG.perchActive) { break }
                    }
                    Start-Sleep -Milliseconds 200
                }
                if ($null -eq $stG) { $stG = Wait-FreshState $inLandMsT 3 }
                $closed = -not [M15W2]::IsWindow($hwnd)
                $idleJudgeG = if ($null -ne $stG) { Test-NonClimbIdle $stG } else { @{ Ok = $false; Reason = 'no final state' } }
                $goneOk = ($closed -and $capT.Ok -and $idleJudgeG.Ok)
                $goneDetail = "§$($capT.Detail) targetClosed=$closed after=$(Format-ActionState $stG) cleared=$($idleJudgeG.Ok)($($idleJudgeG.Reason)) | 帧=frames/land-before-close.png(+sidecar证明land) | 该故障组合自动测试已覆盖(Reviewer_LandTargetGone)，此为生产路径真实关闭观察"
            } else { $goneDetail += " | land(未覆盖): $($landT.Detail)" }
        } else { $goneDetail += " | start: $($startT.Detail)" }
    }
    Add-Check '⑨ land 期间目标失效→关闭清理回 idle 镜像复位' $goneOk $goneDetail
    }  # R15:end Check-Enabled 9

    # 10. 探针收尾 + 诱饵存活核对（内容/标题/位置/进程）→ 10 项唯一完整全绿
    if (-not (Test-ProbeAlive)) { throw 'probe exited before final check' }
    $finalFrameCount = @(Get-ChildItem $framesDir -Filter '*.png' -ErrorAction SilentlyContinue).Count
    $captureEvents = @(Read-Events | Where-Object { $_.type -eq 'frame-captured' }).Count
    Send-Command 'quit'
    $probeExited = $script:probe.WaitForExit(15000)
    $exitCode = if ($probeExited) { $script:probe.ExitCode } else { -999 }
    if (-not $probeExited) { $script:probe.Kill(); Start-Sleep -Milliseconds 300 }
    # 诱饵文件内容核对（非仅存在；清理留给 finally，核对通过才允许清理）
    $lureFileAlive = Test-Path $script:lureFile
    $lureContentOk = $false
    if ($lureFileAlive) {
        $lureText = (Get-Content $script:lureFile -Raw).Trim()
        $lureContentOk = ($lureText -eq 'P5B-003 R9 other-run marker lure (must survive this run)')
    }
    # 诱饵窗口标题/位置核对（与创建时快照一致）
    $lureWinAlive = (-not $script:lureWindow.IsDisposed -and $script:lureWindow.Visible)
    $lureWinOk = $false
    if ($lureWinAlive) {
        $lureRNow = New-Object M15W+RECT
        [M15W]::GetWindowRect($script:lureWindow.Handle, [ref]$lureRNow) | Out-Null
        $lureWinOk = ($script:lureWindow.Text -eq $script:lureSnapshot.Text -and
            $lureRNow.L -eq $script:lureSnapshot.L -and $lureRNow.T -eq $script:lureSnapshot.T)
    }
    # 诱饵进程存活（未被越界清理）
    $lureProcAlive = -not $script:windowlessLure.HasExited
    $tenOk = ($exitCode -eq 0 -and $lureFileAlive -and $lureContentOk -and $lureWinAlive -and $lureWinOk -and
        $lureProcAlive -and $finalFrameCount -ge 4 -and $captureEvents -ge 4)
    Add-Check '⑩ 探针 exitCode=0 且诱饵内容/标题/位置/进程存活 + 帧数达标' $tenOk `
        "exitCode=$exitCode frames=$finalFrameCount frameEvents=$captureEvents lureFile=$lureFileAlive lureContent=$lureContentOk lureWin=$lureWinAlive lureWinPos=$lureWinOk lureProc=$lureProcAlive"
    if ($script:narrowMode) {
        $complete = Test-NarrowChecks $script:checks $script:onlyChecksSet
    } else {
        $complete = Test-CompleteChecks $script:checks
    }
    if (-not $complete.Ok) {
        Write-Host "[R9] checklist completeness FAIL ($($script:modeLabel)): $($complete.Reason)"
        $script:driverExit = 1
    } else {
        Write-Host "[R9] checklist completeness OK ($($script:modeLabel)): $($complete.Reason)"
    }
    if ($exitCode -ne 0) { Write-Host "[R9] probe result: $(if(Test-Path $resultFile){Get-Content $resultFile -Raw}else{'n/a'})" }
} catch {
    Write-Host "[R9] EXCEPTION: $($_.Exception.Message)"
    Add-Check '①⑩（中止）运行异常' $false $_.Exception.Message
    $script:driverExit = 1
} finally {
    Invoke-CleanupOwnedTarget
    # 探针：先 SendMessage(WM_CLOSE)，再 WaitForExit，仍未退出才 Kill
    if ($null -ne $script:probe -and -not $script:probe.HasExited) {
        try {
            $pmh = $script:probe.MainWindowHandle
            if ($pmh -ne [IntPtr]::Zero) { [M15W]::SendMessage($pmh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        } catch { }
        if (-not $script:probe.WaitForExit(3000)) { $script:probe.Kill() }
    }
    # 安全清理本轮自建对象（⑩ 已核对诱饵存活；此处只清精确进程/窗口/表单，不越界）
    if ($null -ne $script:windowlessLure -and -not $script:windowlessLure.HasExited) { $script:windowlessLure.Kill() }
    if ($null -ne $script:lureWindow -and -not $script:lureWindow.IsDisposed) { $script:lureWindow.Dispose() }
    $json = @{
        runDir = $runDir
        targetSubstr = $targetSubstr
        mode = $(if ($script:narrowMode) { 'R15-NARROW' } else { 'FULL-10' })
        narrowChecks = $(if ($script:narrowMode) { $script:onlyChecksSet } else { $null })
        checks = @($script:checks | ForEach-Object { [pscustomobject]@{ Item = $_.Item; Result = $_.Result; Detail = $_.Detail } })
        summary = [pscustomobject]@{
            total = $script:checks.Count
            passed = @($script:checks | Where-Object Result -eq 'PASS').Count
            failed = @($script:checks | Where-Object Result -eq 'FAIL').Count
        }
        framesDir = $framesDir
    } | ConvertTo-Json -Depth 6
    Set-Content -LiteralPath (Join-Path $runDir 'r9-summary.json') -Value $json -Encoding UTF8
    Write-Host "[R9] summary ($($script:modeLabel)): $($script:checks.Count) checks, passed=$(@($script:checks | Where-Object Result -eq 'PASS').Count) failed=$(@($script:checks | Where-Object Result -eq 'FAIL').Count)"
    Write-Host "[R9] details at $runDir (r9-summary.json / driver-transcript.log / frames)"
    Stop-Transcript | Out-Null
}
# 退出码：全量=10 项唯一全绿且无异常才 0；窄轮=被选编号+①②⑩ 全绿才 0；
# 含 FAIL/缺项/异常非零（SelfTest 已提前 exit）。
exit $script:driverExit