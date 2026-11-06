<#
.SYNOPSIS
  F-11 R13 negative control tests — verify the driver's final PASS judgment
  correctly rejects timeout, non-zero exit, and missing completion marker.
  Does NOT start, kill, or interact with any real process.
  Only tests the judgment logic with synthetic log content.
#>
param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$negRoot = Join-Path $repositoryRoot "artifacts/probe-live/negative-controls"
New-Item -ItemType Directory -Path $negRoot -Force | Out-Null

# Replicate the judgment logic from Run-ProbeLive.ps1 (auto mode final check).
function Invoke-Judgment {
    param(
        [bool]$TimeoutOccurred,
        [int]$ProcessExitCode,
        [string]$LogContent,   # can be $null for "no log"
        [string]$LogPath
    )

    # Write synthetic log if content provided.
    if ($null -ne $LogContent) {
        Set-Content -LiteralPath $LogPath -Value $LogContent -Encoding UTF8
    } elseif (Test-Path -LiteralPath $LogPath) {
        Remove-Item -LiteralPath $LogPath -Force
    }

    $judgmentErrors = @()

    # Condition 1: No timeout.
    if ($TimeoutOccurred) {
        $judgmentErrors += 'FAIL timeout occurred (process was killed after timeout)'
    }

    # Condition 2: Process exit code is 0.
    if ($ProcessExitCode -ne 0) {
        $judgmentErrors += "FAIL process exit code is $ProcessExitCode (expected 0)"
    }

    # Condition 3: Log exists and has zero FAIL/ERROR lines.
    if (-not (Test-Path -LiteralPath $LogPath)) {
        $judgmentErrors += 'FAIL live.log not found after run'
    } else {
        $failCount = @(Select-String -LiteralPath $LogPath -Pattern ' FAIL | ERROR ').Count
        if ($failCount -gt 0) {
            $judgmentErrors += "FAIL log contains $failCount FAIL/ERROR line(s)"
        }

        # Condition 4: All required markers present.
        $requiredMarkers = @('BASELINE', 'PROBE_ACTIVE', 'PROBE_ENDED', 'MONITOR', 'HWND_DPI', 'ARCHIVE_SNAPSHOT_AFTER')
        foreach ($marker in $requiredMarkers) {
            if (-not (Select-String -LiteralPath $LogPath -Pattern $marker -Quiet)) {
                $judgmentErrors += "FAIL missing required log marker: $marker"
            }
        }

        # Condition 5: RESULT failures=0 present.
        if (-not (Select-String -LiteralPath $LogPath -Pattern 'RESULT failures=0' -Quiet)) {
            $judgmentErrors += 'FAIL missing RESULT failures=0 completion marker'
        }
    }

    return $judgmentErrors
}

# ── Helper: generate a valid log with all markers ──
function New-ValidLog {
    return @"
$(Get-Date -Format 'o') === PROBE_LIVE_START ===
$(Get-Date -Format 'o') PROBE_MODE=auto
$(Get-Date -Format 'o') HWND_DPI scale=(1.5000,1.5000)
$(Get-Date -Format 'o') MONITOR device=\\.\DISPLAY1 dpiScale=1.5000 full=(0,0,2560,1440,2560x1440) work=(0,0,2560,1384,2560x1384)
$(Get-Date -Format 'o') STATE[BASELINE] hwnd=12345 dpi=144 dpiScale=1.5000 windowDIP=(100.00,100.00) windowPhys=(150.00,150.00) windowSizePhys=(180.00,180.00) action=idle isProbing=False
$(Get-Date -Format 'o') BASELINE_TIME=$(Get-Date -Format 'o')
$(Get-Date -Format 'o') PASS all Test injection fields remain null
$(Get-Date -Format 'o') PROBE_START_DETECTED=$(Get-Date -Format 'o') IsProbing=True
$(Get-Date -Format 'o') PASS probe started within ~3s
$(Get-Date -Format 'o') STATE[PROBE_ACTIVE] hwnd=12345 dpi=144 dpiScale=1.5000 windowDIP=(100.00,-60.00) action=curious isProbing=True
$(Get-Date -Format 'o') PASS probe action is 'curious'
$(Get-Date -Format 'o') EDGE_DISTANCE top=-90.0px
$(Get-Date -Format 'o') NEAREST_EDGE=top
$(Get-Date -Format 'o') PASS window moved from baseline
$(Get-Date -Format 'o') PROBE_END_DETECTED=$(Get-Date -Format 'o') IsProbing=False
$(Get-Date -Format 'o') PASS probe ended within ~5s
$(Get-Date -Format 'o') STATE[PROBE_ENDED] hwnd=12345 dpi=144 action=idle isProbing=False
$(Get-Date -Format 'o') PASS window restored to baseline
$(Get-Date -Format 'o') PASS action returned to 'idle'
$(Get-Date -Format 'o') PROBE_DURATION=2.92s
$(Get-Date -Format 'o') PASS archive rareEventPlan.consumed=True
$(Get-Date -Format 'o') FINAL: checksFailures=0
$(Get-Date -Format 'o') ARCHIVE_SNAPSHOT_AFTER=state.json:abc123
$(Get-Date -Format 'o') EXIT_MODE=auto (tray PerformClick)
$(Get-Date -Format 'o') RESULT failures=0; pre-seeded archive, real timer/DPI/monitors, no Test injection
"@
}

