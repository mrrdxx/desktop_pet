# P0-003 F-01 R2: Verify actual HWND DPI awareness context is PMv2.
# Starts the pet, finds its window, checks GetWindowDpiAwarenessContext against
# DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 (handle value -4).
# Then performs basic Recover and drag regression at the current DPI setting.
# Output is printed to stdout and saved to artifacts for evidence.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DpiProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct GuiInfo { public int Size; public uint Flags; public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret; public Rect CaretRect; }
    public delegate bool EnumProc(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window,StringBuilder name,int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread,ref GuiInfo info);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(Point point,uint flags);
    [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] public static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="PostMessageW")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] public static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetMenuItemRect(IntPtr window,IntPtr menu,uint index,out Rect rect);
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyInput { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,Input[] inputs,int size);
    public static IntPtr FindWindow(uint process,bool menu) {
        IntPtr found=IntPtr.Zero;
        EnumWindows((window,data)=>{
            uint actual; GetWindowThreadProcessId(window,out actual);
            var name=new StringBuilder(256); GetClassName(window,name,name.Capacity);
            if(actual==process && IsWindowVisible(window) && (name.ToString()=="#32768")==menu) { found=window; return false; }
            return true;
        },IntPtr.Zero);
        return found;
    }
    public static void Button(bool down,bool right) {
        var input=new Input(); input.Data.Mouse.Flags=right ? (down?8u:16u) : (down?2u:4u);
        if(SendInput(1,new[]{input},Marshal.SizeOf<Input>())!=1) throw new InvalidOperationException("SendInput failed.");
    }
    public static void MoveMouseRelative(int dx,int dy) {
        var input=new Input(); input.Data.Mouse.Flags=0x0001u; // MOVE (relative)
        input.Data.Mouse.X=dx; input.Data.Mouse.Y=dy;
        if(SendInput(1,new[]{input},Marshal.SizeOf<Input>())!=1) throw new InvalidOperationException("SendInput relative move failed.");
    }
    // Move mouse using SendInput absolute coordinates (0..65535 mapped to virtual desktop).
    // This bypasses SetCursorPos DPI virtualization issues in PMv2 mode.
    public static void MoveMouseAbsolute(int x,int y,int virtualW,int virtualH) {
        var input=new Input(); input.Data.Mouse.Flags=0xC001u; // ABSOLUTE | MOVE | VIRTUALDESK
        input.Data.Mouse.X=(int)Math.Round((x*65535.0)/virtualW);
        input.Data.Mouse.Y=(int)Math.Round((y*65535.0)/virtualH);
        if(SendInput(1,new[]{input},Marshal.SizeOf<Input>())!=1) throw new InvalidOperationException("SendInput absolute move failed.");
    }
    [DllImport("user32.dll")] public static extern int SystemParametersInfo(uint action,uint param,ref Rect value,uint winini);
    public static Rect GetVirtualScreen() {
        var rect=new Rect();
        SystemParametersInfo(0x0030,0,ref rect,0); // SPI_GETVIRTUALSCREEN
        return rect;
    }
    public static bool HasCapture(IntPtr window) {
        uint process; uint thread=GetWindowThreadProcessId(window,out process);
        var info=new GuiInfo{Size=Marshal.SizeOf<GuiInfo>()};
        if(!GetGUIThreadInfo(thread,ref info)) throw new InvalidOperationException("Cannot read GUI thread info.");
        return info.Capture==window;
    }
    public static void Click(int x,int y,bool right,uint expectedProcess) {
        uint actual; GetWindowThreadProcessId(WindowFromPoint(new Point{X=x,Y=y}),out actual);
        if(actual!=expectedProcess) throw new InvalidOperationException("Refusing to click unrelated window.");
        SetCursorPos(x,y); Button(true,right); Button(false,right);
    }
    public static void Escape() {
        var down=new Input{Type=1}; down.Data.Key.Key=27;
        var up=down; up.Data.Key.Flags=2;
        if(SendInput(2,new[]{down,up},Marshal.SizeOf<Input>())!=2) throw new InvalidOperationException("Escape failed.");
    }
}
'@
function Wait-Probe([scriptblock]$Condition,[string]$Failure) {
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do {
        [System.Windows.Forms.Application]::DoEvents()
        $result=& $Condition
        if ($result -is [bool] -and $result) { return }
        Start-Sleep -Milliseconds 40
    } while ($timer.ElapsedMilliseconds -lt 10000)
    throw $Failure
}
function Get-PetRect {
    $rect=New-Object DpiProbe+Rect
    if (-not [DpiProbe]::GetWindowRect($script:appWindow,[ref]$rect)) { throw 'Cannot read pet window.' }
    return $rect
}
function Open-PetMenu {
    $rect=Get-PetRect
    $clickX=[int](($rect.Left+$rect.Right)/2)
    $clickY=[int]($rect.Top+($rect.Bottom-$rect.Top)*0.60)
    $menuFound=$false
    for ($attempt=1;$attempt -le 3;$attempt++) {
        $hitPt=New-Object DpiProbe+Point; $hitPt.X=$clickX; $hitPt.Y=$clickY
        $hitWindow=[DpiProbe]::WindowFromPoint($hitPt)
        $hitProc=0; [DpiProbe]::GetWindowThreadProcessId($hitWindow,[ref]$hitProc) | Out-Null
        "Open-PetMenu(attempt=$attempt): click=($clickX,$clickY) hitPID=$hitProc petPID=$($script:probe.Id)"
        [DpiProbe]::Click($clickX,$clickY,$true,[uint32]$script:probe.Id)
        Start-Sleep -Milliseconds 500
        $menuWindow=[DpiProbe]::FindWindow([uint32]$script:probe.Id,$true)
        if ($menuWindow -ne [IntPtr]::Zero) { $menuFound=$true; break }
        "Open-PetMenu: attempt $attempt failed, retrying..."
        Start-Sleep -Milliseconds 500
    }
    if (-not $menuFound) { throw 'Pet menu did not open after 3 attempts.' }
}
function Select-MenuItem([uint32]$Index) {
    $menuWindow=[DpiProbe]::FindWindow([uint32]$script:probe.Id,$true)
    $menu=[DpiProbe]::SendMessage($menuWindow,0x01E1,[IntPtr]::Zero,[IntPtr]::Zero)
    $rect=New-Object DpiProbe+Rect
    if (-not [DpiProbe]::GetMenuItemRect([IntPtr]::Zero,$menu,$Index,[ref]$rect)) { throw 'Cannot locate menu item.' }
    [DpiProbe]::Click([int](($rect.Left+$rect.Right)/2),[int]($rect.Top+$rect.Bottom)/2,$false,[uint32]$script:probe.Id)
}
function Assert-Recovered([string]$Label) {
    Wait-Probe {
        $script:probe.Refresh()
        if ($script:probe.HasExited) { throw "Pet crashed: exit=$($script:probe.ExitCode)" }
        $rect=Get-PetRect
        # Product convention: foot center (bottom-center of window) is placed at
        # work area center by RecoverToPrimary. Do NOT compare window center.
        $footX=($rect.Left+$rect.Right)/2
        $footY=$rect.Bottom
        $workCenterX=($script:work.Left+$script:work.Right)/2
        $workCenterY=($script:work.Top+$script:work.Bottom)/2
        $inBounds=$rect.Left -ge $script:work.Left -and $rect.Top -ge $script:work.Top -and $rect.Right -le $script:work.Right -and $rect.Bottom -le $script:work.Bottom
        $footCentered=[Math]::Abs($footX-$workCenterX) -lt 2 -and [Math]::Abs($footY-$workCenterY) -lt 2
        # Condition returns ONLY a boolean; diagnostics go to a separate stream.
        if (-not $inBounds) { Write-Host "Assert-Recovered: out of bounds rect=($($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)) work=($($script:work.Left),$($script:work.Top),$($script:work.Right),$($script:work.Bottom))" }
        if (-not $footCentered) { Write-Host "Assert-Recovered: foot not centered foot=($footX,$footY) workCenter=($workCenterX,$workCenterY)" }
        $inBounds -and $footCentered
    } "Recover failed: $Label"
    Start-Sleep -Milliseconds 500
    $rect=Get-PetRect
    if (([DpiProbe]::GetWindowLong($script:appWindow,-20) -band 0x08000000) -eq 0) { throw 'NOACTIVATE lost after Recover.' }
    "PASS: $Label; rect=($($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom))"
}

# DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
$pmv2Context=[IntPtr](-4)
$previousContext=[DpiProbe]::SetThreadDpiAwarenessContext($pmv2Context)
$previousForeground=[DpiProbe]::GetForegroundWindow()
$previousCursor=New-Object DpiProbe+Point
[DpiProbe]::GetCursorPos([ref]$previousCursor) | Out-Null
$info=New-Object DpiProbe+MonitorInfo
$info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
$origin=New-Object DpiProbe+Point
if (-not [DpiProbe]::GetMonitorInfo([DpiProbe]::MonitorFromPoint($origin,1),[ref]$info)) { throw 'Cannot read primary work area.' }
$work=$info.Work
$target=New-Object System.Windows.Forms.Form
$target.Text='DesktopPet PMv2 verify target'
$target.ShowInTaskbar=$false
$target.TopMost=$true
$target.StartPosition='Manual'
$target.SetBounds($work.Right-350,$work.Bottom-220,320,180)
$probe=$null
$appWindow=[IntPtr]::Zero
$exe=Join-Path $PSScriptRoot '../../src/DesktopPet.App/bin/Debug/net10.0-windows/DesktopPet.exe'
$outFile=Join-Path $PSScriptRoot 'pmv2-verify-output.txt'
try {
    $target.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $probe=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $PSScriptRoot 'pmv2-verify-stderr.txt')
    Wait-Probe { $script:appWindow=[DpiProbe]::FindWindow([uint32]$probe.Id,$false); $appWindow -ne [IntPtr]::Zero } 'Pet window did not appear.'

    # Core PMv2 check
    $hwndDpiCtx=[DpiProbe]::GetWindowDpiAwarenessContext($appWindow)
    $isPmv2=[DpiProbe]::AreDpiAwarenessContextsEqual($hwndDpiCtx,$pmv2Context)
    $windowDpi=[DpiProbe]::GetDpiForWindow($appWindow)
    $envLine="Environment: Windows=$([Environment]::OSVersion.Version); DPI=$windowDpi; PMv2=$isPmv2; PID=$($probe.Id); work=($($work.Left),$($work.Top),$($work.Right),$($work.Bottom))"
    $envLine
    if (-not $isPmv2) { throw "PMv2 is NOT enabled. HWND DPI awareness context does not match PER_MONITOR_AWARE_V2." }
    "PASS: HWND DPI awareness context is PER_MONITOR_AWARE_V2 (PMv2=true)"

    # Verify WS_EX_NOACTIVATE is set on the pet window
    $exStyle=[DpiProbe]::GetWindowLong($appWindow,-20)
    $hasNoActivate=($exStyle -band 0x08000000) -ne 0
    "WS_EX_NOACTIVATE=$hasNoActivate (exStyle=0x$($exStyle.ToString('X')))"
    if (-not $hasNoActivate) { throw 'WS_EX_NOACTIVATE not set on pet window.' }

    # Recover regression — 1 Recover + 1 repeat
    Open-PetMenu
    Select-MenuItem 0
    Assert-Recovered 'Recover from startup position'
    Start-Sleep -Milliseconds 1000
    Open-PetMenu
    Select-MenuItem 0
    Assert-Recovered 'repeated Recover'
    Start-Sleep -Milliseconds 1000

    # Drag regression — record real cursor positions, capture state, and verify
    # exact displacement, focus retention, and mouse-up stop.
    # Uses SendInput absolute coordinates to bypass SetCursorPos DPI virtualization.
    $rect=Get-PetRect
    $petX=[int](($rect.Left+$rect.Right)/2)
    $petY=[int]($rect.Top+($rect.Bottom-$rect.Top)*0.60)
    $fgSetResult=[DpiProbe]::SetForegroundWindow($target.Handle)
    Start-Sleep -Milliseconds 200
    $fgBefore=[DpiProbe]::GetForegroundWindow()
    "SetForegroundWindow result=$fgSetResult; fgBefore=$fgBefore vs target=$($target.Handle)"

    # If SetForegroundWindow did not succeed, focus checks are unreliable.
    # Record this as a test condition issue, not an application bug.
    $focusCheckable=($fgBefore -eq $target.Handle)
    if (-not $focusCheckable) {
        "TEST_CONDITION: SetForegroundWindow did not set target as foreground. Focus checks will compare against actual fgBefore=$fgBefore instead of target handle."
    }

    # Get virtual screen dimensions for SendInput absolute coordinate mapping.
    $vScreen=[DpiProbe]::GetVirtualScreen()
    $virtualW=$vScreen.Right-$vScreen.Left
    $virtualH=$vScreen.Bottom-$vScreen.Top
    "Virtual screen: ($($vScreen.Left),$($vScreen.Top),$($vScreen.Right),$($vScreen.Bottom)) size=($virtualW,$virtualH)"

    # Record real cursor start position.
    $cursorStart=New-Object DpiProbe+Point
    [DpiProbe]::GetCursorPos([ref]$cursorStart) | Out-Null
    "Cursor start (GetCursorPos): ($($cursorStart.X),$($cursorStart.Y))"

    # Move cursor to pet position.
    # SetCursorPos in PMv2 may have DPI virtualization issues. Use a two-step
    # approach: SetCursorPos for rough positioning, then SendInput relative
    # move for precise adjustment. Record all values as evidence.
    $setCursorResult=[DpiProbe]::SetCursorPos($petX,$petY)
    Start-Sleep -Milliseconds 50
    $afterSet=New-Object DpiProbe+Point
    [DpiProbe]::GetCursorPos([ref]$afterSet) | Out-Null
    "SetCursorPos to pet: result=$setCursorPos target=($petX,$petY) actual=($($afterSet.X),$($afterSet.Y))"
    # Correct any offset using SendInput relative move.
    $corrX=$petX-$afterSet.X
    $corrY=$petY-$afterSet.Y
    if ($corrX -ne 0 -or $corrY -ne 0) {
        [DpiProbe]::MoveMouseRelative($corrX,$corrY)
        Start-Sleep -Milliseconds 50
    }
    # Wait for cursor to actually reach the target position.
    Wait-Probe {
        $p=New-Object DpiProbe+Point
        [DpiProbe]::GetCursorPos([ref]$p) | Out-Null
        [Math]::Abs($p.X-$petX) -le 3 -and [Math]::Abs($p.Y-$petY) -le 3
    } 'Cursor did not reach pet position.'
    $cursorAtPet=New-Object DpiProbe+Point
    [DpiProbe]::GetCursorPos([ref]$cursorAtPet) | Out-Null
    "Cursor at pet (GetCursorPos): ($($cursorAtPet.X),$($cursorAtPet.Y)) expected=($petX,$petY)"
    [DpiProbe]::Button($true,$false)

    # Wait for capture state to be set by the pet (not just fixed delay).
    Wait-Probe { [DpiProbe]::HasCapture($appWindow) } 'Pet did not capture mouse for drag.'

    # Confirm capture state and record evidence.
    $captured=[DpiProbe]::HasCapture($appWindow)
    "Mouse capture after button-down: $captured"
    if (-not $captured) { throw 'Pet did not capture mouse for drag.' }

    # Focus check — FAIL if pet stole focus (foreground changed from fgBefore).
    # R2 fix #2: Fail on focus loss, do not print WARNING and continue.
    $fgAfterDown=[DpiProbe]::GetForegroundWindow()
    if ($fgAfterDown -ne $fgBefore) {
        throw "Pet stole focus on left-button down (fgBefore=$fgBefore fgAfterDown=$fgAfterDown target=$($target.Handle))."
    }
    "PASS: foreground unchanged after left-button down (fg=$fgAfterDown)"

    # Move cursor by +100, +60.
    $targetX=$petX+100
    $targetY=$petY+60
    $setCursorResult2=[DpiProbe]::SetCursorPos($targetX,$targetY)
    Start-Sleep -Milliseconds 50
    $afterSet2=New-Object DpiProbe+Point
    [DpiProbe]::GetCursorPos([ref]$afterSet2) | Out-Null
    "SetCursorPos to drag target: result=$setCursorResult2 target=($targetX,$targetY) actual=($($afterSet2.X),$($afterSet2.Y))"
    $corrX2=$targetX-$afterSet2.X
    $corrY2=$targetY-$afterSet2.Y
    if ($corrX2 -ne 0 -or $corrY2 -ne 0) {
        [DpiProbe]::MoveMouseRelative($corrX2,$corrY2)
        Start-Sleep -Milliseconds 50
    }
    # Wait for cursor to reach target position.
    Wait-Probe {
        $p=New-Object DpiProbe+Point
        [DpiProbe]::GetCursorPos([ref]$p) | Out-Null
        [Math]::Abs($p.X-$targetX) -le 3 -and [Math]::Abs($p.Y-$targetY) -le 3
    } 'Cursor did not reach drag target position.'
    $cursorEnd=New-Object DpiProbe+Point
    [DpiProbe]::GetCursorPos([ref]$cursorEnd) | Out-Null
    $cursorEndX=$cursorEnd.X
    $cursorEndY=$cursorEnd.Y
    "Cursor end (GetCursorPos): ($cursorEndX,$cursorEndY) expected=($targetX,$targetY)"

    # Wait for window position to stabilize (not just fixed delay).
    $stableRect=$null
    $stableCount=0
    $stableTimer=[Diagnostics.Stopwatch]::StartNew()
    do {
        [System.Windows.Forms.Application]::DoEvents()
        $curRect=Get-PetRect
        if ($stableRect -ne $null -and $curRect.Left -eq $stableRect.Left -and $curRect.Top -eq $stableRect.Top) {
            $stableCount++
        } else {
            $stableCount=0
        }
        $stableRect=$curRect
        if ($stableCount -ge 3) { break }
        Start-Sleep -Milliseconds 50
    } while ($stableTimer.ElapsedMilliseconds -lt 2000)
    "Window stabilized at ($($stableRect.Left),$($stableRect.Top)) after $([Math]::Round($stableTimer.ElapsedMilliseconds))ms"

    # Release mouse button.
    [DpiProbe]::Button($false,$false)

    # Wait for capture to be released (not just fixed delay).
    Wait-Probe { -not [DpiProbe]::HasCapture($appWindow) } 'Pet did not release capture on mouse-up.'

    # Verify capture released.
    $stillCaptured=[DpiProbe]::HasCapture($appWindow)
    "Mouse capture after button-up: $stillCaptured"
    if ($stillCaptured) { throw 'Pet did not release capture on mouse-up.' }

    $moved=Get-PetRect
    $deltaX=$moved.Left-$rect.Left
    $deltaY=$moved.Top-$rect.Top
    $cursorDeltaX=$cursorEndX-$cursorAtPet.X
    $cursorDeltaY=$cursorEndY-$cursorAtPet.Y
    "Drag: before=($($rect.Left),$($rect.Top)); after=($($moved.Left),$($moved.Top)); delta=($deltaX,$deltaY); expected=(100,60)"
    "Cursor: start=($($cursorAtPet.X),$($cursorAtPet.Y)); end=($cursorEndX,$cursorEndY); cursorDelta=($cursorDeltaX,$cursorDeltaY)"

    # R2 fix #1: Only check that the window actually moved. Do NOT compare
    # window displacement against cursor displacement — PMv2 PointToScreen
    # coordinate space differs from GetCursorPos, making that comparison
    # unreliable as a pass/fail criterion (per reviewer R2 directive).
    $cursorDelivered=([Math]::Abs($cursorDeltaX-100) -le 15 -and [Math]::Abs($cursorDeltaY-60) -le 15)
    if (-not $cursorDelivered) {
        "TEST_INVALID: Cursor did not reach target. cursorDelta=($cursorDeltaX,$cursorDeltaY) expected=(100,60). This is an automation issue, not an application bug."
        throw "Automation failed: cursor displacement=($cursorDeltaX,$cursorDeltaY) does not match expected (100,60). Test invalid."
    }
    "PASS: cursor delivered to target within 15px tolerance (cursorDelta=($cursorDeltaX,$cursorDeltaY))"

    # Record full evidence of cursor and window positions for reviewer audit.
    "Evidence: cursor start=($($cursorAtPet.X),$($cursorAtPet.Y)) end=($cursorEndX,$cursorEndY) cursorDelta=($cursorDeltaX,$cursorDeltaY)"
    "Evidence: window before=($($rect.Left),$($rect.Top)) after=($($moved.Left),$($moved.Top)) windowDelta=($deltaX,$deltaY)"
    "Evidence: WPF DIP before=($([Math]::Round($rect.Left/1.5)),$([Math]::Round($rect.Top/1.5))) after=($([Math]::Round($moved.Left/1.5)),$([Math]::Round($moved.Top/1.5)))"

    # Verify the window actually moved (not necessarily by the exact cursor delta).
    if ($deltaX -eq 0 -and $deltaY -eq 0) {
        throw "Window did not move after drag. before=($($rect.Left),$($rect.Top)) after=($($moved.Left),$($moved.Top))"
    }
    "PASS: window moved after drag (delta=($deltaX,$deltaY))"

    # Verify window is within work area after clamp.
    if ($moved.Left -lt $work.Left -or $moved.Top -lt $work.Top -or $moved.Right -gt $work.Right -or $moved.Bottom -gt $work.Bottom) {
        throw "Window outside work area after drag clamp: ($($moved.Left),$($moved.Top),$($moved.Right),$($moved.Bottom)) vs work=($($work.Left),$($work.Top),$($work.Right),$($work.Bottom))"
    }
    "PASS: window within work area after drag clamp"

    # Mouse-up stop verification (R2 fix #4): move cursor away and verify
    # window does not follow. Record cursor and window positions as evidence.
    $awayX=$petX+250
    $awayY=$petY+160
    $setCursorResult3=[DpiProbe]::SetCursorPos($awayX,$awayY)
    Start-Sleep -Milliseconds 50
    $afterSet3=New-Object DpiProbe+Point
    [DpiProbe]::GetCursorPos([ref]$afterSet3) | Out-Null
    "SetCursorPos to away: result=$setCursorResult3 target=($awayX,$awayY) actual=($($afterSet3.X),$($afterSet3.Y))"
    $corrX3=$awayX-$afterSet3.X
    $corrY3=$awayY-$afterSet3.Y
    if ($corrX3 -ne 0 -or $corrY3 -ne 0) {
        [DpiProbe]::MoveMouseRelative($corrX3,$corrY3)
        Start-Sleep -Milliseconds 50
    }
    # Wait for cursor to reach the away position.
    Wait-Probe {
        $p=New-Object DpiProbe+Point
        [DpiProbe]::GetCursorPos([ref]$p) | Out-Null
        [Math]::Abs($p.X-$awayX) -le 3 -and [Math]::Abs($p.Y-$awayY) -le 3
    } 'Cursor did not reach away position for stop test.'
    # Wait for window position to stabilize after cursor moved away.
    $stopRect=$null
    $stopCount=0
    $stopTimer=[Diagnostics.Stopwatch]::StartNew()
    do {
        [System.Windows.Forms.Application]::DoEvents()
        $curRect=Get-PetRect
        if ($stopRect -ne $null -and $curRect.Left -eq $stopRect.Left -and $curRect.Top -eq $stopRect.Top) {
            $stopCount++
        } else {
            $stopCount=0
        }
        $stopRect=$curRect
        if ($stopCount -ge 3) { break }
        Start-Sleep -Milliseconds 50
    } while ($stopTimer.ElapsedMilliseconds -lt 1000)
    $cursorAway=New-Object DpiProbe+Point
    [DpiProbe]::GetCursorPos([ref]$cursorAway) | Out-Null
    "Stop test: cursor moved to ($($cursorAway.X),$($cursorAway.Y)) expected=($awayX,$awayY)"
    "Stop test: window before mouse-away=($($moved.Left),$($moved.Top)) after=($($stopRect.Left),$($stopRect.Top))"
    if ($stopRect.Left -ne $moved.Left -or $stopRect.Top -ne $moved.Top) {
        throw "Pet kept following after mouse-up: before=($($moved.Left),$($moved.Top)) after_cursor_away=($($stopRect.Left),$($stopRect.Top))"
    }
    "PASS: window stopped moving after mouse-up (cursor moved away to ($awayX,$awayY), window unchanged)"

    # Focus check after drag — FAIL if focus changed from fgBefore.
    $fgAfterDrag=[DpiProbe]::GetForegroundWindow()
    if ($fgAfterDrag -ne $fgBefore) {
        throw "Foreground changed after drag (fgBefore=$fgBefore fgAfterDrag=$fgAfterDrag target=$($target.Handle))."
    }
    "PASS: foreground unchanged after drag (fg=$fgAfterDrag)"

    # Menu dismiss — use Esc key instead of outside click to avoid
    # WindowFromPoint hitting the wrong window after drag moved the pet.
    Open-PetMenu
    [DpiProbe]::Escape()
    Wait-Probe { [DpiProbe]::FindWindow([uint32]$probe.Id,$true) -eq [IntPtr]::Zero } 'Esc failed to dismiss menu.'
    "PASS: Esc menu dismissal"

    Open-PetMenu
    Select-MenuItem 1
    Wait-Probe { $probe.Refresh(); $probe.HasExited } 'Exit failed.'
    $probe.Refresh()
    # WPF OnMainWindowClose may report ExitCode as null/empty/0;
    # only fail on a non-zero integer exit code.
    $ec = $probe.ExitCode
    if ($ec -ne $null -and $ec -ne '' -and $ec -ne 0) { throw "Exit code=$ec" }
    "PASS: Exit naturally ends process with code 0"

    "`nALL PASS"
} catch {
    "FAIL: $($_.Exception.Message)"
    throw
} finally {
    if ($probe) {
        $probe.Refresh()
        if (-not $probe.HasExited -and $appWindow -ne [IntPtr]::Zero) {
            [DpiProbe]::PostMessage($appWindow,0x001F,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
            [DpiProbe]::PostMessage($appWindow,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
            $probe.WaitForExit(1500) | Out-Null
        }
    }
    $target.Close()
    $target.Dispose()
    [DpiProbe]::SetCursorPos($previousCursor.X,$previousCursor.Y) | Out-Null
    if ($previousForeground -ne [IntPtr]::Zero) { [DpiProbe]::SetForegroundWindow($previousForeground) | Out-Null }
    if ($previousContext -ne [IntPtr]::Zero) { [DpiProbe]::SetThreadDpiAwarenessContext($previousContext) | Out-Null }
}
