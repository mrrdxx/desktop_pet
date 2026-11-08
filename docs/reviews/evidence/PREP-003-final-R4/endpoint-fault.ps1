# PREP-003 性能基线采集脚本（R3 修正版）
# 按 TESTING M-08 采集空闲和动态性能数据
# 显式 PID 入口 + 目标 DLL 核对 + Stopwatch 计时 + 终点样本 + 退出码区分
# R3 修正：终点失败不标 COMPLETED；异常行统一 9 列字段；终点核对 StartTime
# 用法：.\Collect-Perf.ps1 -DurationSec 300 -Label "idle-r1" -TargetPid 65656 -ExpectedDll "DesktopPet.dll"
#       .\Collect-Perf.ps1 -DurationSec 120 -Label "dynamic-r1" -TargetPid 65656 -ExpectedDll "DesktopPet.dll"
param(
    [Parameter(Mandatory=$true)][int]$DurationSec,
    [Parameter(Mandatory=$true)][string]$Label,
    [Parameter(Mandatory=$true)][int]$TargetPid,
    [string]$ExpectedDll,
    [string]$OutDir = (Join-Path $PSScriptRoot "..\..\artifacts\perf-baseline")
)

# Use UTF8 with BOM for compatibility with Windows PowerShell 5.1
$utf8Bom = [System.Text.UTF8Encoding]::new($true)

$ErrorActionPreference = 'Stop'
$timestamp = Get-Date -Format 'yyyy-MM-dd_HHmmss'
$outFile = Join-Path $OutDir "${Label}_${timestamp}.csv"
$metaFile = Join-Path $OutDir "${Label}_${timestamp}_meta.json"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# Exit codes: 0=completed, 1=process not found, 2=identity mismatch,
#             3=process exited early, 4=insufficient samples
$exitCode = 0

# Verify target process exists and capture identity metadata
$proc = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
if ($null -eq $proc) {
    Write-Error "Process PID=$TargetPid not found. Start the app first."
    exit 1
}

# Capture process identity metadata
$procStartTime = $proc.StartTime
$procPath = $proc.Path
$procName = $proc.ProcessName
$cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$TargetPid").CommandLine

# Verify target DLL if specified
$dllMatched = $true
if ($ExpectedDll) {
    if ($cmdLine -and $cmdLine.Contains($ExpectedDll)) {
        Write-Host "Identity check: PID=$TargetPid command line contains '$ExpectedDll' - OK"
    } else {
        Write-Warning "Identity check: PID=$TargetPid command line does NOT contain '$ExpectedDll'"
        Write-Warning "  Command line: $cmdLine"
        $dllMatched = $false
        $exitCode = 2
    }
}

# Save run metadata
$logicalCores = (Get-CimInstance Win32_Processor).NumberOfLogicalProcessors
$meta = @{
    label = $Label
    targetPid = $TargetPid
    processName = $procName
    processStartTime = $procStartTime.ToString('O')
    processPath = $procPath
    commandLine = $cmdLine
    expectedDll = $ExpectedDll
    dllMatched = $dllMatched
    durationRequestedSec = $DurationSec
    collectionStartIso = (Get-Date).ToString('O')
    logicalCores = $logicalCores
    timer = 'System.Diagnostics.Stopwatch (high-resolution monotonic)'
    csvColumns = 'timestamp_iso,elapsed_sec,pid,cpu_ms_total,working_set_mb,private_memory_mb,handle_count,thread_count,status'
}
[IO.File]::WriteAllText($metaFile, ($meta | ConvertTo-Json -Depth 3), $utf8Bom)
Write-Host "Metadata: $metaFile"

# Use Stopwatch for elapsed time (monotonic, not wall clock)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$wallStart = Get-Date

# CSV header includes PID and status
"timestamp_iso,elapsed_sec,pid,cpu_ms_total,working_set_mb,private_memory_mb,handle_count,thread_count,status" | Out-File -FilePath $outFile -Encoding UTF8

Write-Host "Collecting perf data for PID=$TargetPid ($procName) for $DurationSec seconds..."
Write-Host "Output: $outFile"
Write-Host "Wall start: $wallStart | Process start: $procStartTime"

$sampleCount = 0
$processExited = $false
$sampleErrors = 0
$identityChanged = $false

# Record initial process start time for identity change detection
$initialStartTime = $procStartTime

