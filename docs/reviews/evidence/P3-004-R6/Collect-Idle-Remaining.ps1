$ErrorActionPreference='Stop'
$first=Join-Path $PSScriptRoot 'default-idle-dual-1/driver.log'
$deadline=[DateTimeOffset]::Now.AddMinutes(7)
while(-not (Select-String -LiteralPath $first -Pattern 'EXIT .* CODE=0' -Quiet)){
    if([DateTimeOffset]::Now -gt $deadline){throw 'first sample did not exit cleanly'}
    if(Select-String -LiteralPath $first -Pattern 'COLLECT_EXIT=[1-9]|NORMAL_EXIT_TIMEOUT' -Quiet){throw 'first sample failed'}
    Start-Sleep -Seconds 3
}
$runner=Join-Path $PSScriptRoot 'Run-Sample.ps1'
& $runner -Label idle-single-1 -Pets 1 -Duration 300
& $runner -Label idle-dual-2 -Pets 2 -Duration 300
& $runner -Label idle-single-2 -Pets 1 -Duration 300
