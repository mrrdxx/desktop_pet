# P5A-PLAN / TESTING M-14: window-perch 实机验证驱动（探针只读）。
# 仓库内长期复跑入口；运行证据写入 .temp\p5a-plan\m14\run-<时间戳>-<目标>\。
#
# 目标锁定方式（不触碰用户窗口；F-03 收尾：标记绑定 + 归属确认前置 + 零写入）：
#   - notepad: 驱动先创建本轮独有的临时测试文件 P5AM14NP-<ts>.txt，再让记事本打开
#     它——窗口标题自然携带本轮标记，不再用 SetWindowText 识别/改名。初开与重开都
#     用同一标记确认目标；标记命中后还须归属确认（窗口宿主必须是本轮启动后新
#     出现的记事本进程：ProcessName='Notepad'、StartTime >= t0-2s、且非驱动/诱饵
#     进程；Win11 记事本启动器会把窗口交给子进程，宿主 pid 不同于启动器 pid），
#     归属不明一律只读跳过（run-20261117-210809 单实例事故防线）。
#     对照诱饵：目标启动前由独立 pwsh 进程创建标题符合旧规则（'*- Notepad'）的外部
#     候选窗口（不同 PID、驱动启动后出现），全程断言不改标题、不移动、不关闭，
#     同时真正带标记的测试窗口仍通过全部检查。
#   - edge:    独立 user-data-dir（run 目录内）+ 本地 html，页面标题带本轮唯一标记
#     `P5AM14Browser-<ts>`（F-03 Edge 收尾：旧固定标题两轮并行会互相误选）；页面/探针/
#     驱动共用该标记。归属确认：宿主 msedge 命令行含本轮 profile 路径（精确匹配，排除
#     其他轮 profile）+ StartTime >= t0-2s 且 pid != 驱动进程。对照窗口：另一个真实
#     msedge（独立 profile `edge-profile-other`，页面标题仍为旧固定规则 `P5AM14Browser`），
#     驱动启动后出现，全程断言被跳过、不改标题、不移动、不关闭；本轮标记窗口仍须通过
#     全部检查。不复用用户浏览器窗口；结束只清理命令行含本 run profile 路径的 msedge 进程。
#   - 启动前断言系统中不存在标题含 "p5am14" 的窗口，否则中止。
#   运行方式：必须以 powershell -File（独立进程）运行；交互式会话内调用会命中旧类型缓存。
#
# 操作序列（每步等探针事件并断言）：
#   select → 慢速移动 → 静止不重复上报 → 移回初始位置 → 快速拖动（最终外框比对）
#   → 真实鼠标快速拖动 → 最大化 → 最小化(cleared/Minimized) → 恢复(reacquire 同句柄)
#   → 上缘遮挡(occluded) → 解除(unoccluded) → 关闭(cleared/WindowGone)
#   → 重开(reacquire 新句柄；标记文件重开 + 归属确认，外部候选零写入)
#   → 外部候选不改标题/移动/关闭断言(不同 PID 诱饵) → 最终关闭。
#
# 用法（在仓库根目录，必须 pwsh 7 -File 独立进程运行；5.1 读 UTF-8 乱码）:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-M14WindowPerch.ps1 -TargetKind notepad
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-M14WindowPerch.ps1 -TargetKind edge

param(
    [Parameter(Mandatory = $true)][ValidateSet('notepad', 'edge')][string]$TargetKind,
    [int]$DurationSec = 150,
    [int]$PollMs = 200
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$script:t0 = Get-Date   # 目标归属校验基线：本轮启动的进程其 StartTime 不得早于此
$runDir = Join-Path $repo ".temp\p5a-plan\m14\run-$ts-$TargetKind"
$scenarioDir = Join-Path $runDir 'window-perch'
New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
$resultFile = Join-Path $runDir 'probe-result.json'
$eventsFile = Join-Path $scenarioDir 'window-perch-events.jsonl'
$probeExe = Join-Path $repo 'tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe'
Start-Transcript -Path (Join-Path $runDir 'driver-transcript.log') | Out-Null

Add-Type -AssemblyName System.Windows.Forms
# 驱动线程设为 Per-Monitor V2：SetWindowPos/GetWindowRect 使用物理像素，
# 与探针坐标系一致（否则 DPI 虚拟化会把坐标放大 ~1.5 倍导致窗口移出屏幕）。
if (-not ([System.Management.Automation.PSTypeName]'M14Dpi').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M14Dpi {
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
$threadAwareness = [M14Dpi]::EnablePerMonitorV2()
Write-Host "[$TargetKind] driver thread DPI awareness = $threadAwareness (2=PerMonitor; required for physical px)"
if (-not ([System.Management.Automation.PSTypeName]'M14W').Type) {
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class M14W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SetWindowText(IntPtr h, string s);
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

# 命中测试（与探针同语义：WindowFromPoint → GetAncestor root）
if (-not ([System.Management.Automation.PSTypeName]'M14Hit').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M14Hit {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint ga);
}
"@
}

# 补充 Win32（交互式会话里旧版本类型会被缓存、无法追加成员，改版必须换新类名）：
#   IsWindow   - Edge 关闭时先短暂最小化（cleared/Minimized）再销毁，用它确认窗口已真正失效。
#   DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS=9) - 与探针同口径实测可见外框，
#               用于 F-01“快速移动后最终外框正确”的逐项比对。
#   SetCursorPos / mouse_event - F-01 补充：注入真实鼠标输入做快速拖动（仅作用于自建测试窗口）。
#   GetWindowThreadProcessId - 目标归属校验：记事本目标窗口必须属于本轮启动的进程。
#   重要：本脚本必须以 powershell -File（独立进程）运行，交互式会话内 . / & 调用会命中
#   旧类型缓存（run-20261117-210809 的 FATAL 即由此产生）。
if (-not ([System.Management.Automation.PSTypeName]'M14W2').Type) {
Add-Type @"
using System;using System.Runtime.InteropServices;
public static class M14W2 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, ref RECT r, int cb);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
}

function Get-Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 256
    [M14W]::GetWindowText($h, $sb, 256) | Out-Null
    $sb.ToString()
}
function Get-Tops { [M14W]::Tops() }

# 与探针同口径的可见外框（DWMWA_EXTENDED_FRAME_BOUNDS，物理像素）。
function Get-FrameBounds([IntPtr]$h) {
    $r = New-Object M14W2+RECT
    $hr = [M14W2]::DwmGetWindowAttribute($h, 9, [ref]$r, [System.Runtime.InteropServices.Marshal]::SizeOf([type][M14W2+RECT]))
    if ($hr -ne 0) { return $null }
    $r
}

# 把窗口提升到置顶带顶部（HWND_TOPMOST）。关键：当前环境用户可能活跃使用最大化
# Edge（前台），任何非置顶带的新窗口/恢复窗口都会排在前台窗口之下。置顶只作用于
# 我们自己的测试窗口，目标关闭后即消失，不影响用户窗口。
function Raise-Top([IntPtr]$h) {
    # HWND_TOPMOST=-1, SWP_NOSIZE|NOMOVE|NOACTIVATE = 0x0013
    [M14W]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
}
# 采样目标上缘可见比例（与探针同点位语义；y 取 rect.top+10，GetWindowRect 与
# extended frame bounds 差几像素，+10 保证在可见标题栏内）。
function Get-TopEdgeInfo([IntPtr]$h) {
    $r = New-Object M14W+RECT
    if (-not [M14W]::GetWindowRect($h, [ref]$r)) { return $null }
    $y = $r.T + 10
    $w = $r.R - $r.L
    $vis = 0
    $occ = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt 9; $i++) {
        $p = New-Object M14Hit+POINT
        $p.X = $r.L + [int][Math]::Round($w * ($i + 0.5) / 9)
        $p.Y = $y
        $hit = [M14Hit]::WindowFromPoint($p)
        $root = [M14Hit]::GetAncestor($hit, 2)
        if ($root -eq $h) { $vis++; continue }
        if ($root -ne [IntPtr]::Zero) {
            $t = Get-Title $root
            if ($t -and -not $occ.Contains($t)) { $occ.Add($t) }
        }
    }
    [pscustomobject]@{ Fraction = $vis / 9.0; Occluders = $occ }
}