while ($sw.Elapsed.TotalSeconds -lt $DurationSec) {
    try {
        $p = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
        if ($null -eq $p) {
            $exitWall = Get-Date
            $exitElapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
            Write-Host "PROCESS_EXITED at wall=$exitWall, elapsed=${exitElapsed}s"
            "$($exitWall.ToString('O')),$exitElapsed,$TargetPid,,,,,,PROCESS_EXITED" | Out-File -FilePath $outFile -Encoding UTF8 -Append
            $processExited = $true
            $exitCode = 3
            break
        }
        # Check identity change: if process start time differs, PID was reused
        if ($p.StartTime -ne $initialStartTime) {
            $now = Get-Date
            $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
            Write-Warning "IDENTITY_CHANGED: PID=$TargetPid start time changed from $($initialStartTime.ToString('O')) to $($p.StartTime.ToString('O'))"
            "$($now.ToString('O')),$elapsed,$TargetPid,,,,,,IDENTITY_CHANGED" | Out-File -FilePath $outFile -Encoding UTF8 -Append
            $identityChanged = $true
            $exitCode = 2
            break
        }
        $now = Get-Date
        $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
        $cpuMs = $p.TotalProcessorTime.TotalMilliseconds
        $wsMb = [math]::Round($p.WorkingSet64 / 1MB, 2)
        $privMb = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
        $handles = $p.HandleCount
        $threads = $p.Threads.Count
        "$($now.ToString('O')),$elapsed,$TargetPid,$([math]::Round($cpuMs,0)),$wsMb,$privMb,$handles,$threads,OK" | Out-File -FilePath $outFile -Encoding UTF8 -Append
        $sampleCount++
    } catch {
        $sampleErrors++
        $errElapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
        # Use structured status instead of raw exception message to avoid CSV corruption
        "$((Get-Date).ToString('O')),$errElapsed,$TargetPid,,,,,,SAMPLE_ERROR" | Out-File -FilePath $outFile -Encoding UTF8 -Append
        Write-Warning "Sample error at elapsed=${errElapsed}s: $_"
    }
    Start-Sleep -Milliseconds 1000
}

# Capture final endpoint sample if process still alive and loop completed normally
$endpointCaptured = $false
$endpointFailed = $false
if (-not $processExited -and -not $identityChanged) {
    try {
        throw "REVIEW_INJECTED_ENDPOINT_READ_FAILURE"
        $p = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
        if ($null -eq $p) {
            # Process disappeared between loop end and endpoint capture
            $now = Get-Date
            $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
            Write-Warning "Endpoint: process PID=$TargetPid not found at elapsed=${elapsed}s"
            "$($now.ToString('O')),$elapsed,$TargetPid,,,,,,PROCESS_EXITED" | Out-File -FilePath $outFile -Encoding UTF8 -Append
            $processExited = $true
            $exitCode = 3
            $endpointFailed = $true
        } elseif ($p.StartTime -ne $initialStartTime) {
            # Identity changed between loop end and endpoint capture
            $now = Get-Date
            $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
            Write-Warning "Endpoint: IDENTITY_CHANGED at elapsed=${elapsed}s"
            "$($now.ToString('O')),$elapsed,$TargetPid,,,,,,IDENTITY_CHANGED" | Out-File -FilePath $outFile -Encoding UTF8 -Append
            $identityChanged = $true
            $exitCode = 2
            $endpointFailed = $true
        } else {
            $now = Get-Date
            $elapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
            $cpuMs = $p.TotalProcessorTime.TotalMilliseconds
            $wsMb = [math]::Round($p.WorkingSet64 / 1MB, 2)
            $privMb = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
            $handles = $p.HandleCount
            $threads = $p.Threads.Count
            "$($now.ToString('O')),$elapsed,$TargetPid,$([math]::Round($cpuMs,0)),$wsMb,$privMb,$handles,$threads,ENDPOINT" | Out-File -FilePath $outFile -Encoding UTF8 -Append
            $sampleCount++
            $endpointCaptured = $true
            Write-Host "Endpoint sample captured at elapsed=${elapsed}s"
        }
    } catch {
        Write-Warning "Failed to capture endpoint sample: $_"
        $errElapsed = [math]::Round($sw.Elapsed.TotalSeconds, 3)
        "$((Get-Date).ToString('O')),$errElapsed,$TargetPid,,,,,,SAMPLE_ERROR" | Out-File -FilePath $outFile -Encoding UTF8 -Append
        $sampleErrors++
        $endpointFailed = $true
    }
}

$sw.Stop()
$actualDuration = $sw.Elapsed.TotalSeconds
$wallEnd = Get-Date

# Determine final exit code
# COMPLETED requires: endpoint captured, no identity change, no process exit,
#                     no unhandled sample errors, sufficient samples
if ($exitCode -eq 0) {
    if (-not $endpointCaptured) {
        # Endpoint not captured (process exited, identity changed, or capture failed)
        $exitCode = if ($endpointFailed) { 3 } else { 4 }
    } elseif ($sampleErrors -gt 0) {
        # Unhandled sample errors during collection
        $exitCode = 4
    } elseif ($sampleCount -lt 2) {
        $exitCode = 4
    }
    # else: exitCode stays 0 (COMPLETED)
}

