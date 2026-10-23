$ErrorActionPreference='Stop'
[Reflection.Assembly]::LoadFrom((Join-Path (Get-Location).Path 'src/DesktopPet.Core/bin/Debug/net10.0/DesktopPet.Core.dll')) | Out-Null
$json=Get-Content -LiteralPath assets/pets/default/character.json -Raw
$character=[DesktopPet.Core.Animation.CharacterLoader]::Load($json)
$script:timeMs=0.0
$clock=[Func[double]]{return $script:timeMs}
$player=[DesktopPet.Core.Animation.AnimationPlayer]::new($character,$clock)
$script:completed=0
$player.add_ActionCompleted([Action[string]]{param($id) $script:completed++})
$player.PlayAction('dodge')
$script:timeMs=1000.0
$null=$player.GetCurrentFrame()
"ONCE_SINGLE_FRAME: completed=$($player.IsCompleted); completionEvents=$script:completed"
$script:timeMs=0.0
$player.PlayAction('idle')
$script:timeMs=750.0
$before=$player.GetCurrentFrame().Item2.ImagePath
$player.Pause()
$script:timeMs=950.0
$paused=$player.GetCurrentFrame().Item2.ImagePath
$player.Resume()
$after=$player.GetCurrentFrame().Item2.ImagePath
"NORMAL_PAUSE: before=$before; paused=$paused; resumed=$after"
$player.Pause()
$player.PlayAction('drag')
"PAUSED_ACTION_CHANGE: pausedAfterPlay=$($player.IsPaused); expected lifecycle pause retained"
$baseName=Split-Path -Leaf (Get-Location).Path
$escape=$json.Replace('idle_0.png',"../$baseName-sibling/outside.png")
$loaded=[DesktopPet.Core.Animation.CharacterLoader]::Load($escape)
"SIBLING_PREFIX_PATH_ACCEPTED: $($loaded.Actions['idle'].Frames[0].ImagePath)"