function Read-Events {
    if (-not (Test-Path $eventsFile)) { return @() }
    Get-Content $eventsFile -ErrorAction SilentlyContinue | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json }
}
function Wait-Event([string]$type, [int]$timeoutSec = 15, [string]$requireHwnd = $null, [int]$afterMs = 0) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $ev = Read-Events | Where-Object { $_.type -eq $type -and $_.tMs -gt $afterMs }
        if ($requireHwnd) { $ev = $ev | Where-Object { $_.hwnd -eq $requireHwnd } }
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
function HwndHex([IntPtr]$h) { '0x' + $h.ToInt64().ToString('X') }

# ---- 检查清单 ----
$script:checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$name, [bool]$pass, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Item = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} : {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

# ---- 目标题材与启动器 ----
# notepad 标记绑定（F-03 收尾）：本轮独有临时测试文件，文件名即标记（写入窗口标题）。
# 初开与重开都打开它；驱动不再 SetWindowText 识别/改名，只在标记命中且归属确认后写入。
$script:markerFile = $null
if ($TargetKind -eq 'notepad') {
    $script:markerFile = Join-Path $runDir "P5AM14NP-$ts.txt"
    Set-Content -LiteralPath $script:markerFile -Value "P5A-PLAN M-14 marker file (run $ts)" -Encoding UTF8
    $targetSubstr = "P5AM14NP-$ts"
    # 清理历史轮的标记文件（run-20261118-080353 教训）：Win11 记事本"继续上次会话"
    # 会把上一轮遗留的标记文件作为标签页恢复进本轮窗口，活动标签切走后窗口标题
    # 脱离本轮标记，探针按标题跟踪断链。标记文件是驱动生成的临时测试材料，
    # 各 run 目录的清单/转录/事件证据不受影响。
    Get-ChildItem (Split-Path $runDir -Parent) -Directory -Filter 'run-*' |
        ForEach-Object { Get-ChildItem $_.FullName -File -Filter 'P5AM14NP-*.txt' -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -ne $script:markerFile } |
        Remove-Item -Force -ErrorAction SilentlyContinue
} else {
    # Edge 标记绑定（F-03 收尾）：页面标题带上本轮唯一标记，页面/探针/驱动共用。
    # 旧固定标题 'P5AM14Browser' 在两轮并行时会互相误选（监理 F-03 Edge 收尾要求）；
    # 本轮标记唯一且另一个 profile 的旧标题窗口必须被只读跳过。
    $targetSubstr = "P5AM14Browser-$ts"
    $htmlFile = Join-Path $runDir 'p5am14-page.html'
    Set-Content -Path $htmlFile -Value "<!doctype html><html><head><title>$targetSubstr</title></head><body><h1>P5A M14 window-perch test target</h1></body></html>" -Encoding UTF8
}
function Start-Target {
    # 返回本轮启动的目标进程（供归属确认 pid 精确匹配）
    if ($TargetKind -eq 'notepad') {
        Start-Process notepad -ArgumentList "`"$script:markerFile`"" -PassThru
    } else {
        Start-Process msedge -ArgumentList "--user-data-dir=`"$runDir\edge-profile`"", '--no-first-run', '--window-size=900,600', "`"file:///$htmlFile`"" -PassThru
    }
}
function Test-EdgeHostOwned([uint32]$procId) {
    # Edge 宿主归属（F-03 Edge 收尾）：窗口宿主进程命令行必须含本轮 profile 路径
    # （`--user-data-dir="<runDir>\edge-profile"`）。带引号结尾做精确匹配，避免
    # `edge-profile-other` 等前缀相同的其他 profile 误过。
    $profileArg = (Join-Path $runDir 'edge-profile') + '"'
    $cp = Get-CimInstance Win32_Process -Filter "ProcessId=$procId" -ErrorAction SilentlyContinue
    return ($null -ne $cp -and $cp.CommandLine -and $cp.CommandLine.Contains($profileArg))
}

# ---- 安全预检：不允许已存在 p5am14 标题窗口 ----
foreach ($h in Get-Tops) {
    if ((Get-Title ([IntPtr]$h)) -match 'p5am14') { throw "existing p5am14 window found (0x$($h.ToString('X'))) - abort" }
}
# ---- 安全预检（notepad）：Win11 记事本单实例，新开文件/窗口可能并入用户既有实例的
#      进程（run-20261117-210809 事故根因：驱动误占用用户进程的窗口）。凡系统中已有
#      记事本窗口，notepad 目标一律中止；请改用 -TargetKind edge，或先关闭用户记事本。
if ($TargetKind -eq 'notepad') {
    $existingNp = @(Get-Tops | ForEach-Object { Get-Title ([IntPtr]$_) } | Where-Object { $_ -like '*- Notepad' })
    if ($existingNp.Count -gt 0) {
        throw "existing Notepad window(s) found: [$($existingNp -join '; ')] - notepad target unsafe (single-instance may merge into user's windows); use -TargetKind edge or close user Notepad first"
    }
}
Write-Host "[$TargetKind] run dir: $runDir"
Write-Host "[$TargetKind] targetSubstr='$targetSubstr' (unique; user windows cannot match)"

# config（探针场景目录内）
@{ targetTitleSubstr = $targetSubstr; durationSec = $DurationSec; pollMs = $PollMs } |
    ConvertTo-Json | Set-Content (Join-Path $scenarioDir 'window-perch-config.json') -Encoding UTF8

$before = Get-Tops
$externalSeen = New-Object System.Collections.Generic.List[string]

# ---- F-03 收尾：不同 PID 外部诱饵（仅 notepad）----
# 诱饵由独立 pwsh 进程创建，在驱动启动后出现、标题符合旧识别规则（'*- Notepad'）、
# 但不含本轮标记、不属于本轮启动的目标进程。初始发现与重开发现都必须只读跳过它
# （零写入），全程不改标题、不移动、不关闭；真正带标记的测试窗口仍须通过全部检查。
$decoyProc = $null; $decoyHwnd = [IntPtr]::Zero; $decoyPid = 0
$decoyTitle = $null; $decoyRectBefore = $null; $decoySeen = $false
if ($TargetKind -eq 'notepad') {
    $decoyScriptPath = Join-Path $runDir 'decoy-window.ps1'
    $decoyOutFile = Join-Path $runDir 'decoy-window-info.txt'
    @'
param([string]$OutFile)
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object System.Windows.Forms.Form
$f.Text = '外部候选对照 - Notepad'
$f.StartPosition = 'Manual'
$f.Left = 150; $f.Top = 150; $f.Width = 320; $f.Height = 140
$f.Show()
"$($f.Handle.ToInt64())`t$PID" | Set-Content -LiteralPath $OutFile -Encoding UTF8
[System.Windows.Forms.Application]::Run($f)
'@ | Set-Content -LiteralPath $decoyScriptPath -Encoding UTF8
    $decoyProc = Start-Process pwsh -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$decoyScriptPath`"", '-OutFile', "`"$decoyOutFile`"" -PassThru -WindowStyle Hidden
    $decoyDeadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $decoyDeadline -and -not (Test-Path $decoyOutFile)) { Start-Sleep -Milliseconds 300 }
    if (Test-Path $decoyOutFile) {
        $decoyParts = (Get-Content $decoyOutFile -Raw).Trim() -split "`t"
        $decoyHwnd = [IntPtr][int64]$decoyParts[0]
        $decoyPid = [int]$decoyParts[1]
        $decoyTitle = Get-Title $decoyHwnd
        $decoyRectBefore = New-Object M14W+RECT
        [M14W]::GetWindowRect($decoyHwnd, [ref]$decoyRectBefore) | Out-Null
        # 诱饵成立条件：不同 PID、驱动启动后出现（StartTime 旧规则会达标）
        $decoySeen = ($decoyPid -ne $PID) -and ($decoyProc.StartTime -ge $script:t0)
        Write-Host "[$TargetKind] decoy window up: hwnd=$(HwndHex $decoyHwnd) pid=$decoyPid (driver pid=$PID) title='$decoyTitle' startedAfterT0=$($decoyProc.StartTime -ge $script:t0)"
    } else {
        Write-Warning "decoy window did not appear - decoy contrast not available"
    }
}

