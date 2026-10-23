param([switch]$ExpectCrash, [ValidateSet(0,96,144)][int]$ExpectedDpi=0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class RecoverProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct GuiInfo { public int Size; public uint Flags; public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret; public Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyInput { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Data; }
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
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] inputs,int size);
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
    public static bool HasCapture(IntPtr window) {
        uint process; uint thread=GetWindowThreadProcessId(window,out process);
        var info=new GuiInfo{Size=Marshal.SizeOf<GuiInfo>()};
        if(!GetGUIThreadInfo(thread,ref info)) throw new InvalidOperationException("Cannot read test app mouse capture.");
        return info.Capture==window;
    }
    public static void Click(int x,int y,bool right,uint expectedProcess) {
        uint actual; GetWindowThreadProcessId(WindowFromPoint(new Point{X=x,Y=y}),out actual);
        if(actual!=expectedProcess) throw new InvalidOperationException("Refusing to click an unrelated window.");
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
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 40
    } while ($timer.ElapsedMilliseconds -lt 5000)
    throw $Failure
}
function Get-PetRect {
    $rect=New-Object RecoverProbe+Rect
    if (-not [RecoverProbe]::GetWindowRect($script:appWindow,[ref]$rect)) { throw 'Cannot read pet window.' }
    return $rect
}
function Open-PetMenu([switch]$Offscreen) {
    if ($Offscreen) {
        [RecoverProbe]::SetForegroundWindow($script:appWindow) | Out-Null
        [RecoverProbe]::SetCursorPos([int]($script:work.Left+$script:work.Right)/2,[int]($script:work.Top+$script:work.Bottom)/2) | Out-Null
        [RecoverProbe]::PostMessage($script:appWindow,0x0205,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    } else {
        $rect=Get-PetRect
        [RecoverProbe]::Click([int](($rect.Left+$rect.Right)/2),[int]($rect.Top+($rect.Bottom-$rect.Top)*0.60),$true,[uint32]$script:probe.Id)
    }
    Wait-Probe { [RecoverProbe]::FindWindow([uint32]$script:probe.Id,$true) -ne [IntPtr]::Zero } 'Pet menu did not open.'
}
function Select-MenuItem([uint32]$Index) {
    $menuWindow=[RecoverProbe]::FindWindow([uint32]$script:probe.Id,$true)
    $menu=[RecoverProbe]::SendMessage($menuWindow,0x01E1,[IntPtr]::Zero,[IntPtr]::Zero)
    $rect=New-Object RecoverProbe+Rect
    if (-not [RecoverProbe]::GetMenuItemRect([IntPtr]::Zero,$menu,$Index,[ref]$rect)) { throw 'Cannot locate native menu item.' }
    [RecoverProbe]::Click([int](($rect.Left+$rect.Right)/2),[int](($rect.Top+$rect.Bottom)/2),$false,[uint32]$script:probe.Id)
}
function Assert-Recovered([string]$Label, [bool]$RequireCenter=$true) {
    Wait-Probe {
        $script:probe.Refresh()
        if ($script:probe.HasExited) { throw "Pet crashed: exit=$($script:probe.ExitCode)" }
        $rect=Get-PetRect
        $rect.Left -ge $script:work.Left -and $rect.Top -ge $script:work.Top -and $rect.Right -le $script:work.Right -and $rect.Bottom -le $script:work.Bottom -and
            (-not $RequireCenter -or [Math]::Abs(($rect.Left+$rect.Right)/2-($script:work.Left+$script:work.Right)/2) -lt 2)
    } "Recover failed: $Label"
    Start-Sleep -Milliseconds 200
    $rect=Get-PetRect
    if (([RecoverProbe]::GetWindowLong($script:appWindow,-20) -band 0x08000000) -eq 0) { throw 'NOACTIVATE lost after Recover.' }
    "PASS: $Label; rect=($($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom))"
}
$previousContext=[RecoverProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
$previousForeground=[RecoverProbe]::GetForegroundWindow()
$previousCursor=New-Object RecoverProbe+Point
[RecoverProbe]::GetCursorPos([ref]$previousCursor) | Out-Null
$info=New-Object RecoverProbe+MonitorInfo
$info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
$origin=New-Object RecoverProbe+Point
if (-not [RecoverProbe]::GetMonitorInfo([RecoverProbe]::MonitorFromPoint($origin,1),[ref]$info)) { throw 'Cannot read primary work area.' }
$work=$info.Work
$target=New-Object System.Windows.Forms.Form
$target.Text='DesktopPet recover test target'
$target.ShowInTaskbar=$false
$target.TopMost=$true
$target.StartPosition='Manual'
$target.SetBounds($work.Right-350,$work.Bottom-220,320,180)
$probe=$null
$appWindow=[IntPtr]::Zero
$exe=Join-Path $PSScriptRoot '../../../src/DesktopPet.App/bin/Debug/net10.0-windows/DesktopPet.exe'
$errorFile=Join-Path $PSScriptRoot $(if($ExpectCrash){'before-stderr.txt'}else{'after-stderr.txt'})
try {
    $target.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $probe=Start-Process -FilePath $exe -WindowStyle Hidden -PassThru -RedirectStandardError $errorFile
    Wait-Probe { $script:appWindow=[RecoverProbe]::FindWindow([uint32]$probe.Id,$false); $appWindow -ne [IntPtr]::Zero } 'Pet window did not appear.'
    "Environment: Windows=$([Environment]::OSVersion.Version); DPI=$([RecoverProbe]::GetDpiForWindow($appWindow)); PMv2=$([RecoverProbe]::AreDpiAwarenessContextsEqual([RecoverProbe]::GetWindowDpiAwarenessContext($appWindow),[IntPtr](-4))); PID=$($probe.Id); work=($($work.Left),$($work.Top),$($work.Right),$($work.Bottom))"
    if (-not [RecoverProbe]::AreDpiAwarenessContextsEqual([RecoverProbe]::GetWindowDpiAwarenessContext($appWindow),[IntPtr](-4))) { throw 'PMv2 is not enabled.' }
    if ($ExpectedDpi -ne 0 -and [RecoverProbe]::GetDpiForWindow($appWindow) -ne $ExpectedDpi) { throw "Actual HWND DPI does not match required DPI $ExpectedDpi." }
    Open-PetMenu
    Select-MenuItem 0
    if ($ExpectCrash) {
        Wait-Probe { (Get-Item -LiteralPath $errorFile).Length -gt 0 } 'Expected exception was not reproduced.'
        $finished=$probe.WaitForExit(20000)
        "REPRODUCED: Recover wrote unhandled exception; process exited=$finished"
        if ($finished) { "Crash exit code=$($probe.ExitCode)" }
        Get-Content -LiteralPath $errorFile
        return
    }
    Assert-Recovered 'Recover from startup position'
    for ($iteration=1;$iteration -le 3;$iteration++) {
        Open-PetMenu
        Select-MenuItem 0
        Assert-Recovered "repeated Recover $iteration"
    }
    foreach ($notification in @(0x001A,0x007E)) {
        [RecoverProbe]::SetWindowPos($appWindow,[IntPtr]::Zero,$work.Left-500,$work.Top-500,0,0,0x0015) | Out-Null
        Wait-Probe { $offscreen=Get-PetRect; $offscreen.Right -lt $work.Left } 'Cannot place owned test window offscreen.'
        # WM_SETTINGCHANGE contains a pointer parameter and cannot be posted
        # asynchronously. Use synchronous delivery to the owned test HWND.
        [RecoverProbe]::SendMessage($appWindow,$notification,[IntPtr](0x002F),[IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 400
        $notificationRect=Get-PetRect
        "Notification result: 0x$($notification.ToString('X4')); rect=($($notificationRect.Left),$($notificationRect.Top),$($notificationRect.Right),$($notificationRect.Bottom))"
        Assert-Recovered ("owned-window notification 0x{0:X4}; system layout unchanged" -f $notification) $false
    }
    Open-PetMenu
    Select-MenuItem 0
    Assert-Recovered 'Recover after notification routing tests'
    $rect=Get-PetRect
    $petX=[int](($rect.Left+$rect.Right)/2)
    $petY=[int]($rect.Top+($rect.Bottom-$rect.Top)*0.60)
    [RecoverProbe]::SetForegroundWindow($target.Handle) | Out-Null
    [RecoverProbe]::Click($petX,$petY,$false,[uint32]$probe.Id)
    if ([RecoverProbe]::GetForegroundWindow() -ne $target.Handle) { throw 'Pet stole focus after Recover.' }
    [RecoverProbe]::SetCursorPos($petX,$petY) | Out-Null
    [RecoverProbe]::Button($true,$false)
    Wait-Probe { [RecoverProbe]::HasCapture($appWindow) } 'Pet did not capture mouse for drag.'
    [RecoverProbe]::SetCursorPos($petX+100,$petY+60) | Out-Null
    Start-Sleep -Milliseconds 180
    [RecoverProbe]::Button($false,$false)
    Wait-Probe { -not [RecoverProbe]::HasCapture($appWindow) } 'Pet did not release capture on mouse-up.'
    Start-Sleep -Milliseconds 100
    $moved=Get-PetRect
    "Drag measurement: before=($($rect.Left),$($rect.Top)); after=($($moved.Left),$($moved.Top)); expected delta=(100,60)"
    if ([Math]::Abs($moved.Left-$rect.Left-100) -gt 2 -or [Math]::Abs($moved.Top-$rect.Top-60) -gt 2) { throw 'Drag offset drift after Recover.' }
    [RecoverProbe]::SetCursorPos($petX+150,$petY+100) | Out-Null
    Start-Sleep -Milliseconds 120
    $stopped=Get-PetRect
    if ($stopped.Left -ne $moved.Left -or $stopped.Top -ne $moved.Top) { throw 'Pet kept following after mouse-up.' }
    if ([RecoverProbe]::GetForegroundWindow() -ne $target.Handle) { throw 'Drag stole focus after Recover.' }
    'PASS: drag after Recover moves by physical cursor delta, mouse-up stops, foreground remains external test window'
    Open-PetMenu
    $targetRect=New-Object RecoverProbe+Rect
    [RecoverProbe]::GetWindowRect($target.Handle,[ref]$targetRect) | Out-Null
    [RecoverProbe]::Click($targetRect.Left+50,$targetRect.Top+70,$false,[uint32]$PID)
    Wait-Probe { [RecoverProbe]::FindWindow([uint32]$probe.Id,$true) -eq [IntPtr]::Zero } 'Outside click failed to dismiss.'
    'PASS: outside-click menu dismissal after Recover'
    Open-PetMenu
    [RecoverProbe]::Escape()
    Wait-Probe { [RecoverProbe]::FindWindow([uint32]$probe.Id,$true) -eq [IntPtr]::Zero } 'Escape failed.'
    'PASS: Escape menu dismissal after Recover'
    Open-PetMenu
    Select-MenuItem 1
    Wait-Probe { $probe.Refresh(); $probe.HasExited } 'Exit failed to terminate naturally.'
    if ($probe.ExitCode -ne 0) { throw "Exit code=$($probe.ExitCode)" }
    'PASS: actual Exit menu command naturally ends process with code 0'
} finally {
    if ($probe) {
        $probe.Refresh()
        if (-not $probe.HasExited -and $appWindow -ne [IntPtr]::Zero) {
            [RecoverProbe]::PostMessage($appWindow,0x001F,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
            [RecoverProbe]::PostMessage($appWindow,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
            $probe.WaitForExit(1500) | Out-Null
        }
    }
    $target.Close()
    $target.Dispose()
    [RecoverProbe]::SetCursorPos($previousCursor.X,$previousCursor.Y) | Out-Null
    if ($previousForeground -ne [IntPtr]::Zero) { [RecoverProbe]::SetForegroundWindow($previousForeground) | Out-Null }
    if ($previousContext -ne [IntPtr]::Zero) { [RecoverProbe]::SetThreadDpiAwarenessContext($previousContext) | Out-Null }
}
