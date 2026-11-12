$ErrorActionPreference='Stop'
$suite=(Resolve-Path 'artifacts/perf-baseline/run-bb7ac89c').Path
$out=(Resolve-Path 'artifacts/p3-004-m10').Path
$app=Join-Path $suite 'isolated-app'
$xaml=Join-Path $app 'MainWindow.xaml'
$x=Get-Content -LiteralPath $xaml -Raw
Set-Content -LiteralPath $xaml $x.Replace('ShowInTaskbar="False"','ShowInTaskbar="True"')
$path=Join-Path $app 'App.xaml.cs'
$s=Get-Content -LiteralPath $path -Raw
$config='        SocialCoordinator = new SocialCoordinator();'
$i=$s.LastIndexOf($config)
if($i -lt 0){throw 'missing startup configuration'}
$replacement=@'
        if (Environment.GetEnvironmentVariable("PET_M10_FORCE") == "1")
        {
            SocialCoordinator = new SocialCoordinator(new SocialConfig
            {
                NoticeProbability = 0,
                SlowPacedNoticeProbability = 0,
                ChaseAttemptIntervalMs = 1000,
                ChaseInviteProbability = 1,
                ChaseCooldownMs = 7000,
                RespondProbability = 1,
            });
        }
        else
            SocialCoordinator = new SocialCoordinator();
'@
$s=$s.Substring(0,$i)+$replacement+$s.Substring($i+$config.Length)
$anchor='        if (stateUnknown || settingsUnknown)'
if(([regex]::Matches($s,[regex]::Escape($anchor))).Count -ne 1){throw 'startup anchor changed'}
$observer=@'
        var reviewData = Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR")!;
        var reviewClock = System.Diagnostics.Stopwatch.StartNew();
        var reviewLog = Path.Combine(reviewData, "motion.log");
        var reviewStop = Path.Combine(reviewData, "stop.request");
        var reviewFull = Environment.GetEnvironmentVariable("PET_M10_FULL") == "1";
        File.AppendAllText(reviewLog, $"START {DateTimeOffset.Now:O} pid={Environment.ProcessId} force={Environment.GetEnvironmentVariable("PET_M10_FORCE")}{Environment.NewLine}");
        foreach (var pair in _petWindows)
        {
            var w = pair.Value;
            var reviewSnapshot = w.BuildPetSocialState(pair.Key, 0);
            var dpi = MonitorInterop.GetWindowDpiScale(new System.Windows.Interop.WindowInteropHelper(w).Handle);
            File.AppendAllText(reviewLog, $"WINDOW {pair.Key} bottom={reviewSnapshot.IsOnBottomEdge} monitor={reviewSnapshot.MonitorId} dpi={dpi} visible={w.IsVisible}{Environment.NewLine}");
        }
        string prior = "";
        var reviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(reviewFull ? 40 : 100) };
        void ReviewTick()
        {
            var session = SocialCoordinator!.CurrentSession;
            var status = $"phase={session.Phase} leader={session.ChaseLeaderId} direction={session.ChaseDirection} " +
                string.Join(" | ", _petWindows.Select(kv =>
                    FormattableString.Invariant($"{kv.Key}:drag={kv.Value.IsDragging} physics={kv.Value.IsPhysicsActive} paused={kv.Value.IsAppPaused}")));
            var positions = string.Join(" | ", _petWindows.Select(kv =>
                FormattableString.Invariant($"{kv.Key}:x={kv.Value.Left:F2},y={kv.Value.Top:F2}")));
            var key = reviewFull ? status + positions : status;
            if (key != prior)
            {
                prior = key;
                File.AppendAllText(reviewLog, $"{DateTimeOffset.Now:O} ms={reviewClock.ElapsedMilliseconds} {status} {positions}{Environment.NewLine}");
            }
            if (File.Exists(reviewStop))
            {
                reviewTimer.Stop();
                ShutdownMainWindow();
            }
        }
        ReviewTick();
        reviewTimer.Tick += (_, _) => ReviewTick();
        reviewTimer.Start();

'@
Set-Content -LiteralPath $path ($s.Replace($anchor,$observer+[Environment]::NewLine+$anchor))
$seed=Get-Content (Join-Path $suite 'data/state.json') -Raw | ConvertFrom-Json
$seed.accumulatedRuntimeMinutes=0
$seed.pets[1].personality=1
foreach($count in @(2,1)){
    $seed.pets=@($seed.pets | Select-Object -First $count)
    $seed | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $out "seed-$count.json")
}
dotnet build (Join-Path $app 'DesktopPet.App.csproj') -c Release --no-restore --nologo -v minimal
if($LASTEXITCODE -ne 0){throw 'fixture build failed'}
Set-Content (Join-Path $out 'binary.txt') (Join-Path $app 'bin/Release/net10.0-windows/DesktopPet.dll')