# ---- F-03 Edge 收尾：其他轮对照窗口（真实 msedge，不同 profile，旧固定标题）----
# 旧识别规则是固定标题 `P5AM14Browser`：两轮并行时另一轮的窗口会互相误选。
# 本轮页面标题带唯一标记 `P5AM14Browser-<ts>`；对照窗口是驱动启动后出现的另一个
# 真实 msedge 窗口（独立 user-data-dir `edge-profile-other`、页面标题仍为旧规则
# `P5AM14Browser`、宿主进程命令行不含本轮 profile 路径）。初开/重开发现都必须
# 只读跳过它（零写入、不改标题、不移动、不关闭），本轮标记窗口正常通过全部检查。
$otherProc = $null; $otherHwnd = [IntPtr]::Zero; $otherPid = 0
$otherTitle = $null; $otherRectBefore = $null; $otherSeen = $false
if ($TargetKind -eq 'edge') {
    $otherHtml = Join-Path $runDir 'p5am14-other-round.html'
    Set-Content -Path $otherHtml -Value '<!doctype html><html><head><title>P5AM14Browser</title></head><body><h1>other round (old fixed title)</h1></body></html>' -Encoding UTF8
    $otherProfile = Join-Path $runDir 'edge-profile-other'
    $otherProc = Start-Process msedge -ArgumentList "--user-data-dir=`"$otherProfile`"", '--no-first-run', '--window-size=500,400', '--window-position=80,80', "`"file:///$otherHtml`"" -PassThru
    $otherDeadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $otherDeadline -and $otherHwnd -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 500
        foreach ($h in Get-Tops) {
            if ($before -contains $h) { continue }
            $t = Get-Title ([IntPtr]$h)
            if ($t -notlike '*P5AM14Browser*' -or $t -like "*$targetSubstr*") { continue }
            $otherHwnd = [IntPtr]$h
            [M14W2]::GetWindowThreadProcessId($otherHwnd, [ref]$otherPid) | Out-Null
            $otherTitle = $t
            $otherRectBefore = New-Object M14W+RECT
            [M14W]::GetWindowRect($otherHwnd, [ref]$otherRectBefore) | Out-Null
            $otherSeen = $true
            break
        }
    }
    if ($otherSeen) {
        Write-Host "[$TargetKind] other-round contrast window up: hwnd=$(HwndHex $otherHwnd) pid=$otherPid title='$otherTitle' (old fixed-title rule; zero write)"
    } else {
        Write-Warning "other-round contrast window did not appear - edge contrast not available"
    }
}

# ---- 启动探针（隐藏控制台，避免遮挡目标）----
$probe = Start-Process -FilePath $probeExe -ArgumentList "`"$runDir`"", 'window-perch', "`"$resultFile`"" -PassThru -WindowStyle Hidden
Write-Host "[$TargetKind] probe started pid=$($probe.Id) (console hidden)"

