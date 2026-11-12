$ErrorActionPreference='Stop'
$source='artifacts/perf-baseline/run-bb7ac89c/isolated-app'
$app=Join-Path $PSScriptRoot 'control-app'
if(Test-Path $app){throw 'control copy already exists'}
New-Item -ItemType Directory $app | Out-Null
Get-ChildItem -LiteralPath $source | Where-Object Name -NotIn 'bin','obj' | Copy-Item -Destination $app -Recurse
$path=Join-Path $app 'App.xaml.cs'
$s=Get-Content -LiteralPath $path -Raw
$needle='        reviewTimer.Start();'
$ui=@'
        var reviewButtons = new System.Windows.Controls.StackPanel
        {
            Margin = new Thickness(12),
            Orientation = System.Windows.Controls.Orientation.Horizontal,
        };
        void AddReviewButton(string label, Action action)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = label, Width = 75, Height = 38, Margin = new Thickness(3),
            };
            button.Click += (_, _) =>
            {
                File.AppendAllText(reviewLog,
                    $"INPUT {DateTimeOffset.Now:O} ms={reviewClock.ElapsedMilliseconds} {label} prior={SocialCoordinator!.CurrentSession.Phase}{Environment.NewLine}");
                action();
                ReviewTick();
            };
            reviewButtons.Children.Add(button);
        }
        AddReviewButton("Pause", () => OnPauseToggled(null, true));
        AddReviewButton("Resume", () => OnPauseToggled(null, false));
        AddReviewButton("Exit", ShutdownMainWindow);
        var reviewControls = new Window
        {
            Title = "P3-004 isolated controls", Width = 280, Height = 110,
            Left = 1100, Top = 100, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = reviewButtons,
        };
        reviewControls.Show();
'@
Set-Content $path ($s.Replace($needle,$needle+[Environment]::NewLine+$ui))
dotnet build (Join-Path $app 'DesktopPet.App.csproj') -c Release --nologo -v minimal
if($LASTEXITCODE -ne 0){throw 'control fixture build failed'}
Set-Content (Join-Path $PSScriptRoot 'binary-control.txt') (Join-Path $app 'bin/Release/net10.0-windows/DesktopPet.dll')
