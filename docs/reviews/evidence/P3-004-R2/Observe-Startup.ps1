param([string]$Suite, [string]$Configuration)
$ErrorActionPreference='Stop'
$isolatedApp=Join-Path $Suite 'isolated-app'
$source=Join-Path $isolatedApp 'App.xaml.cs'
$s=Get-Content $source -Raw
$anchor='        if (stateUnknown || settingsUnknown)'
if(([regex]::Matches($s,[regex]::Escape($anchor))).Count -ne 1){throw 'startup anchor not found'}
$observe=@'
        var proof = string.Join(Environment.NewLine, _petWindows.Select(kv =>
        {
            var w = kv.Value;
            var snapshot = w.BuildPetSocialState(kv.Key, 0);
            return FormattableString.Invariant($"{kv.Key} Left={w.Left:F2} Top={w.Top:F2} Bottom={snapshot.IsOnBottomEdge} Monitor={snapshot.MonitorId} Visible={w.IsVisible}");
        }));
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR")!, "review-startup.txt"), proof);
        _ = Dispatcher.BeginInvoke(new Action(ShutdownMainWindow));

'@
Set-Content $source ($s.Replace($anchor, $observe+[Environment]::NewLine+$anchor))
dotnet build (Join-Path $isolatedApp 'DesktopPet.App.csproj') -c $Configuration --no-restore --nologo -v minimal
if($LASTEXITCODE -ne 0){throw 'build failed'}
$exe=(Resolve-Path (Join-Path $isolatedApp "bin/$Configuration/net10.0-windows/DesktopPet.exe")).Path
$start=[Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute=$false
$start.Environment['DESKTOPPET_VALIDATION_DIR']=(Resolve-Path (Join-Path $Suite 'data')).Path
$start.Environment['DESKTOPPET_PROBE_MODE']='interactive'
$fixtureProcess=[Diagnostics.Process]::Start($start)
if(-not $fixtureProcess.WaitForExit(15000)){ $fixtureProcess.Kill(); throw 'isolated fixture timed out'}
"PROCESS_EXIT_CODE=$($fixtureProcess.ExitCode)"
Get-Content (Join-Path $Suite 'data/review-startup.txt')
