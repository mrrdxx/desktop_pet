$root=$PSScriptRoot
$samples=foreach($d in Get-ChildItem -LiteralPath $root -Directory){
    $csv=Get-ChildItem -LiteralPath $d.FullName -Filter '*.csv' | Select-Object -First 1
    if(!$csv){continue}
    $r=@(Import-Csv -LiteralPath $csv.FullName | Where-Object {$_.status -in 'OK','ENDPOINT'})
    if($r.Count -lt 2){continue}
    $first=$r[0]
    $last=$r[-1]
    $duration=[double]$last.elapsed_sec-[double]$first.elapsed_sec
    $cpu=([double]$last.cpu_ms_total-[double]$first.cpu_ms_total)/($duration*10)
    $envInfo=Get-Content (Join-Path $root 'environment.json') -Raw | ConvertFrom-Json
    $phase=@(Get-Content (Join-Path $d.FullName 'motion.log') | Where-Object {$_ -match 'phase=Chasing ' -and [DateTimeOffset]::Parse(($_ -split ' ')[0]) -ge [DateTimeOffset]::Parse($first.timestamp_iso) -and [DateTimeOffset]::Parse(($_ -split ' ')[0]) -le [DateTimeOffset]::Parse($last.timestamp_iso)})
    $m=@(Get-Content (Join-Path $d.FullName 'motion.log') | Where-Object {$_ -match 'drag=True|physics=True'})
    $normal=Select-String -LiteralPath (Join-Path $d.FullName 'driver.log') -Pattern 'EXIT .* CODE=0' -Quiet
    [pscustomobject]@{
        label=$d.Name
        completed=($last.status -eq 'ENDPOINT' -and $normal)
        elapsed_s=[math]::Round($duration,3)
        sample_count=$r.Count
        cpu_single_pct=[math]::Round($cpu,3)
        cpu_normalized_pct=[math]::Round($cpu/[double]$envInfo.logical,4)
        working_set_mean_mb=[math]::Round(($r.working_set_mb | Measure-Object -Average).Average,2)
        private_mean_mb=[math]::Round(($r.private_memory_mb | Measure-Object -Average).Average,2)
        handles_mean=[math]::Round(($r.handle_count | Measure-Object -Average).Average,2)
        threads_mean=[math]::Round(($r.thread_count | Measure-Object -Average).Average,2)
        chasing_log_rows=$phase.Count
        drag_or_physics_log_rows=$m.Count
    }
}
$samples | Export-Csv (Join-Path $root 'summary.csv') -NoTypeInformation -Encoding utf8
$samples | Format-Table -AutoSize
