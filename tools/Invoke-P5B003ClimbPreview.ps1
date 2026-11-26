# P5B-003: 攀爬动作隔离预览驱动（第一阶段：素材/注册/姿态观感）。
# 证据写入 .temp\p5b-003\run-<时间戳>\。
#
# 探针 climb-preview 场景（无目标窗口、无命令文件、无鼠标注入）：
#   1. TestSetupIsolatedSave + CreatePetWindow（生产路径）建真实宠物窗口。
#   2. 顺序播放 climb_side(loop, 9×100ms) → climb_edge(loop, 9×100ms) →
#      climb_land(once, 15×200ms)，每帧先走生产 UpdateFrame 再
#      RenderTargetBitmap(PetImage) 渲染 120×120 PNG（frames/*.png，
#      含 idle_base 基准）；land 末帧为 idle_0 站稳（注册 2400ms）。
#   3. 成功判定：探针 exitCode=0 且 probe-result.json{exitCode:0}。
#      渲染级脚点断言（非 Offset=0）交给 Verify-P5B003RenderPreview.py。
# 本阶段不接线 PlayClimbAction/EndClimbAction，不改路线/位置/中断规则。
#
# 用法（仓库根目录，pwsh 7 -File 独立进程运行）：
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-P5B003ClimbPreview.ps1
#   python tools\Verify-P5B003RenderPreview.py ^
#     .temp\p5b-003\run-<ts>\climb-preview\frames .temp\p5b-003\run-<ts>\deliverables

param(
    [string]$ProbePath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $repo ".temp\p5b-003\run-$ts"
$scenarioDir = Join-Path $runDir 'climb-preview'
New-Item -ItemType Directory -Force -Path $scenarioDir | Out-Null
$resultFile = Join-Path $runDir 'probe-result.json'
$probeExe = if ($ProbePath) { (Resolve-Path -LiteralPath $ProbePath).Path } else { Join-Path $repo 'tests\DesktopPet.DiagnosticProbe\bin\Debug\net10.0-windows\DesktopPet.DiagnosticProbe.exe' }
Start-Transcript -Path (Join-Path $runDir 'driver-transcript.log') | Out-Null

Write-Host "[P5B003] run dir: $runDir"
Write-Host "[P5B003] probe exe: $probeExe"

$script:probe = $null
$script:checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$name, [bool]$pass, [string]$detail) {
    $script:checks.Add([pscustomobject]@{ Item = $name; Result = $(if ($pass) { 'PASS' } else { 'FAIL' }); Detail = $detail })
    Write-Host ("[{0}] {1} : {2}" -f $(if ($pass) { 'PASS' } else { 'FAIL' }), $name, $detail)
}

try {
    # 1. 启动探针 climb-preview 场景
    $script:probe = Start-Process -FilePath $probeExe -ArgumentList "`"$runDir`"", 'climb-preview', "`"$resultFile`"" -PassThru -WindowStyle Hidden
    Write-Host "[P5B003] probe started pid=$($script:probe.Id)"

    # 2. 等汇总产物出现（渲染 34 帧，预计数秒内）
    $summaryFile = Join-Path $scenarioDir 'climb-preview-summary.json'
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline -and -not (Test-Path $summaryFile)) { Start-Sleep -Milliseconds 300 }
    Add-Check '① 探针产出 climb-preview-summary.json' (Test-Path $summaryFile) $summaryFile

    # 3. 等探针正常退出
    if (-not $script:probe.WaitForExit(60 * 1000)) { throw 'probe did not exit within 60s' }
    Write-Host "[P5B003] probe exited code=$($script:probe.ExitCode)"

    # 4. 断言：探针 exitCode=0 + probe-result.json exitCode=0
    $res = $(if (Test-Path $resultFile) { Get-Content $resultFile -Raw | ConvertFrom-Json } else { $null })
    Add-Check '② 探针 exitCode=0' ($script:probe.ExitCode -eq 0 -and $null -ne $res -and $res.exitCode -eq 0) "probe exit=$($script:probe.ExitCode) result exitCode=$($res.exitCode)"

    # 5. 帧产物齐全：idle_base + climb_side 9 + climb_edge 9 + climb_land 15 = 34
    $framesDir = Join-Path $scenarioDir 'frames'
    $frames = @(Get-ChildItem $framesDir -Filter '*.png' -ErrorAction SilentlyContinue)
    $idleOk = Test-Path (Join-Path $framesDir 'idle_base.png')
    $sideCount = @($frames | Where-Object Name -like 'climb_side_*.png').Count
    $edgeCount = @($frames | Where-Object Name -like 'climb_edge_*.png').Count
    $landCount = @($frames | Where-Object Name -like 'climb_land_*.png').Count
    Add-Check '③ 帧产物齐全（idle_base+side 9+edge 9+land 15）' ($idleOk -and $sideCount -eq 9 -and $edgeCount -eq 9 -and $landCount -eq 15) "frames=$($frames.Count) idle=$idleOk side=$sideCount edge=$edgeCount land=$landCount"
}
catch {
    Write-Host "[P5B003] FATAL: $($_.Exception.GetType().Name): $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
    throw
}
finally {
    if ($script:probe -and -not $script:probe.HasExited) { Stop-Process -Id $script:probe.Id -Force -ErrorAction SilentlyContinue }
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Set-Content (Join-Path $runDir 'p5b003-checklist.txt')
    $script:checks | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
    $allPass = -not ($script:checks | Where-Object { $_.Result -eq 'FAIL' })
    Write-Host "[P5B003] checklist: $(if ($allPass) { 'ALL PASS' } else { 'HAS FAIL' }) -> $runDir"
    Stop-Transcript | Out-Null
    if (-not $allPass) { exit 1 }
}