$cover = $null
try {
    # 1. 启动目标并识别新窗口（标记绑定：notepad 打开本轮独有临时文件，标题自然
    #    含本轮标记；识别不靠 SetWindowText，窗口标题由记事本自身维护）
    $targetProc = Start-Target
    Write-Host "[$TargetKind] target start issued pid=$($targetProc.Id)"
    $hwnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 400
        foreach ($h in Get-Tops) {
            if ($before -contains $h) { continue }
            $t = Get-Title ([IntPtr]$h)
            # 标记不匹配的候选（含不同 PID 诱饵等旧规则窗口）只读跳过，零写入
            if ($t -notlike "*$targetSubstr*") { continue }
            # 标记命中后归属确认（发现循环内、任何写入之前）：
            #   notepad: 宿主必须是本轮启动后新出现的记事本进程（ProcessName='Notepad'、
            #            StartTime >= t0-2s、非驱动/诱饵进程）。启动器 pid 与实际宿主
            #            pid 可能不同（Win11 记事本把窗口交给子进程），不能按启动器
            #            pid 精确匹配；单实例把窗口并入既有进程时 StartTime 不达标，
            #            按归属不明跳过（210809 防线）。
            #   edge:    多进程架构无法 pid 精确匹配，沿用 StartTime >= t0-2s 且
            #            pid != 驱动进程；edge 标题来自本轮本地 html，标记即绑定。
            $cPid = [uint32]0
            [M14W2]::GetWindowThreadProcessId([IntPtr]$h, [ref]$cPid) | Out-Null
            $cProc = Get-Process -Id $cPid -ErrorAction SilentlyContinue
            $owned = $(if ($TargetKind -eq 'notepad') {
                           $null -ne $cProc -and $cProc.ProcessName -eq 'Notepad' -and
                           $cPid -ne $PID -and $cPid -ne $decoyPid -and
                           $cProc.StartTime -ge $script:t0.AddSeconds(-2)
                       }
                       else {
                           # Edge 宿主归属：除 StartTime 外，还须命令行含本轮 profile 路径。
                           # 其他轮/其他 profile 的 msedge 窗口即使标题命中标记也按归属不明只读跳过。
                           $null -ne $cProc -and $cPid -ne $PID -and (Test-EdgeHostOwned $cPid) -and
                           $cProc.StartTime -ge $script:t0.AddSeconds(-2)
                       })
            if (-not $owned) {
                if (-not $externalSeen.Contains($t)) {
                    $externalSeen.Add($t)
                    Write-Host "[$TargetKind] marker-matched candidate skipped (zero write): hwnd=0x$($h.ToString('X')) title='$t' pid=$cPid"
                }
                continue
            }
            $hwnd = [IntPtr]$h
            $ownPid = $cPid
            $ownProc = $cProc
            break
        }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw "no marker-owned target window appeared - aborting (user windows untouched)" }
    # 标题漂移中止检查（run-20261118-080353 教训）：记事本会话恢复/用户切标签页会把
    # 活动标签切走，窗口标题脱离本轮标记；此时继续操作虽安全（归属已确认）但探针
    # 无法跟踪，按归属不明中止，不碰任何窗口。
    Start-Sleep -Milliseconds 1500
    if ((Get-Title $hwnd) -notlike "*$targetSubstr*") {
        throw "target window title drifted off this run's marker (got '$(Get-Title $hwnd)') - aborting (user windows untouched)"
    }
    Write-Host "[$TargetKind] target ownership verified BEFORE any write: hwnd=$(HwndHex $hwnd) pid=$ownPid started=$($ownProc.StartTime)"
    $hwndHex = HwndHex $hwnd
    Write-Host "[$TargetKind] target window $hwndHex title='$(Get-Title $hwnd)'"
    # 立即移到屏幕右下清空区（避开驱动终端等常驻窗口对上缘的真实遮挡），并置于前台
    # 显式尺寸 800x500 物理像素，保证后续移动/拖动均不越出 2560x1440 单屏
    [M14W]::SetWindowPos($hwnd, [IntPtr]::Zero, 1450, 800, 800, 500, 0x0004) | Out-Null
    Raise-Top $hwnd
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Seconds 2

    # 2. select 断言
    $sel = Wait-Event 'select' 20
    Add-Check 'select 命中我们的唯一标题窗口' ($null -ne $sel -and $sel.hwnd -eq $hwndHex) "probe select hwnd=$($sel.hwnd) title=$($sel.title)"
    if (-not $sel -or $sel.hwnd -ne $hwndHex) { throw 'probe selected wrong/absent target - aborting' }

    # 3. 慢速移动（保持在清空区内）+ F-01：静止不重复上报、移回初始位置有更新
    $r = New-Object M14W+RECT
    [M14W]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $hgt = $r.B - $r.T
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    # 移动前的外框基线（select 或 update 事件的 bounds；探针上报 DWM 可见外框，
    # 与 SetWindowPos 的窗口 rect 相差不可见边框，不能按 rect 常量断言）。
    $beforeMove = Read-Events | Where-Object { $_.type -eq 'select' -or $_.type -eq 'update' } | Select-Object -Last 1
    $wm = Get-LastEventMs
    [M14W]::SetWindowPos($hwnd, [IntPtr]::Zero, 1500, 830, $w, $hgt, 0x0004) | Out-Null
    $mv = Wait-Event 'update' 10 -afterMs $wm
    Add-Check '移动跟踪（update 事件）' ($null -ne $mv) "bounds=[$($mv.bounds -join ',')]"
    if ($null -eq $mv) { throw 'move update missing - aborting' }
    # F-01：等外框稳定（最后一个 update 后 1s 无新事件，最多 5s），再观察静止窗口。
    # update 以上一次已上报外框为基线，静止期间不得重复上报。
    $settleDeadline = (Get-Date).AddSeconds(5)
    do {
        $settleUps = @(Read-Events | Where-Object { $_.type -eq 'update' -and $_.tMs -gt $wm })
        $settleLastT = $(if ($settleUps) { $settleUps[-1].tMs } else { 0 })
        Start-Sleep -Milliseconds 300
        if ($settleLastT -gt 0 -and (Get-LastEventMs) -eq $settleLastT) { break }
    } while ((Get-Date) -lt $settleDeadline)
    $stable = $settleUps | Select-Object -Last 1
    Start-Sleep -Milliseconds 1200
    $quietUps = @(Read-Events | Where-Object { $_.type -eq 'update' -and $_.tMs -gt $stable.tMs })
    Add-Check '移动后静止不重复上报' ($quietUps.Count -eq 0) "stableBounds=[$($stable.bounds -join ',')] extraUpdates=$($quietUps.Count)"
    # F-01：移回初始位置。基线是上一次已上报外框（≠初始外框），必须再次上报，
    # 且最终外框应与移动前的外框一致。
    $wm = Get-LastEventMs
    [M14W]::SetWindowPos($hwnd, [IntPtr]::Zero, 1450, 800, $w, $hgt, 0x0004) | Out-Null
    $back = Wait-Event 'update' 10 -afterMs $wm
    $backOk = ($null -ne $back) -and ($null -ne $beforeMove) -and
        (($back.bounds -join ',') -eq ($beforeMove.bounds -join ',')) -and
        (($back.bounds -join ',') -ne ($stable.bounds -join ','))
    Add-Check '移回初始位置仍有 update' $backOk "bounds=[$($back.bounds -join ',')] beforeMove=[$($beforeMove.bounds -join ',')] stable=[$($stable.bounds -join ',')]"

    # 4. 快速拖动（20 步 × 60ms，模拟快速拖动的连续位移；终点 (1700,930) 仍在屏内）
    #    F-01：最终 update 事件外框须与驱动实测可见外框（DWM 同口径）逐项一致。
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    $wm = Get-LastEventMs
    for ($i = 1; $i -le 20; $i++) {
        [M14W]::SetWindowPos($hwnd, [IntPtr]::Zero, 1500 + $i * 10, 830 + $i * 5, $w, $hgt, 0x0004) | Out-Null
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Milliseconds 800
    $dragUpdates = @(Read-Events | Where-Object { $_.type -eq 'update' -and $_.tMs -gt $wm })
    $lastUp = $dragUpdates | Select-Object -Last 1
    Add-Check '快速拖动跟踪（多次 update）' ($dragUpdates.Count -ge 2 -and $null -ne $lastUp) "updateCount=$($dragUpdates.Count) lastBounds=[$($lastUp.bounds -join ',')]"
    $fb = Get-FrameBounds $hwnd
    $finOk = ($null -ne $lastUp -and $null -ne $fb) -and
        ($lastUp.bounds[0] -eq $fb.L -and $lastUp.bounds[1] -eq $fb.T -and $lastUp.bounds[2] -eq $fb.R -and $lastUp.bounds[3] -eq $fb.B)
    Add-Check '快速移动最终外框与实测一致' $finOk "event=[$($lastUp.bounds -join ',')] measured=[$($fb.L),$($fb.T),$($fb.R),$($fb.B)]"

    # 4b. F-01 补充：真实鼠标快速拖动（mouse_event 注入真实鼠标输入，仅作用于自建测试窗口标题栏）
    [M14W]::SetWindowPos($hwnd, [IntPtr]::Zero, 1450, 800, 800, 500, 0x0004) | Out-Null
    Raise-Top $hwnd
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 800
    $md = New-Object M14W+RECT
    [M14W]::GetWindowRect($hwnd, [ref]$md) | Out-Null
    # 抓取点取标题栏中部：GetWindowRect 含 DWM 阴影，+12 可能落在不可见上边框
    # （HTTOP，按下只缩放不拖动）；+25 在 150% DPI 下仍在标题栏内。
    $grabY = $md.T + 25
    # 抓取点必须位于 HTCAPTION（可拖动标题栏）：Win11 记事本标签页占用标题栏中部，
    # 按下落在标签页上不会启动窗口拖动（run-085703 实测：前置校验全过、注入完成、
    # 窗口零移动）。注入前用 WM_NCHITTEST 确认；默认中心点不是 HTCAPTION 时沿标题栏
    # 从右向左扫描（避开右侧.caption 按钮），找不到 HTCAPTION 则安全跳过注入。
    $htAt = { param($x, $y)
        $lp = [IntPtr](([int64]($y -shl 16) -bor ($x -band 0xFFFF)))
        [M14W2]::SendMessage($hwnd, 0x0084, [IntPtr]::Zero, $lp).ToInt64() }
    $grabX = $md.L + [int](($md.R - $md.L) / 2)
    if ((& $htAt $grabX $grabY) -ne 2) {
        $found = $false
        for ($sx = ($md.R - 260); $sx -ge ($md.L + 30); $sx -= 10) {
            if ((& $htAt $sx $grabY) -eq 2) { $grabX = $sx; $found = $true; break }
        }
        if (-not $found) { $grabX = -1 }
        Write-Host "[$TargetKind] title-bar center not HTCAPTION - scanning drag handle: grabX=$grabX"
    }
    if ($grabX -lt 0) {
        Write-Host "[$TargetKind] real-mouse drag skipped: no HTCAPTION drag handle on title bar"
        Add-Check '真实鼠标快速拖动（update+最终外框）' $false "skipped: no HTCAPTION drag handle on title bar"
    }
    $preFb = Get-FrameBounds $hwnd   # 注入前外框（证明窗口确实被真实拖动）
    # 前台处理：目标不在前台时，注入的 LEFTDOWN 会被系统当作激活点击吞掉、不启动
    # 标题栏拖动（run-221512 实测）。SetForegroundWindow 受前台锁定限制对 Edge 无效
    # （run-222856/223211 实测，最小化还原也不行）。改为：先确认光标命中本窗口，
    # 若仍未前台则在自建窗口标题栏上注入一次安全的“激活点击”（命中已验证，点击
    # 只会落在本窗口上，绝不触达用户窗口），激活后再做拖动。
    for ($try = 1; $try -le 3; $try++) {
        if ([M14W2]::GetForegroundWindow() -eq $hwnd) { break }
        [M14W]::SetForegroundWindow($hwnd) | Out-Null
        Start-Sleep -Milliseconds 300
    }
    $wm = Get-LastEventMs   # 拖动前水位（重定位产生的 update 不计入拖动）
    # 注入前置校验：SetCursorPos 须生效（±2px）且光标命中本窗口标题栏才注入按钮事件。
    # 用户活跃使用鼠标时注入位移会被物理鼠标覆盖（run-214033 实测），此时注入
    # LEFTDOWN 会点击到用户窗口——绝对禁止。重试 5 次仍不生效则判环境受限跳过注入。
    $posOk = ($grabX -ge 0)
    $readback = ''
    for ($try = 1; $try -le 5 -and $posOk; $try++) {
        [M14W2]::SetCursorPos($grabX, $grabY) | Out-Null
        $cp = New-Object M14W2+POINT
        [M14W2]::GetCursorPos([ref]$cp) | Out-Null
        $readback = "$($cp.X),$($cp.Y)"
        if ([Math]::Abs($cp.X - $grabX) -le 2 -and [Math]::Abs($cp.Y - $grabY) -le 2) { $posOk = $true; break }
        Start-Sleep -Milliseconds 300
    }
    $hitRoot = [IntPtr]::Zero
    if ($posOk) {
        $hitPt = New-Object M14Hit+POINT
        $hitPt.X = $grabX; $hitPt.Y = $grabY
        $hitRoot = [M14Hit]::GetAncestor([M14Hit]::WindowFromPoint($hitPt), 2)
        if ($hitRoot -ne $hwnd) { $posOk = $false }
    }
    if (-not $posOk) {
        Write-Host "[$TargetKind] real-mouse drag skipped: cursor injection ineffective (readback=$readback hit=$(HwndHex $hitRoot)) - user mouse active"
    }
    # 若未前台：光标已验证命中本窗口标题栏，注入一次激活点击是安全的（只点击自建窗口）。
    if ($posOk -and ([M14W2]::GetForegroundWindow() -ne $hwnd)) {
        Write-Host "[$TargetKind] target not foreground after SetForegroundWindow retries - injecting safe activation click on own title bar"
        for ($try = 1; $try -le 3; $try++) {
            $cpA = New-Object M14W2+POINT
            [M14W2]::GetCursorPos([ref]$cpA) | Out-Null
            $hitA = New-Object M14Hit+POINT
            $hitA.X = $cpA.X; $hitA.Y = $cpA.Y
            if ([M14Hit]::GetAncestor([M14Hit]::WindowFromPoint($hitA), 2) -ne $hwnd) {
                Write-Host "[$TargetKind] activation click aborted: cursor drifted off target at try=$try"
                $posOk = $false
                break
            }
            [M14W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
            [M14W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
            Start-Sleep -Milliseconds 500
            if ([M14W2]::GetForegroundWindow() -eq $hwnd) { break }
        }
        if ([M14W2]::GetForegroundWindow() -ne $hwnd) {
            Write-Host "[$TargetKind] real-mouse drag skipped: activation click failed to bring target foreground"
            $posOk = $false
        }
    }
    if ($posOk) {
        # 等待后瞬时复核：物理鼠标可能已挪走光标，此时 LEFTDOWN 会点到别处。
        # 任何一项失守立即跳过。
        Start-Sleep -Milliseconds 150
        $cp2 = New-Object M14W2+POINT
        [M14W2]::GetCursorPos([ref]$cp2) | Out-Null
        $rePosOk = ([Math]::Abs($cp2.X - $grabX) -le 2 -and [Math]::Abs($cp2.Y - $grabY) -le 2)
        $reFgOk = ([M14W2]::GetForegroundWindow() -eq $hwnd)
        $reHitPt = New-Object M14Hit+POINT
        $reHitPt.X = $cp2.X; $reHitPt.Y = $cp2.Y
        $reHitRoot = [M14Hit]::GetAncestor([M14Hit]::WindowFromPoint($reHitPt), 2)
        Write-Host "[$TargetKind] real-mouse inject begin: cursor=$($cp2.X),$($cp2.Y) fg=$(HwndHex ([M14W2]::GetForegroundWindow())) hit=$(HwndHex $reHitRoot)"
        if (-not ($rePosOk -and $reFgOk -and ($reHitRoot -eq $hwnd))) {
            $posOk = $false
            Write-Host "[$TargetKind] real-mouse drag skipped at last recheck: pos=$rePosOk fg=$reFgOk hitOk=$($reHitRoot -eq $hwnd)"
        }
    }
    if ($posOk) {
        [M14W2]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # MOUSEEVENTF_LEFTDOWN
        for ($i = 1; $i -le 20; $i++) {
            [M14W2]::SetCursorPos(($grabX + $i * 8), ($grabY + $i * 4)) | Out-Null
            Start-Sleep -Milliseconds 30
        }
        [M14W2]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # MOUSEEVENTF_LEFTUP
        $cp3 = New-Object M14W2+POINT
        [M14W2]::GetCursorPos([ref]$cp3) | Out-Null
        Write-Host "[$TargetKind] real-mouse inject done: endCursor=$($cp3.X),$($cp3.Y) fg=$(HwndHex ([M14W2]::GetForegroundWindow()))"
    } else {
        Write-Host "[$TargetKind] real-mouse drag skipped: cursor injection ineffective (readback=$readback hit=$(HwndHex $hitRoot)) - user mouse active"
    }
    Start-Sleep -Milliseconds 800
    $mouseUps = @(Read-Events | Where-Object { $_.type -eq 'update' -and $_.tMs -gt $wm })
    $lastMouseUp = $mouseUps | Select-Object -Last 1
    $fb2 = Get-FrameBounds $hwnd
    # 600ms 快速拖动在 200ms 轮询下仅产生 1~3 次 update（F-01 去重语义），因此要求
    # updateCount>=1 而非 >=2；核心断言：最终事件外框与驱动实测一致，且窗口确实被
    # 真实鼠标拖离注入前位置。
    $mouseOk = ($mouseUps.Count -ge 1) -and ($null -ne $lastMouseUp) -and ($null -ne $fb2) -and ($null -ne $preFb) -and
        ($lastMouseUp.bounds[0] -eq $fb2.L -and $lastMouseUp.bounds[1] -eq $fb2.T -and
         $lastMouseUp.bounds[2] -eq $fb2.R -and $lastMouseUp.bounds[3] -eq $fb2.B) -and
        (-not ($fb2.L -eq $preFb.L -and $fb2.T -eq $preFb.T))
    Add-Check '真实鼠标快速拖动（update+最终外框）' $mouseOk "updateCount=$($mouseUps.Count) lastEvent=[$($lastMouseUp.bounds -join ',')] measured=[$($fb2.L),$($fb2.T),$($fb2.R),$($fb2.B)] preDrag=[$($preFb.L),$($preFb.T),$($preFb.R),$($preFb.B)]"

    # 5. 最大化 → 恢复
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    $wm = Get-LastEventMs
    [M14W]::ShowWindow($hwnd, 3) | Out-Null   # SW_MAXIMIZE
    Start-Sleep -Seconds 2
    $maxUp = Read-Events | Where-Object { $_.type -eq 'update' -and $_.tMs -gt $wm -and $_.bounds[0] -eq 0 -and $_.bounds[1] -eq 0 } | Select-Object -Last 1
    Add-Check '最大化跟踪（bounds 原点 0,0）' ($null -ne $maxUp) "bounds=[$($maxUp.bounds -join ',')]"
    [M14W]::ShowWindow($hwnd, 1) | Out-Null   # SW_RESTORE
    Start-Sleep -Seconds 2
    Raise-Top $hwnd

    # 6. 最小化 → cleared(Minimized) → 恢复 → reacquire 同句柄
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    $wm = Get-LastEventMs
    [M14W]::ShowWindow($hwnd, 6) | Out-Null   # SW_MINIMIZE
    $clMin = Wait-Event 'cleared' 12 -afterMs $wm
    Add-Check '最小化 → cleared(Minimized)' ($null -ne $clMin -and $clMin.reason -eq 'Minimized') "reason=$($clMin.reason)"
    # 水位必须在还原动作之前采样：探针可能在还原后立即 reacquire（run-214033 实测
    # 14615ms 已上报，而原实现还原后才取水位导致竞态漏检）。
    $wmRestore = Get-LastEventMs
    [M14W]::ShowWindow($hwnd, 1) | Out-Null   # SW_RESTORE
    Start-Sleep -Milliseconds 500
    Raise-Top $hwnd
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    $ra1 = Wait-Event 'reacquire' 12 $hwndHex $wmRestore
    Add-Check '恢复 → reacquire 同句柄' ($null -ne $ra1) "hwnd=$($ra1.hwnd)"

    # 7. 上缘遮挡 → occluded → 解除 → unoccluded（自建覆盖窗，只盖目标上缘）
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 800
    # 基线门控：遮挡事件只在 fraction 1.0→<1.0 翻转时产生。若目标上缘当前被
    # 其他窗口盖住（如用户最大化的 Edge），先提升 Z 序直至基线可见再盖覆盖窗。
    $baseOk = $false; $baseInfo = $null
    foreach ($attempt in 1..5) {
        Raise-Top $hwnd
        Start-Sleep -Milliseconds 400
        $baseInfo = Get-TopEdgeInfo $hwnd
        if ($baseInfo -and $baseInfo.Fraction -ge 1.0) { $baseOk = $true; break }
    }
    if (-not $baseOk) {
        Add-Check '上缘遮挡 → occluded(fraction<1)' $false "baseline fraction=$($baseInfo.Fraction) occluders=[$($baseInfo.Occluders -join '; ')] - cannot test"
        Add-Check '遮挡解除 → unoccluded' $false 'skipped: baseline not visible'
    } else {
        Write-Host "[$TargetKind] occlusion baseline fraction=1.0 (top raised above occluders)"
        $r2 = New-Object M14W+RECT
        [M14W]::GetWindowRect($hwnd, [ref]$r2) | Out-Null
        $cover = New-Object System.Windows.Forms.Form
        $cover.Text = "P5AM14Cover-$ts"
        $cover.StartPosition = 'Manual'
        $cover.Left = $r2.L + 40; $cover.Top = $r2.T - 5
        $cover.Width = [Math]::Min(500, ($r2.R - $r2.L) - 80); $cover.Height = 260
        $cover.TopMost = $true
        $wm = Get-LastEventMs
        $cover.Show()
        # 双保险：显式 HWND_TOPMOST（-1）置顶覆盖窗
        [M14W]::SetWindowPos($cover.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x0013) | Out-Null
        # Edge 轮实测：后台进程的 TopMost 变更后，覆盖窗可能仍排在目标之下。
        # 显式把目标插入覆盖窗正下方（hWndInsertAfter=cover），保证上缘被盖住。
        [M14W]::SetWindowPos($hwnd, $cover.Handle, 0, 0, 0, 0, 0x0013) | Out-Null
        Start-Sleep -Milliseconds 500
        $covHit = New-Object M14Hit+POINT
        $covHit.X = $cover.Left + [int]($cover.Width / 2); $covHit.Y = $cover.Top + 50
        $covRoot = [M14Hit]::GetAncestor([M14Hit]::WindowFromPoint($covHit), 2)
        Write-Host "[$TargetKind] cover z-check hit=0x$($covRoot.ToString('X')) cover=0x$($cover.Handle.ToString('X')) match=$($covRoot -eq $cover.Handle)"
        $oc = Wait-Event 'occluded' 12 -afterMs $wm
        Add-Check '上缘遮挡 → occluded(fraction<1)' ($null -ne $oc -and $oc.fraction -lt 1) "fraction=$($oc.fraction) occluders=[$($oc.occluders -join '; ')] coverHit=$($covRoot -eq $cover.Handle)"
        # 水位必须在 Hide 之前采样：探针 200ms 轮询会在覆盖窗隐藏后立即上报
        # unoccluded（run-004822 实测仅晚 430ms），Hide 之后的 Raise-Top 含
        # 多段 sleep，还原后取水位会晚于事件本身导致 -gt 比较漏检。
        $wm = Get-LastEventMs
        $cover.Hide(); $cover.Dispose(); $cover = $null
        # 目标此前被插到覆盖窗之下；恢复其置顶带位置，供后续步骤稳定运行
        Raise-Top $hwnd
        $un = Wait-Event 'unoccluded' 12 -afterMs $wm
        Add-Check '遮挡解除 → unoccluded' ($null -ne $un) "fraction=$($un.fraction)"
    }

    # 8. 关闭 → cleared（WindowGone，或先 Minimized 随后窗口销毁）
    # Edge 收 WM_CLOSE 后先短暂最小化再销毁（事件流出现 cleared/Minimized），
    # 因此接受首个 cleared 后，继续等 WindowGone 或用 IsWindow 确认窗口已销毁。
    $wm = Get-LastEventMs
    [M14W]::SetForegroundWindow($hwnd) | Out-Null
    [M14W]::SendMessage($hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $clGone = Wait-Event 'cleared' 12 $hwndHex $wm
    $goneOk = $false; $goneDetail = 'no cleared event'
    if ($clGone) {
        if ($clGone.reason -eq 'WindowGone') {
            $goneOk = $true; $goneDetail = 'reason=WindowGone'
        } else {
            $goneDetail = "firstReason=$($clGone.reason)"
            $deadlineGone = (Get-Date).AddSeconds(10)
            while ((Get-Date) -lt $deadlineGone) {
                $follow = Read-Events | Where-Object { $_.type -eq 'cleared' -and $_.tMs -ge $wm -and $_.reason -eq 'WindowGone' } | Select-Object -Last 1
                if ($follow) { $goneOk = $true; $goneDetail = "firstReason=$($clGone.reason) + followUp=WindowGone"; break }
                if (-not [M14W2]::IsWindow($hwnd)) { $goneOk = $true; $goneDetail = "firstReason=$($clGone.reason) + IsWindow=false (destroyed)"; break }
                Start-Sleep -Milliseconds 200
            }
        }
    }
    Add-Check '关闭 → cleared(窗口已失效)' $goneOk $goneDetail

    # 9. 重开 → 重新验证目标（F-03 收尾：标记绑定重开 + 归属确认前置，零写入）
    #    notepad 重开同样打开本轮标记文件：新窗口标题自然含标记（不 SetWindowText）。
    #    归属确认：notepad 宿主必须是本轮启动后新出现的记事本进程（初开/重开同一
    #    标记文件、同族记事本进程）；不同 PID 诱饵窗口虽在驱动启动后出现且标题符合
    #    旧规则（'*- Notepad'），但不含本轮标记，标记过滤即排除，全程零写入
    #    （run-014959 教训）。
    $wm = Get-LastEventMs
    $reopenProc = Start-Target
    $hwnd2 = [IntPtr]::Zero
    $hwnd2Owned = $false
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline -and -not $hwnd2Owned) {
        Start-Sleep -Milliseconds 400
        foreach ($hh in Get-Tops) {
            if ($before -contains $hh -or $hh -eq $hwnd.ToInt64()) { continue }
            $t = Get-Title ([IntPtr]$hh)
            # 标记不匹配的候选（含不同 PID 诱饵）只读跳过，零写入
            if ($t -notlike "*$targetSubstr*") { continue }
            $own2Pid = [uint32]0
            [M14W2]::GetWindowThreadProcessId([IntPtr]$hh, [ref]$own2Pid) | Out-Null
            $own2Proc = Get-Process -Id $own2Pid -ErrorAction SilentlyContinue
            $owned2 = $(if ($TargetKind -eq 'notepad') {
                            $null -ne $own2Proc -and $own2Proc.ProcessName -eq 'Notepad' -and
                            $own2Pid -ne $PID -and $own2Pid -ne $decoyPid -and
                            $own2Proc.StartTime -ge $script:t0.AddSeconds(-2)
                        }
                        else {
                            $null -ne $own2Proc -and $own2Pid -ne $PID -and (Test-EdgeHostOwned $own2Pid) -and
                            $own2Proc.StartTime -ge $script:t0.AddSeconds(-2)
                        })
            if (-not $owned2) {
                if (-not $externalSeen.Contains($t)) {
                    $externalSeen.Add($t)
                    Write-Host "[$TargetKind] marker-matched candidate skipped (zero write): hwnd=0x$($hh.ToString('X')) title='$t' pid=$own2Pid"
                }
                continue
            }
            # 归属确认后才允许写入
            $hwnd2 = [IntPtr]$hh
            $hwnd2Owned = $true
            Write-Host "[$TargetKind] reopened ownership verified BEFORE any write: hwnd=$(HwndHex $hwnd2) pid=$own2Pid started=$($own2Proc.StartTime)"
            break
        }
    }
    if (-not $hwnd2Owned) {
        Write-Warning "no marker-owned reopened window found (external candidates untouched)"
    }
    $hwnd2Hex = $(if ($hwnd2Owned) { HwndHex $hwnd2 } else { '?' })
    $reEv = $null
    $deadlineRe = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadlineRe -and -not $reEv) {
        $reEv = Read-Events | Where-Object { $_.tMs -ge $wm -and $_.type -eq 'reacquire' } | Select-Object -Last 1
        if (-not $reEv) { Start-Sleep -Milliseconds 200 }
    }
    # F-03 收紧：必须已找到并验证本轮新窗口（hwnd2Owned），且探针事件句柄与其一致；
    # 没找到/归属不明时本检查必须失败（旧条件在 hwnd2 为空时也能通过）。
    $reacqOk = $hwnd2Owned -and ($null -ne $reEv) -and ($reEv.title -like "*$targetSubstr*") -and
        ($reEv.hwnd -eq $hwnd2Hex)
    Add-Check '重开 → reacquire 重新验证（已确认归属的新窗口）' $reacqOk "probe=$($reEv.hwnd) type=$($reEv.type) reopened=$hwnd2Hex owned=$hwnd2Owned title=$($reEv.title) sameValue=$($reEv.hwnd -eq $hwndHex)"

    # 10. 最终移动 + 关闭 → cleared（仅当重开窗口归属已验证才允许操作）
    if ($hwnd2Owned) {
        [M14W]::SetForegroundWindow($hwnd2) | Out-Null
        [M14W]::SetWindowPos($hwnd2, [IntPtr]::Zero, 1600, 900, 800, 500, 0x0004) | Out-Null
        Raise-Top $hwnd2
        Start-Sleep -Seconds 2
        $wm = Get-LastEventMs
        [M14W]::SendMessage($hwnd2, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        $clFin = Wait-Event 'cleared' 12 $hwnd2Hex $wm
        Add-Check '最终关闭 → cleared' ($null -ne $clFin) "reason=$($clFin.reason)"
    }

    # 10b. F-03 收尾对照断言：不同 PID 诱饵窗口全程未被改标题、未被移动、未被关闭。
    #      诱饵在驱动启动后由独立 pwsh 进程创建、标题符合旧规则（'*- Notepad'），
    #      在初开与重开两个发现窗口期都存在且始终零写入；真正带标记的测试窗口
    #      已通过上面全部检查。
    if ($TargetKind -eq 'notepad') {
        if ($decoySeen) {
            $decoyAlive = [M14W2]::IsWindow($decoyHwnd)
            $decoyTitleAfter = Get-Title $decoyHwnd
            $decoyRectAfter = New-Object M14W+RECT
            [M14W]::GetWindowRect($decoyHwnd, [ref]$decoyRectAfter) | Out-Null
            $rectSame = ($decoyRectAfter.L -eq $decoyRectBefore.L -and $decoyRectAfter.T -eq $decoyRectBefore.T -and
                $decoyRectAfter.R -eq $decoyRectBefore.R -and $decoyRectAfter.B -eq $decoyRectBefore.B)
            $decoyOk = $decoyAlive -and ($decoyTitleAfter -eq $decoyTitle) -and $rectSame
            Add-Check '外部候选不被改标题/移动/关闭（不同 PID 诱饵）' $decoyOk "decoyPid=$decoyPid driverPid=$PID title='$decoyTitleAfter' titleUnchanged=$($decoyTitleAfter -eq $decoyTitle) rectUnchanged=$rectSame alive=$decoyAlive"
        } else {
            Add-Check '外部候选不被改标题/移动/关闭（不同 PID 诱饵）' $false 'decoy window did not appear - contrast missing'
        }
    }

    # 10c. F-03 Edge 收尾对照断言：其他轮真实 msedge 窗口（旧固定标题、不同 profile、
    #      驱动启动后出现）全程被只读跳过——不改标题、不移动、不关闭、绝不被选为
    #      目标（探针 select 与各事件句柄均不得等于它）；本轮标记窗口已通过上面
    #      全部检查（本轮窗口能被选中，对照见 select/重开检查）。
    if ($TargetKind -eq 'edge') {
        if ($otherSeen) {
            $otherAlive = [M14W2]::IsWindow($otherHwnd)
            $otherTitleAfter = Get-Title $otherHwnd
            $otherRectAfter = New-Object M14W+RECT
            [M14W]::GetWindowRect($otherHwnd, [ref]$otherRectAfter) | Out-Null
            $otherRectSame = ($otherRectAfter.L -eq $otherRectBefore.L -and $otherRectAfter.T -eq $otherRectBefore.T -and
                $otherRectAfter.R -eq $otherRectBefore.R -and $otherRectAfter.B -eq $otherRectBefore.B)
            $evAll = Read-Events
            $otherInEvents = @($evAll | Where-Object { $_.hwnd -eq (HwndHex $otherHwnd) })
            $otherNeverSelected = ($otherInEvents.Count -eq 0) -and ($sel.hwnd -ne (HwndHex $otherHwnd))
            $otherOk = $otherAlive -and ($otherTitleAfter -eq $otherTitle) -and $otherRectSame -and $otherNeverSelected
            Add-Check '其他轮窗口被跳过（零写入）本轮窗口被选中' $otherOk "otherPid=$otherPid driverPid=$PID title='$otherTitleAfter' titleUnchanged=$($otherTitleAfter -eq $otherTitle) rectUnchanged=$otherRectSame alive=$otherAlive neverSelected=$otherNeverSelected selectedHwnd=$($sel.hwnd)"
        } else {
            Add-Check '其他轮窗口被跳过（零写入）本轮窗口被选中' $false 'other-round contrast window did not appear - contrast missing'
        }
    }

    # 11. 等探针自然结束
    if (-not $probe.WaitForExit(($DurationSec + 40) * 1000)) { Stop-Process -Id $probe.Id -Force }
    Write-Host "[$TargetKind] probe exited code=$($probe.ExitCode)"
    $res = Get-Content $resultFile -Raw | ConvertFrom-Json
    Write-Host "[$TargetKind] probe result: exitCode=$($res.exitCode) msg=$($res.message)"
    $wr = Get-Content (Join-Path $scenarioDir 'window-perch-result.json') -Raw | ConvertFrom-Json
    Write-Host "[$TargetKind] summary: awareness=$($wr.awareness) monitors=$($wr.monitorCount) primaryDpi=$($wr.primaryDpi) select=$($wr.selectCount) reacquire=$($wr.reacquireCount) clear=$($wr.clearCount) update=$($wr.updateCount) occluded=$($wr.occludedCount) unoccluded=$($wr.unoccludedCount)"
    Add-Check '探针 exitCode=0' ($probe.ExitCode -eq 0 -and $res.exitCode -eq 0) "probe exit=$($probe.ExitCode) result exitCode=$($res.exitCode)"
}
catch {
    # 异常必须落入转录（否则静默丢失无法定位）
    Write-Host "[$TargetKind] FATAL: $($_.Exception.GetType().Name): $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
    Write-Host "[$TargetKind] FATAL position: $($_.InvocationInfo.PositionMessage)"
    throw
}
finally {
    if ($cover) { $cover.Dispose() }
    # 清理本驱动启动的诱饵进程（独立 pwsh，pid 精确，不触及其他进程）
    if ($decoyProc -and -not $decoyProc.HasExited) { Stop-Process -Id $decoyProc.Id -Force -ErrorAction SilentlyContinue }
    # 只清理命令行含本 run profile 路径的 msedge 进程（不按 pid 差集，避免误杀）
    $profilePath = Join-Path $runDir 'edge-profile'
    $edgeProcs = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profilePath) })
    foreach ($p in $edgeProcs) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    if ($edgeProcs.Count -gt 0) { Write-Host "[$TargetKind] cleaned $($edgeProcs.Count) msedge process(es) of our profile" }
    # 同样只清理命令行含本轮 other-round profile 路径的 msedge（edge F-03 收尾对照窗口）
    if ($TargetKind -eq 'edge') {
        $otherProfilePath = (Join-Path $runDir 'edge-profile-other') + '\'
        $otherEdgeProcs = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.CommandLine -and $_.CommandLine.Contains($otherProfilePath) })
        foreach ($p in $otherEdgeProcs) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
        if ($otherEdgeProcs.Count -gt 0) { Write-Host "[$TargetKind] cleaned $($otherEdgeProcs.Count) msedge process(es) of other-round profile" }
    }
    if (-not $probe.HasExited) { Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue }
    if ($script:checks.Count -eq 0) {
        Write-Host "[$TargetKind] ABORTED before any check - no checklist"
        Stop-Transcript | Out-Null
        exit 1
    }
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Set-Content (Join-Path $runDir 'm14-checklist.txt')
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $allPass = -not ($script:checks | Where-Object { $_.Result -eq 'FAIL' })
    Write-Host "[$TargetKind] checklist: $(if ($allPass) { 'ALL PASS' } else { 'HAS FAIL' }) -> $runDir"
    Stop-Transcript | Out-Null
    if (-not $allPass) { exit 1 }
}