# Update metadata with actual results
$meta.collectionEndIso = $wallEnd.ToString('O')
$meta.actualDurationSec = [math]::Round($actualDuration, 3)
$meta.sampleCount = $sampleCount
$meta.sampleErrors = $sampleErrors
$meta.processExitedDuringCollection = $processExited
$meta.identityChanged = $identityChanged
$meta.endpointCaptured = $endpointCaptured
$meta.endpointFailed = $endpointFailed
$meta.exitCode = $exitCode
$meta.exitCodeMeaning = switch ($exitCode) {
    0 { 'COMPLETED' }
    1 { 'PROCESS_NOT_FOUND' }
    2 { 'IDENTITY_MISMATCH' }
    3 { 'PROCESS_EXITED_EARLY' }
    4 { 'INSUFFICIENT_SAMPLES' }
}
[IO.File]::WriteAllText($metaFile, ($meta | ConvertTo-Json -Depth 3), $utf8Bom)

Write-Host "Done. Samples: $sampleCount | Errors: $sampleErrors | Monotonic duration: $([math]::Round($actualDuration,3))s | Exited: $processExited | ExitCode: $exitCode"
Write-Host "Output: $outFile"

# Summary - use actual first/last valid sample interval, not script total duration
$samples = Import-Csv -Path $outFile | Where-Object { $_.cpu_ms_total -match '^\d+$' }
if ($samples.Count -gt 1) {
    $first = $samples[0]
    $last = $samples[$samples.Count - 1]
    $cpuDelta = [double]$last.cpu_ms_total - [double]$first.cpu_ms_total
    $elapsedDelta = [double]$last.elapsed_sec - [double]$first.elapsed_sec
    $cpuPercentSingle = if ($elapsedDelta -gt 0) { ($cpuDelta / ($elapsedDelta * 1000)) * 100 } else { 0 }
    $cpuPercentNormalized = if ($logicalCores -gt 0) { $cpuPercentSingle / $logicalCores } else { 0 }
    $wsFirst = [double]$first.working_set_mb
    $wsLast = [double]$last.working_set_mb
    $privFirst = [double]$first.private_memory_mb
    $privLast = [double]$last.private_memory_mb
    $handleFirst = [int]$first.handle_count
    $handleLast = [int]$last.handle_count
    $threadFirst = [int]$first.thread_count
    $threadLast = [int]$last.thread_count

    $summary = @"
=== PREP-003 性能基线摘要: $Label ===
PID: $TargetPid
采样数: $($samples.Count)
首末有效区间: $([math]::Round($elapsedDelta,3)) 秒 (首 elapsed=$([math]::Round([double]$first.elapsed_sec,3))s, 末 elapsed=$([math]::Round([double]$last.elapsed_sec,3))s)
脚本总耗时: $([math]::Round($actualDuration,3)) 秒
CPU 累计差: $([math]::Round($cpuDelta,0)) ms
单核 CPU 占比: $([math]::Round($cpuPercentSingle,2))%
逻辑核数: $logicalCores
归一化 CPU 占比 (单核%/逻辑核数): $([math]::Round($cpuPercentNormalized,4))%
公式: cpuPercent = (cpuDelta_ms / (elapsedDelta_s * 1000)) * 100; normalized = cpuPercent / logicalCores
工作集: 首样本 $wsFirst MB -> 末样本 $wsLast MB (差 $([math]::Round($wsLast - $wsFirst,2)) MB)
私有内存: 首样本 $privFirst MB -> 末样本 $privLast MB (差 $([math]::Round($privLast - $privFirst,2)) MB)
句柄: 首样本 $handleFirst -> 末样本 $handleLast (差 $($handleLast - $handleFirst))
线程: 首样本 $threadFirst -> 末样本 $threadLast (差 $($threadLast - $threadFirst))
进程退出: $processExited
身份变更: $identityChanged
终点样本: $endpointCaptured
采样错误: $sampleErrors
退出码: $exitCode ($($meta.exitCodeMeaning))
"@
    Write-Host $summary
    $summaryFile = Join-Path $OutDir "${Label}_${timestamp}_summary.txt"
    [IO.File]::WriteAllText($summaryFile, $summary, $utf8Bom)
    Write-Host "Summary: $summaryFile"
} else {
    Write-Host "Insufficient valid samples for summary (count=$($samples.Count))"
    $summaryFile = Join-Path $OutDir "${Label}_${timestamp}_summary.txt"
    $noData = "Insufficient valid samples (count=$($samples.Count)), exitCode=$exitCode"
    [IO.File]::WriteAllText($summaryFile, $noData, $utf8Bom)
}

exit $exitCode
