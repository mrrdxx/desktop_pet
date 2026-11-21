# P5A-001 / TESTING M-15: window-perch-live 隔离预览驱动。
# 证据写入 .temp\p5a-001\m15\run-<时间戳>-<目标>\。
#
# 真实宠物窗口（探针内生产 CreatePetWindow 路径，隔离存档）+ 真实外部测试窗口
# （驱动以独立进程启动）。操作序列（每步等探针事件并断言）：
#   select→landed（脚点=目标可见外框上缘中点）→ 移动→follow
#   → 最大化→detached(no-landing，宠物完整矩形保持可见) → 还原+重选→landed
#   → 最小化→detached(Minimized，宠物停在原地) → 恢复+重选→landed
#   → 上缘遮挡→occluded-detach（宠物停在原地）→ 解除+重选→landed
#   → 仅遮上缘远端（避开落脚区）→ 不脱离仍 active 且脚点不变
#   → 关闭→detached(WindowGone，宠物停在原地) → 重开+重选→landed
#   → 真实鼠标拖动宠物→drag-preempted（绑定解除、记录取消句柄、松手不回拉）
#   → 移动目标→无 follow 且脚点不回拉 → quit → 探针 exitCode=0。
#
# 目标锁定复用 M-14 纪律（不触碰用户窗口）：
#   - notepad: 本轮独有临时文件 P5A001NP-<ts>.txt 绑定标题 + 归属确认
#     （宿主 ProcessName='Notepad'、StartTime >= t0-2s、非驱动进程；Win11 记事本
#     启动器把窗口交给子进程，不按启动器 pid 匹配），归属不明一律只读跳过。
#   - edge: 独立 user-data-dir（run 目录内）+ 本地 html，页面标题带本轮唯一标记
#     P5A001Browser-<ts>；归属确认：宿主命令行含本轮 profile 路径 + StartTime>=t0-2s。
#   - 启动前断言系统中不存在标题含 "p5a001" 的窗口，否则中止。
#   - 真实鼠标注入只作用于本驱动/探针自建窗口（宠物窗口），注入前做光标回读、
#     命中、前台三重前置校验（M-14 教训：用户物理鼠标活跃时注入会被覆盖）。
#
# 用法（在仓库根目录，必须 pwsh 7 -File 独立进程运行；5.1 读 UTF-8 乱码）:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5A001Preview.ps1 -TargetKind notepad
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5A001Preview.ps1 -TargetKind edge