$results = @()

# ── Negative Control 1: Timeout with all markers present ──
# R13 scenario: 6 markers present, but timeout occurred → must NOT PASS.
$logPath = Join-Path $negRoot 'nc1-timeout.log'
$errors1 = Invoke-Judgment -TimeoutOccurred $true -ProcessExitCode 1 -LogContent (New-ValidLog) -LogPath $logPath
$nc1Pass = $errors1.Count -gt 0 -and ($errors1 -join ' ').Contains('timeout')
$results += "NC1_TIMEOUT: $(if ($nc1Pass) {'REJECTED (correct)'} else {'WRONGLY PASSED (bug)'}) — $($errors1.Count) error(s)"

# ── Negative Control 2: Non-zero exit code with all markers ──
# Scenario: process exited with code 1, all markers present → must NOT PASS.
$logPath = Join-Path $negRoot 'nc2-nonzero-exit.log'
$errors2 = Invoke-Judgment -TimeoutOccurred $false -ProcessExitCode 1 -LogContent (New-ValidLog) -LogPath $logPath
$nc2Pass = $errors2.Count -gt 0 -and ($errors2 -join ' ').Contains('exit code is 1')
$results += "NC2_NONZERO_EXIT: $(if ($nc2Pass) {'REJECTED (correct)'} else {'WRONGLY PASSED (bug)'}) — $($errors2.Count) error(s)"

# ── Negative Control 3: Missing RESULT failures=0 completion marker ──
# Scenario: all 6 markers present, exit 0, no timeout, but RESULT line missing.
$logMissingResult = (New-ValidLog) -replace 'RESULT failures=0.*', 'RESULT failures=1; something went wrong'
$logPath = Join-Path $negRoot 'nc3-missing-result.log'
$errors3 = Invoke-Judgment -TimeoutOccurred $false -ProcessExitCode 0 -LogContent $logMissingResult -LogPath $logPath
$nc3Pass = $errors3.Count -gt 0 -and ($errors3 -join ' ').Contains('RESULT failures=0')
$results += "NC3_MISSING_RESULT: $(if ($nc3Pass) {'REJECTED (correct)'} else {'WRONGLY PASSED (bug)'}) — $($errors3.Count) error(s)"

# ── Negative Control 4: Tray exit failure (FAIL in log) ──
# Scenario: all markers present, exit 0, but log has FAIL line from tray exit failure.
$logWithFail = (New-ValidLog) -replace 'EXIT_MODE=auto \(tray PerformClick\)', 'FAIL auto tray exit failed: NullReferenceException'
$logWithFail = $logWithFail -replace 'RESULT failures=0.*', 'RESULT failures=1; tray exit failed'
$logPath = Join-Path $negRoot 'nc4-tray-exit-fail.log'
$errors4 = Invoke-Judgment -TimeoutOccurred $false -ProcessExitCode 0 -LogContent $logWithFail -LogPath $logPath
$nc4Pass = $errors4.Count -gt 0 -and (($errors4 -join ' ').Contains('FAIL') -or ($errors4 -join ' ').Contains('RESULT failures=0'))
$results += "NC4_TRAY_EXIT_FAIL: $(if ($nc4Pass) {'REJECTED (correct)'} else {'WRONGLY PASSED (bug)'}) — $($errors4.Count) error(s)"

# ── Negative Control 5: Missing required marker (PROBE_ENDED absent) ──
# Scenario: exit 0, no timeout, but PROBE_ENDED missing.
$logMissingMarker = (New-ValidLog) -replace 'STATE\[PROBE_ENDED\].*', ''
$logPath = Join-Path $negRoot 'nc5-missing-marker.log'
$errors5 = Invoke-Judgment -TimeoutOccurred $false -ProcessExitCode 0 -LogContent $logMissingMarker -LogPath $logPath
$nc5Pass = $errors5.Count -gt 0 -and ($errors5 -join ' ').Contains('PROBE_ENDED')
$results += "NC5_MISSING_MARKER: $(if ($nc5Pass) {'REJECTED (correct)'} else {'WRONGLY PASSED (bug)'}) — $($errors5.Count) error(s)"

# ── Positive Control: All conditions met → must PASS ──
$logPath = Join-Path $negRoot 'pc1-valid.log'
$errorsPC1 = Invoke-Judgment -TimeoutOccurred $false -ProcessExitCode 0 -LogContent (New-ValidLog) -LogPath $logPath
$pc1Pass = $errorsPC1.Count -eq 0
$results += "PC1_VALID: $(if ($pc1Pass) {'PASSED (correct)'} else {'WRONGLY REJECTED (bug)'}) — $($errorsPC1.Count) error(s)"

# ── Output results ──
""
"=== F-11 R13 NEGATIVE CONTROL RESULTS ==="
foreach ($r in $results) { $r }
""

$allCorrect = $nc1Pass -and $nc2Pass -and $nc3Pass -and $nc4Pass -and $nc5Pass -and $pc1Pass
if ($allCorrect) {
    "ALL NEGATIVE CONTROLS CORRECT — judgment logic properly rejects invalid runs"
    "NEGATIVE_CONTROL_PASS; $negRoot"
} else {
    "SOME NEGATIVE CONTROLS FAILED — judgment logic has bugs"
    throw "Negative control validation failed; see results above."
}
