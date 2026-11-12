param(
    [Parameter(Mandatory)][string]$Label,
    [ValidateSet(1,2)][int]$Pets=2,
    [int]$Duration=0,
    [switch]$ForceChase,
    [switch]$Restart,
    [switch]$FullMotion,
    [switch]$Control
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path $PSScriptRoot).Path
$binary=(Get-Content (Join-Path $root $(if($Control){'binary-control.txt'}else{'binary.txt'})) -Raw).Trim()
$data=Join-Path $root $Label
if($Restart){
    if(!(Test-Path (Join-Path $data 'state.json'))){throw 'restart needs existing archive'}
} else {
    if(Test-Path $data){throw 'sample label already exists'}
    New-Item -ItemType Directory $data | Out-Null
    Copy-Item (Join-Path $root "seed-$Pets.json") (Join-Path $data 'state.json')
}
$stop=Join-Path $data 'stop.request'
if(Test-Path -LiteralPath $stop){Remove-Item -LiteralPath $stop}
$start=[Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
$start.UseShellExecute=$false
$start.ArgumentList.Add($binary)
$start.Environment['DESKTOPPET_VALIDATION_DIR']=$data
$start.Environment['PET_M10_FORCE']=if($ForceChase){'1'}else{'0'}
$start.Environment['PET_M10_FULL']=if($FullMotion){'1'}else{'0'}
$fixture=[Diagnostics.Process]::Start($start)
$fixture.Id | Set-Content (Join-Path $data 'pid.txt')
"START $([DateTimeOffset]::Now.ToString('O')) PID=$($fixture.Id) LABEL=$Label" | Add-Content (Join-Path $data 'driver.log')
try {
    if($Duration -gt 0){
        Start-Sleep -Seconds 12
        if($fixture.HasExited){throw "fixture exited early: $($fixture.ExitCode)"}
        $hostPath=(Get-Process -Id $PID).Path
        & $hostPath -NoProfile -File tests/Integration/Collect-Perf.ps1 -DurationSec $Duration -Label $Label -TargetPid $fixture.Id -ExpectedDll $binary -OutDir $data
        $result=$LASTEXITCODE
        "COLLECT_EXIT=$result" | Add-Content (Join-Path $data 'driver.log')
        if($result -ne 0){throw "collector exited $result"}
    }else{
        if(!$fixture.WaitForExit(600000)){throw 'interactive fixture timed out'}
    }
} finally {
    if(!$fixture.HasExited){
        'normal shutdown' | Set-Content $stop
        if(!$fixture.WaitForExit(12000)){ $fixture.Kill(); $fixture.WaitForExit(); 'NORMAL_EXIT_TIMEOUT' | Add-Content (Join-Path $data 'driver.log') }
    }
    "EXIT $([DateTimeOffset]::Now.ToString('O')) PID=$($fixture.Id) CODE=$($fixture.ExitCode)" | Add-Content (Join-Path $data 'driver.log')
}