param(
    [Parameter(Mandatory = $true)][ValidateSet('notepad', 'edge')][string]$TargetKind,
    [int]$DurationSec = 300
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:t0 = Get-Date
$runDir = Join-Path $repo ".temp\p5a-001\m15\run-$ts-$TargetKind"
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
# 诊断：枚举物理矩形覆盖给定点的可见顶层窗口（标题去重，最多 5 个）。
# 用于区分远端遮挡断言失败时的真实遮挡者（环境弹窗 vs 驱动自建 cover vs 宠物自身）。
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
# 目标上缘可见性采样（与 App SampleTopEdgeOcclusion 同点位语义：top+2，9 点）。
# 用户自己的置顶窗口（如"天翼看家行业版"）盖住投放区时，落位会被真实遮挡按规格
# 立即脱离（run-20261118-105250 实测 fraction=0）——这是正确行为，驱动须另选清空区。
function Test-TopEdgeVisible([IntPtr]$h) {
    $r = New-Object M15W+RECT
    if (-not [M15W]::GetWindowRect($h, [ref]$r)) { return $false }
    $y = $r.T + 2
    $w = $r.R - $r.L
    for ($i = 0; $i -lt 9; $i++) {
        $p = New-Object M15Hit+POINT
        $p.X = $r.L + [int][Math]::Round($w * ($i + 0.5) / 9)
        $p.Y = $y
        $root = [M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($p), 2)
        if ($root -ne $h) { return $false }
    }
    return $true
}
# 把目标放到一个上缘完全可见的位置（清空区扫描）；全部候选被用户置顶窗口盖住则返回 $null。
$script:candidatePos = @(
    @(1450, 800), @(1200, 300), @(2200, 300), @(500, 300), @(500, 900),
    @(1200, 1000), @(2000, 1000), @(100, 100), @(2000, 100),
    @(700, 250), @(1000, 300), @(100, 250), @(300, 500), @(700, 500), @(150, 520), @(900, 520)
)
$script:anchorX = 0; $script:anchorY = 0
# 清空点适配当前屏幕：候选点的 800×500 目标矩形及其步骤 4 移动后
# （+100/+50）的矩形都必须完整落在主屏内，且上缘留 ≥200px 宠物高度余量
# （150% DPI 时宠物 180 物理像素）。否则在低分辨率（如 1920×1080@100%）
# 下移动后宠物矩形会出右屏，落位被 F-01 出屏保护正确拒绝（NoLanding），
# 连锁导致 follow/晃动/拖动全部 FAIL（run-20261118-205750 根因）。
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
# select 前门控：目标须非最小化且上缘完全可见（外部程序可能最小化/遮挡目标窗口）。
# 最小化则先还原；先 Raise-Top 重试 3 次，仍被盖住则重新扫描清空区；找不到即返回 false。
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
# 统一恢复门控：外部程序（如天翼看家告警弹窗）会偶发最小化目标或盖住落位区。
# 每个关键步骤前确保：目标可见且栖息 active；被干扰则恢复 + 重选（重试一次）。
function Ensure-Perched([IntPtr]$h) {
    $st = Get-State
    if ($null -ne $st -and $st.perchActive) { return $true }
    if (-not (Ensure-TargetVisible $h)) { return $false }
    Send-Command 'select'
    $null = Wait-Event 'landed' 20 (Get-LastEventMs)
    Start-Sleep -Milliseconds 600
    $st2 = Get-State
    if ($null -ne $st2 -and $st2.perchActive) { return $true }
    if (-not (Ensure-TargetVisible $h)) { return $false }
    Send-Command 'select'
    $null = Wait-Event 'landed' 20 (Get-LastEventMs)
    Start-Sleep -Milliseconds 600
    $st3 = Get-State
    return ($null -ne $st3 -and $st3.perchActive)
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
    if (-not (Test-Path $stateFile)) { return $null }
    try { Get-Content $stateFile -Raw | ConvertFrom-Json } catch { $null }
}
function Send-Command([string]$cmd) {
    Add-Content -LiteralPath $commandsFile -Value $cmd -Encoding UTF8
}
# 断言宠物脚点贴在目标可见外框上缘中点（允许 ±6px：DWM 外框与宠物窗口 rect 的舍入差）。
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

$script:checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$name, [bool]$pass, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Item = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} : {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

# ---- 目标题材 ----
$script:markerFile = $null
if ($TargetKind -eq 'notepad') {
    $script:markerFile = Join-Path $runDir "P5A001NP-$ts.txt"
    Set-Content -LiteralPath $script:markerFile -Value "P5A-001 M-15 marker file (run $ts)" -Encoding UTF8
    $targetSubstr = "P5A001NP-$ts"
    Get-ChildItem (Split-Path $runDir -Parent) -Directory -Filter 'run-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -File -Filter 'P5A001NP-*.txt' -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -ne $script:markerFile } |
        Remove-Item -Force -ErrorAction SilentlyContinue
} else {
    $targetSubstr = "P5A001Browser-$ts"
    $htmlFile = Join-Path $runDir 'p5a001-page.html'
    Set-Content -Path $htmlFile -Value "<!doctype html><html><head><title>$targetSubstr</title></head><body><h1>P5A-001 M-15 perch target</h1></body></html>" -Encoding UTF8
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

# ---- 安全预检 ----
foreach ($h in Get-Tops) {
    if ((Get-Title ([IntPtr]$h)) -match 'p5a001') { throw "existing p5a001 window found (0x$($h.ToString('X'))) - abort" }
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

    # 定位到上缘完全可见的清空区（避开用户置顶窗口，如"天翼看家行业版"）
    if (-not (Find-ClearSpot $hwnd)) {
        throw 'no clear spot for target top edge (user topmost windows cover all candidates) - aborting'
    }

    # 2. 启动探针（真实宠物窗口）并等心跳
    $probe = Start-Process -FilePath $probeExe -ArgumentList "`"$runDir`"", 'window-perch-live', "`"$resultFile`"" -PassThru -WindowStyle Hidden
    Write-Host "[$TargetKind] probe started pid=$($probe.Id)"
    $stDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $stDeadline -and $null -eq (Get-State)) { Start-Sleep -Milliseconds 300 }
    if ($null -eq (Get-State)) { throw 'probe state file not appearing - aborting' }

    # 3. select → landed，脚点贴上缘中点（目标被外部最小化/遮挡时先恢复再选）
    $null = Ensure-TargetVisible $hwnd
    Send-Command 'select'
    $landed = Wait-Event 'landed' 20
    if ($null -eq $landed) {
        Start-Sleep -Milliseconds 800
        $null = Ensure-TargetVisible $hwnd
        Send-Command 'select'
        $landed = Wait-Event 'landed' 20
    }
    $foot1 = Test-FootOnTopEdge $hwnd
    Add-Check '落位：选中后脚点在目标上缘中点' ($null -ne $landed -and $foot1.Ok) "landed=$($null -ne $landed) $($foot1.Detail)"
    if ($null -eq $landed) { throw 'landed event missing - aborting' }

    # 4. 移动目标 → follow，脚点随新上缘（在清空区附近小位移）
    if (-not (Ensure-Perched $hwnd)) { Add-Check '跟随：目标移动后脚点贴新上缘' $false 'pre-step recovery failed (external interference)' }
    else {
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, ($script:anchorX + 100), ($script:anchorY + 50), 800, 500, 0x0004) | Out-Null
    $wm = Get-LastEventMs
    $follow = Wait-Event 'follow' 12 $wm

    # P5A-002 晃动反馈①：移动时晃——follow 后立即密集采样（衰减包络约 0.7s 结束，
    # 必须在脚点检查的 600ms 停顿之前采样；正弦过零点可能单次采样为 0，
    # 取整个采样窗内最大 |angle|>0.2 即视为在晃）
    $wobMove = $false; $wobAng = 0.0
    if ($null -ne $follow) {
        $wobDl = (Get-Date).AddMilliseconds(1200)
        while ((Get-Date) -lt $wobDl) {
            $stw = Get-State
            if ($null -ne $stw -and $null -ne $stw.PSObject.Properties['wobbling'] -and $stw.wobbling) {
                $a = [Math]::Abs([double]$stw.wobbleAngle)
                if ($a -gt $wobAng) { $wobAng = $a }
                if ($a -gt 0.2) { $wobMove = $true; break }
            }
            Start-Sleep -Milliseconds 80
        }
    }
    Add-Check '晃动反馈：目标移动时宠物在晃（角度非零）' $wobMove "follow=$($null -ne $follow) wobbling=$wobMove maxAngle=$wobAng"

    Start-Sleep -Milliseconds 600
    $foot2 = Test-FootOnTopEdge $hwnd
    Add-Check '跟随：目标移动后脚点贴新上缘' ($null -ne $follow -and $foot2.Ok) "follow=$($null -ne $follow) $($foot2.Detail)"

    # P5A-002 晃动反馈②：停下回稳——目标静止后衰减包络自动归零（≤1.2s + 余量）
    Start-Sleep -Milliseconds 800
    $stSettle = Get-State
    $settled = ($null -ne $stSettle -and $null -ne $stSettle.PSObject.Properties['wobbling'] -and
        -not $stSettle.wobbling -and [Math]::Abs([double]$stSettle.wobbleAngle) -lt 0.001)
    Add-Check '晃动反馈：目标停下后回稳且角度归零' $settled "wobbling=$($stSettle.wobbling) angle=$($stSettle.wobbleAngle)"
    }

    # 5. 最大化 → 上沿容不下完整宠物矩形 → detached(no-landing)，宠物完整可见 → 还原+重选→landed
    if (-not (Ensure-Perched $hwnd)) { Add-Check '最大化：no-landing 脱离且宠物完整可见' $false 'pre-step recovery failed (external interference)' }
    else {
    Raise-Top $hwnd
    [M15W]::SetForegroundWindow($hwnd) | Out-Null
    $wm = Get-LastEventMs
    [M15W]::ShowWindow($hwnd, 3) | Out-Null
    $maxDet = Wait-Event 'detached' 12 $wm { $_.reason -eq 'no-landing' }
    Start-Sleep -Milliseconds 600
    $stMax = Get-State
    # 宠物完整矩形须在其所在屏幕 Bounds 内（动态取屏，不硬编码分辨率）
    $visMax = $false; $visMaxDetail = 'no state'
    if ($null -ne $stMax -and $stMax.petRectPx.Count -ge 4) {
        $pr = $stMax.petRectPx
        $petScreen = [System.Windows.Forms.Screen]::FromPoint((New-Object System.Drawing.Point(([int](($pr[0] + $pr[2]) / 2)), ([int](($pr[1] + $pr[3]) / 2)))))
        $sb = $petScreen.Bounds
        $visMax = ($pr[0] -ge $sb.X -and $pr[1] -ge $sb.Y -and
            $pr[2] -le ($sb.X + $sb.Width) -and $pr[3] -le ($sb.Y + $sb.Height))
        $visMaxDetail = "pet=[$($pr -join ',')] screen=[$($sb.X),$($sb.Y),$($sb.Width),$($sb.Height)]"
    }
    Add-Check '最大化：no-landing 脱离且宠物完整可见' ($null -ne $maxDet -and $visMax -and $null -ne $stMax -and -not $stMax.perchActive) "det=$($null -ne $maxDet) vis=$visMax active=$($stMax.perchActive) $($visMaxDetail)"
    $wmRestore = Get-LastEventMs
    [M15W]::ShowWindow($hwnd, 1) | Out-Null
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, $script:anchorX, $script:anchorY, 800, 500, 0x0004) | Out-Null
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 600
    $visOk5 = Ensure-TargetVisible $hwnd
    Send-Command 'select'
    $landedMax = Wait-Event 'landed' 20 (Get-LastEventMs)
    Start-Sleep -Milliseconds 400
    $footR = Test-FootOnTopEdge $hwnd
    Add-Check '最大化恢复后重选落位' ($visOk5 -and $null -ne $landedMax -and $footR.Ok) "vis=$visOk5 landed=$($null -ne $landedMax) $($footR.Detail)"
    }

    # 6. 最小化 → detached(Minimized)，宠物停在原地
    # 前置：目标可能已被外部程序最小化——先恢复并重选，再做我们自己的最小化
    if ([M15W2]::IsIconic($hwnd)) {
        [M15W]::ShowWindow($hwnd, 1) | Out-Null
        Start-Sleep -Milliseconds 500
        [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, $script:anchorX, $script:anchorY, 800, 500, 0x0004) | Out-Null
        Start-Sleep -Milliseconds 400
    }
    if (-not (Ensure-Perched $hwnd)) { Add-Check '失效脱离：最小化 detached(Minimized) 且宠物停在原地' $false 'pre-step recovery failed (external interference)' }
    else {
    $stBefore = Get-State
    $wm = Get-LastEventMs
    [M15W]::ShowWindow($hwnd, 6) | Out-Null
    $detMin = Wait-Event 'detached' 12 $wm { $_.reason -eq 'Minimized' }
    Start-Sleep -Milliseconds 600
    $stAfterMin = Get-State
    $stayMin = ($null -ne $stBefore -and $null -ne $stAfterMin -and
        [Math]::Abs($stAfterMin.petFootPx[0] - $stBefore.petFootPx[0]) -le 2 -and
        [Math]::Abs($stAfterMin.petFootPx[1] - $stBefore.petFootPx[1]) -le 2)
    Add-Check '失效脱离：最小化 detached(Minimized) 且宠物停在原地' ($null -ne $detMin -and $stayMin -and -not $stAfterMin.perchActive) "reason=$($detMin.reason) stay=$stayMin active=$($stAfterMin.perchActive)"
    }

    # 7. 恢复 + 重选 → landed
    [M15W]::ShowWindow($hwnd, 1) | Out-Null
    [M15W]::SetWindowPos($hwnd, [IntPtr]::Zero, $script:anchorX, $script:anchorY, 800, 500, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 600
    $visOk7 = Ensure-TargetVisible $hwnd
    Send-Command 'select'
    $landed2 = Wait-Event 'landed' 20 (Get-LastEventMs)
    $foot3 = Test-FootOnTopEdge $hwnd
    Add-Check '恢复后重选落位' ($visOk7 -and $null -ne $landed2 -and $foot3.Ok) "vis=$visOk7 landed=$($null -ne $landed2) $($foot3.Detail)"

    # 8. 上缘遮挡 → occluded-detach，宠物停在原地
    if (-not (Ensure-Perched $hwnd)) { Add-Check '遮挡脱离：落脚处被盖 occluded-detach 且宠物停在原地' $false 'pre-step recovery failed (external interference)' }
    else {
    $stBeforeOcc = Get-State
    $r2 = New-Object M15W+RECT
    [M15W]::GetWindowRect($hwnd, [ref]$r2) | Out-Null
    $cover = New-Object System.Windows.Forms.Form
    $cover.Text = "P5A001Cover-$ts"
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
    }
    $cover.Hide(); $cover.Dispose(); $cover = $null
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 600

    # 9. 解除后重选 → landed
    $visOk9 = Ensure-TargetVisible $hwnd
    Send-Command 'select'
    $landed3 = Wait-Event 'landed' 20 (Get-LastEventMs)
    $foot4 = Test-FootOnTopEdge $hwnd
    Add-Check '遮挡解除后重选落位' ($visOk9 -and $null -ne $landed3 -and $foot4.Ok) "vis=$visOk9 landed=$($null -ne $landed3) $($foot4.Detail)"

    # 9b. 仅遮上缘远端（避开落脚区）→ 不脱离，仍 active 且脚点不变
    if (-not (Ensure-Perched $hwnd)) { Add-Check '远端遮挡：落脚区外上缘被盖不脱离' $false 'pre-step recovery failed (external interference)' }
    else {
    $stBeforeFar = Get-State
    if ($null -eq $stBeforeFar -or $stBeforeFar.petRectPx.Count -lt 4) {
        Add-Check '远端遮挡：落脚区外上缘被盖不脱离' $false 'no pet state'
    }
    else {
    $rFar = New-Object M15W+RECT
    [M15W]::GetWindowRect($hwnd, [ref]$rFar) | Out-Null
    # 落脚区 = [footX ± petWidth/2]，cover 只盖其右侧之外的上缘远端
    $footXFar = [int]$stBeforeFar.petFootPx[0]
    $petWFar = [int]([Math]::Abs($stBeforeFar.petRectPx[2] - $stBeforeFar.petRectPx[0]))
    $coverFarLeft = [Math]::Max($rFar.L, ($footXFar + [int]($petWFar / 2) + 10))
    $coverFarWidth = [Math]::Min(300, ($rFar.R - $coverFarLeft) )
    if ($coverFarWidth -lt 40) {
        Add-Check '远端遮挡：落脚区外上缘被盖不脱离' $false "no clear far segment (left=$coverFarLeft width=$coverFarWidth)"
    }
    else {
    $coverFar = New-Object System.Windows.Forms.Form
    $coverFar.Text = "P5A001FarCover-$ts"
    $coverFar.StartPosition = 'Manual'
    $coverFar.Left = $coverFarLeft; $coverFar.Top = $rFar.T - 5
    $coverFar.Width = $coverFarWidth; $coverFar.Height = 120
    $coverFar.TopMost = $true
    $wmFar = Get-LastEventMs
    $coverFar.Show()
    [M15W]::SetWindowPos($coverFar.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
    [M15W]::SetWindowPos($hwnd, $coverFar.Handle, 0, 0, 0, 0, 0x0013) | Out-Null
    # 稳态遮挡检查周期约 2s，等 2.5 个周期
    $farOcc = Wait-Event 'occluded-detach' 7 $wmFar
    $stAfterFar = Get-State
    $footFarSame = ($null -ne $stBeforeFar -and $null -ne $stAfterFar -and
        [Math]::Abs($stAfterFar.petFootPx[0] - $stBeforeFar.petFootPx[0]) -le 2 -and
        [Math]::Abs($stAfterFar.petFootPx[1] - $stBeforeFar.petFootPx[1]) -le 2)
    Add-Check '远端遮挡：落脚区外上缘被盖不脱离' ($null -eq $farOcc -and $footFarSame -and $stAfterFar.perchActive) "occ=$($null -ne $farOcc) footSame=$footFarSame active=$($stAfterFar.perchActive) cover=[$coverFarLeft,$($rFar.T-5),$coverFarWidth,120] foot=$footXFar petW=$petWFar occBy=$(if ($farOcc) { Get-OccludersAtPoint $footXFar ($rFar.T + 2) } else { 'n/a' })"
    $coverFar.Hide(); $coverFar.Dispose()
    }
    }
    }
    Raise-Top $hwnd
    Start-Sleep -Milliseconds 600

    # 10. 关闭 → detached(WindowGone)，宠物停在原地
    if (-not (Ensure-Perched $hwnd)) { Add-Check '失效脱离：关闭 detached(WindowGone) 且宠物停在原地' $false 'pre-step recovery failed (external interference)' }
    else {
    $stBeforeClose = Get-State
    $wm = Get-LastEventMs
    [M15W]::SendMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $detGone = Wait-Event 'detached' 12 $wm
    $goneOk = $false; $goneDetail = 'no detached event'
    if ($detGone) {
        # 记事本关闭过渡态可能先报 Hidden/Minimized，窗口随后销毁；
        # WindowGone 直接通过，其他 reason 须在 8s 内确认 IsWindow=false。
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
    }

    # 11. 重开目标（标记 + 归属确认）+ 重选 → landed
    Start-Target | Out-Null
    $found2 = Find-OwnedTarget @($hwnd.ToInt64())
    if ($null -eq $found2) { throw 'no marker-owned reopened window - aborting' }
    $hwnd2 = $found2.Hwnd
    Write-Host "[$TargetKind] reopened target verified: hwnd=$(HwndHex $hwnd2) pid=$($found2.Pid)"
    [M15W]::SetWindowPos($hwnd2, [IntPtr]::Zero, $script:anchorX, $script:anchorY, 800, 500, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 600
    $visOk11 = Ensure-TargetVisible $hwnd2
    Send-Command 'select'
    $landed4 = Wait-Event 'landed' 20 (Get-LastEventMs)
    $foot5 = Test-FootOnTopEdge $hwnd2
    Add-Check '重开后重选落位' ($visOk11 -and $null -ne $landed4 -and $foot5.Ok) "vis=$visOk11 landed=$($null -ne $landed4) $($foot5.Detail)"

    # 12. 真实鼠标拖动宠物 → drag-preempted（注入只作用于探针自建宠物窗口）
    # 被遮挡脱离后 CancelForDrag 不会发事件；先确保栖息仍活跃
    if (-not (Ensure-Perched $hwnd2)) { Add-Check '拖动接管：真实鼠标拖动宠物 drag-preempted' $false 'pre-step recovery failed (external interference)' }
    else {
    $stHwnd = Get-State
    $petRect = $stHwnd.petRectPx
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
        Add-Check '拖动接管：真实鼠标拖动宠物 drag-preempted' $false 'pet window not found by probe pid'
    } else {
        # R1-3 补实机打断：拖动前先小位移目标触发一次 follow，
        # 确认小熊正在晃、角度非零，然后立即抓住拖动——
        # 晃动衰减包络约 0.7s，确认与注入之间不得插入长停顿。
        $dragWobble = $false; $dragAngle = 0.0
        foreach ($wtry in 1..3) {
            $rw = New-Object M15W+RECT
            [M15W]::GetWindowRect($hwnd2, [ref]$rw) | Out-Null
            [M15W]::SetWindowPos($hwnd2, [IntPtr]::Zero, ($rw.L + 60), ($rw.T + 40), ($rw.R - $rw.L), ($rw.B - $rw.T), 0x0004) | Out-Null
            Raise-Top $hwnd2
            $null = Wait-Event 'follow' 8 (Get-LastEventMs)
            $wDl = (Get-Date).AddMilliseconds(700)
            while ((Get-Date) -lt $wDl) {
                $wst = Get-State
                if ($null -ne $wst -and $null -ne $wst.PSObject.Properties['wobbling'] -and
                    $wst.wobbling -and [Math]::Abs([double]$wst.wobbleAngle) -gt 0.2) {
                    $dragWobble = $true; $dragAngle = [Math]::Abs([double]$wst.wobbleAngle); break
                }
                Start-Sleep -Milliseconds 60
            }
            if ($dragWobble) { break }
        }
        Add-Check '晃动反馈：拖动时小熊正在晃（角度非零）' $dragWobble "wobbling=$dragWobble angle=$dragAngle"

        $stBeforeDrag = Get-State
        $petRect = $stBeforeDrag.petRectPx
        # 抓取点取身体区。宠物是逐像素点击穿透（AllowsTransparency）：
        # 不透明像素稀疏（diag 实测 18 点网格仅 1 点命中宠物），须全矩形密集扫描
        # WindowFromPoint 根窗口==宠物句柄 的不透明命中点；优先取中下部（身体，
        # 头部是抚摸候选区，身体按压才立即拖动）。宠物窗口 WS_EX_NOACTIVATE
        # 不抢前台——不做前台校验，只做光标回读 + 命中校验。
        function Find-PetGrabPoint {
            $petW = $petRect[2] - $petRect[0]; $petH = $petRect[3] - $petRect[1]
            $cands = @()
            foreach ($fy in 0.85, 0.75, 0.65, 0.55, 0.45, 0.35, 0.25, 0.15) {
                foreach ($fx in 0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8) {
                    $cands += ,@(($petRect[0] + [int]($petW * $fx)), ($petRect[1] + [int]($petH * $fy)))
                }
            }
            foreach ($c in $cands) {
                $tp = New-Object M15Hit+POINT
                $tp.X = $c[0]; $tp.Y = $c[1]
                if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($tp), 2) -eq $petHwnd) { return $c }
            }
            # 稀疏候选未命中：10px 密集扫描全矩形
            for ($gy = $petRect[1] + 5; $gy -lt $petRect[3] - 5; $gy += 10) {
                for ($gx = $petRect[0] + 5; $gx -lt $petRect[2] - 5; $gx += 10) {
                    $tp = New-Object M15Hit+POINT
                    $tp.X = $gx; $tp.Y = $gy
                    if ([M15Hit]::GetAncestor([M15Hit]::WindowFromPoint($tp), 2) -eq $petHwnd) { return @($gx, $gy) }
                }
            }
            return $null
        }
        $grab = Find-PetGrabPoint
        if (-not $grab) {
            # 宠物身体区可能被其他置顶窗口盖住：换清空区重选后再扫一次
            Write-Host "[$TargetKind] pet body blocked at current spot - relocating target and re-selecting"
            if (Ensure-TargetVisible $hwnd2) {
                Send-Command 'select'
                $null = Wait-Event 'landed' 20 (Get-LastEventMs)
                Start-Sleep -Milliseconds 400
                $stBeforeDrag = Get-State
                $petRect = $stBeforeDrag.petRectPx
                $petHwnd = [IntPtr]::Zero
                foreach ($h in Get-Tops) {
                    $pp = [uint32]0
                    [M15W2]::GetWindowThreadProcessId([IntPtr]$h, [ref]$pp) | Out-Null
                    if ($pp -ne [uint32]$probe.Id) { continue }
                    $pr = New-Object M15W+RECT
                    [M15W]::GetWindowRect([IntPtr]$h, [ref]$pr) | Out-Null
                    if ([Math]::Abs($pr.L - $petRect[0]) -le 3 -and [Math]::Abs($pr.T - $petRect[1]) -le 3) { $petHwnd = [IntPtr]$h; break }
                }
                if ($petHwnd -ne [IntPtr]::Zero) { $grab = Find-PetGrabPoint }
            }
        }
        $posOk = ($null -ne $grab)
        if ($posOk) {
            $grabX = $grab[0]; $grabY = $grab[1]
            # 光标回读 ±2（用户物理鼠标活跃时注入会被覆盖——绝对不注入）
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
            Add-Check '拖动接管：真实鼠标拖动宠物 drag-preempted' $false "skipped: cursor injection ineffective (grab=$($grab -join ',')) - user mouse active or pet body blocked"
        } else {
            $wm = Get-LastEventMs
            [M15W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
            Start-Sleep -Milliseconds 120
            for ($i = 1; $i -le 12; $i++) {
                [M15W2]::SetCursorPos(($grabX + $i * 14), ($grabY + $i * 10)) | Out-Null
                Start-Sleep -Milliseconds 40
            }
            # 释放前停顿降速，避免甩出物理让松手后继续滑动
            Start-Sleep -Milliseconds 350
            [M15W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
            $dragEv = Wait-Event 'drag-preempted' 12 $wm

            # R3 F-03 取证：探针在接管处理前后各写一条带时间戳的角度记录
            # （preempt-angle-pre / preempt-angle-post，均在 drag-preempted 同一
            # tick 内：pre 在 CancelForDrag 内同步记录，post 在其完整返回后
            # Dispatcher 排队执行）。断言：接管前非零角度（正在晃），
            # 接管完成时刻已归零——归零来自打断，而非自然衰减。
            $preRec = $null; $postRec = $null
            $zpDl = (Get-Date).AddSeconds(3)
            while ((Get-Date) -lt $zpDl -and ($null -eq $preRec -or $null -eq $postRec)) {
                $recs = @(Read-Events | Where-Object { $_.tMs -ge $dragEv.tMs -and ($_.type -eq 'preempt-angle-pre' -or $_.type -eq 'preempt-angle-post') })
                $preRec = @($recs | Where-Object { $_.type -eq 'preempt-angle-pre' })[-1]
                $postRec = @($recs | Where-Object { $_.type -eq 'preempt-angle-post' })[-1]
                Start-Sleep -Milliseconds 40
            }
            $wobAtPreempt = 'n/a'; $angAtPreempt = 'n/a'
            $preNonZero = $false; $zeroAtPreempt = $false
            if ($null -ne $preRec) {
                $wobAtPreempt = $preRec.wobbling; $angAtPreempt = $preRec.wobbleAngle
                $preNonZero = ($preRec.wobbling -and [Math]::Abs([double]$preRec.wobbleAngle) -gt 0.001)
            }
            if ($null -ne $postRec) {
                $zeroAtPreempt = (-not $postRec.wobbling -and [Math]::Abs([double]$postRec.wobbleAngle) -lt 0.001)
            }

            Start-Sleep -Milliseconds 1000
            $stAfterDrag = Get-State
            $moved = ($null -ne $stAfterDrag -and
                [Math]::Abs($stAfterDrag.petFootPx[0] - $stBeforeDrag.petFootPx[0]) -gt 10)
            $cancelOk = ($stAfterDrag.dragCancelledHwnd -eq (HwndHex $hwnd2))
            Add-Check '拖动接管：真实鼠标拖动宠物 drag-preempted' ($null -ne $dragEv -and -not $stAfterDrag.perchActive -and $cancelOk -and $moved) "dragEv=$($null -ne $dragEv) active=$($stAfterDrag.perchActive) dragCancelled=$($stAfterDrag.dragCancelledHwnd) target=$(HwndHex $hwnd2) moved=$moved"

            # P5A-002 晃动反馈③：拖动接管打断后无残留（晃动结束且角度归零）
            # R3 F-03 补证：探针接管前记录非零角度、接管完成时刻记录已归零。
            $noResidue = ($preNonZero -and $zeroAtPreempt -and $null -ne $stAfterDrag -and $null -ne $stAfterDrag.PSObject.Properties['wobbling'] -and
                -not $stAfterDrag.wobbling -and [Math]::Abs([double]$stAfterDrag.wobbleAngle) -lt 0.001)
            Add-Check '晃动反馈：拖动接管打断后无残留' $noResidue "preNonZero=$preNonZero(wob=$wobAtPreempt angle=$angAtPreempt) zeroAtPost=$zeroAtPreempt wobbling=$($stAfterDrag.wobbling) angle=$($stAfterDrag.wobbleAngle)"

            # 13. 松手后移动目标 → 无 follow、脚点不回拉
            [M15W]::SetWindowPos($hwnd2, [IntPtr]::Zero, 1600, 900, 800, 500, 0x0004) | Out-Null
            Start-Sleep -Milliseconds 1600
            $stFinal = Get-State
            $pullBack = @(Read-Events | Where-Object { $_.tMs -gt $wm -and ($_.type -eq 'follow' -or $_.type -eq 'landed') })
            $noPull = ($pullBack.Count -eq 0 -and $null -ne $stFinal -and
                [Math]::Abs($stFinal.petFootPx[0] - $stAfterDrag.petFootPx[0]) -le 2 -and
                [Math]::Abs($stFinal.petFootPx[1] - $stAfterDrag.petFootPx[1]) -le 2 -and
                -not $stFinal.perchActive)
            Add-Check '松手不回拉：移动目标后无 follow 且宠物留在松手处' $noPull "followAfterDrag=$($pullBack.Count) footFinal=[$($stFinal.petFootPx -join ',')] footAtDrop=[$($stAfterDrag.petFootPx -join ',')] active=$($stFinal.perchActive)"
        }
    }
    }

    # 14. 结束
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
    # 收尾关闭本驱动标记的遗留记事本窗口（P5A001NP- 前缀只有本驱动创建，不会命中用户窗口）
    foreach ($h in (Get-Tops)) {
        $t = Get-Title ([IntPtr]$h)
        if ($t -like '*P5A001NP-*.txt*') {
            [M15W]::SendMessage([IntPtr]$h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            Write-Host "[$TargetKind] closed leftover marker window 0x$($h.ToString('X')) '$t'"
        }
    }
    # 只清理本驱动 profile 的 msedge（不按 pid 差集，避免误杀）
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
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Set-Content (Join-Path $runDir 'm15-checklist.txt')
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $allPass = -not ($script:checks | Where-Object { $_.Result -eq 'FAIL' })
    Write-Host "[$TargetKind] checklist: $(if ($allPass) { 'ALL PASS' } else { 'HAS FAIL' }) -> $runDir"
    Stop-Transcript | Out-Null
    if (-not $allPass) { exit 1 }
}
