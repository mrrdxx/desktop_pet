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
$result=$player.GetCurrentFrame()
"ONCE_SINGLE_FRAME: image=$($result.Item2.ImagePath); completed=$($player.IsCompleted); completionEvents=$script:completed; elapsed=1000ms; duration=600ms"
$script:timeMs=0.0
$player.PlayAction('idle')
$script:timeMs=750.0
$before=$player.GetCurrentFrame().Item2.ImagePath
# App stops only polling; its Environment.TickCount64 clock continues advancing.
$script:timeMs=950.0
$after=$player.GetCurrentFrame().Item2.ImagePath
"POLLING_PAUSE_WITH_LIVE_CLOCK: before=$before; after=$after; pausedWallTime=200ms; expected same phase until resume advances active time"
$escape=$json.Replace('idle_0.png','../outside.png')
$loaded=[DesktopPet.Core.Animation.CharacterLoader]::Load($escape)
"PARENT_PATH_ACCEPTED: path=$($loaded.Actions['idle'].Frames[0].ImagePath)"
$bad=$json.Replace('"frameWidth": 120','"frameWidth": -40').Replace('"frameHeight": 120','"frameHeight": -80').Replace('"footAnchorX": 60','"footAnchorX": -1').Replace('"footAnchorY": 110','"footAnchorY": -1')
$invalid=[DesktopPet.Core.Animation.CharacterLoader]::Load($bad)
"INVALID_METADATA_ACCEPTED: normalized=$($invalid.FrameWidth)x$($invalid.FrameHeight); anchor=($($invalid.FootAnchorX),$($invalid.FootAnchorY))"
$script:missing=0
$player.add_OnActionMissing([Action[string]]{param($id) $script:missing++})
$player.PlayAction('not-defined')
$player.PlayAction('not-defined')
"MISSING_ACTION_DIAGNOSTICS: sameIdRequests=2; events=$script:missing"
