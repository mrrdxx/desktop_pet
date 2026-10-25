using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Image = System.Windows.Controls.Image;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using Point = System.Windows.Point;

namespace DesktopPet.InteractionProbe;

// Purpose-built interactive application test. No production App startup, tray,
// archives, injected cursor/time providers, system settings, or other app clicks.
internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static MainWindow pet = null!;
    private static AnimationPlayer player = null!;
    private static int failures;
    private static string output = string.Empty;
    private static readonly List<string> lines = [];
    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/interaction-live/result.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        pet = new MainWindow { Left = 300, Top = 300, ShowInTaskbar = true };
        player = (AnimationPlayer)typeof(MainWindow).GetField("_animPlayer", Private)!.GetValue(pet)!;
        pet.Loaded += async (_, _) =>
        {
            Native.GetCursorPos(out var original);
            try { await Verify(); }
            catch (Exception ex) { failures++; Log("ERROR " + ex); }
            finally
            {
                Native.Button(0x0004); // Release only our potential left drag.
                Native.Move(original.X, original.Y);
                pet.Close();
                Log($"RESULT failures={failures}; real cursor + visible HWND; no archives; operator subjective feel not measured");
                File.WriteAllLines(output, lines);
                app.Shutdown(failures == 0 ? 0 : 1);
            }
        };
        pet.Show();
        return app.Run();
    }
    private static void Log(string text) { lines.Add($"{DateTimeOffset.Now:O} {text}"); Console.WriteLine(text); }
    private static void Check(bool value, string text) { if (!value) failures++; Log($"{(value ? "PASS" : "FAIL")} {text}"); }
    private static double Origin => (double)typeof(AnimationPlayer).GetField("_actionStartMs", Private)!.GetValue(player)!;
    private static Point Point(double x, double y) => pet.PointToScreen(new Point(x, y));
    private static async Task Move(Point target, int ms = 0)
    {
        Native.GetCursorPos(out var start);
        int steps = Math.Max(1, ms / 20);
        for (int i = 1; i <= steps; i++)
        {
            Native.Move(start.X + (target.X - start.X) * i / steps, start.Y + (target.Y - start.Y) * i / steps);
            if (ms > 0) await Task.Delay(ms / steps);
        }
    }
    private static async Task Reset()
    {
        pet.PauseAnimation(); player.PlayAction("idle");
        await Move(Point(-180, 65)); pet.ResumeAnimation(); await Task.Delay(300);
    }
    private static async Task RapidPass()
    {
        await Move(Point(-180, 65)); await Task.Delay(120);
        await Move(Point(60, 65), 60); await Task.Delay(90);
    }
    private static async Task Verify()
    {
        await Task.Delay(300);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(pet).Handle;
        Log($"WINDOW hwnd={hwnd}; dpi={Native.GetDpiForWindow(hwnd)}; visible={Native.IsWindowVisible(hwnd)}; build={typeof(MainWindow).Assembly.Location}");
        await ObserveNaturalIdle();
        await Reset();
        await Move(Point(4, 4)); await Task.Delay(1600);
        Check(pet.CurrentAction == "idle", "transparent corner does not trigger curiosity");

        await Reset(); await Move(Point(60, 65), 800); await Task.Delay(1500);
        Check(pet.CurrentAction == "curious", "real cursor dwell triggers curious");
        double firstOrigin = Origin; await Task.Delay(400);
        Check(Origin == firstOrigin, "continuous dwell does not replay action");
        await Move(Point(-180, 65)); await Task.Delay(100);
        Check(pet.CurrentAction == "curious", "brief mouse leave keeps curiosity during grace period");
        await Move(Point(60, 65), 100); await Task.Delay(100);
        Check(pet.CurrentAction == "curious" && Origin == firstOrigin, "quick return does not flicker or replay curiosity");
        await Move(Point(-180, 65)); await Task.Delay(650);
        Check(pet.CurrentAction == "idle" && pet.CurrentFrameOffset.y == 0,
            "leaving mouse alone restores idle and clears curious offset without click");
        await Move(Point(60, 65), 800); await Task.Delay(1500);
        Check(pet.CurrentAction == "curious" && Origin != firstOrigin, "leave then re-enter generates a new curious action");
        await Task.Delay(2500);
        Check(pet.CurrentAction == "idle", "continuous hover naturally ends curiosity after two active seconds");
        await Task.Delay(1500);
        Check(pet.CurrentAction == "idle", "stationary pointer does not repeat curiosity after natural end");
        await Move(Point(-180, 65)); await Task.Delay(650);
        await Move(Point(60, 65), 800); await Task.Delay(1500);
        pet.PauseAnimation(); await Move(Point(-180, 65)); await Task.Delay(650);
        Check(pet.CurrentAction == "curious" && pet.IsPlayerPaused, "paused curiosity remains frozen after mouse leave");
        pet.ResumeAnimation(); await Task.Delay(700);
        Check(pet.CurrentAction == "idle", "resuming with mouse outside ends curiosity without any click");

        await Reset(); await Move(Point(60, 65), 1600); await Task.Delay(100);
        Check(pet.CurrentAction == "idle", "slow approach without full dwell keeps idle");
        await Reset(); await RapidPass();
        Check(pet.CurrentAction == "idle", "fast cursor movement alone keeps idle (D-009)");
        await RapidPass();
        Check(pet.CurrentAction == "idle", "repeated rapid movement does not change expression");

        await Reset(); await Move(Point(-450, 65)); await Task.Delay(120);
        await Move(Point(-150, 65), 60); await Move(Point(60, 65), 1500);
        Check(pet.CurrentAction == "idle" || pet.CurrentAction == "curious",
            "fast motion followed by slow approach only allows normal hover feedback");

        await Reset(); await Move(Point(60, 65));
        Native.Button(0x0002); await Task.Delay(100);
        Check(pet.IsDragging && pet.CurrentAction == "drag", "actual left button captures and selects drag");
        await Move(Point(110, 95), 300); await Task.Delay(1500);
        Check(pet.IsDragging && pet.CurrentAction == "drag", "held drag suppresses curiosity");
        Native.Button(0x0004); await Task.Delay(150);
        double releaseLeft = pet.Left, releaseTop = pet.Top;
        await Move(Point(-180, 65)); await Task.Delay(150);
        Check(!pet.IsDragging && pet.Left == releaseLeft && pet.Top == releaseTop, "button up stops capture and cursor following");

        foreach (var head in new[] { new Point(34, 18), new Point(85, 18), new Point(60, 25) })
        {
            await Reset();
            await Move(Point(head.X, head.Y), 200);
            var destination = Point(head.X + 24, head.Y + 12);
            double startLeft = pet.Left, startTop = pet.Top;
            Native.Button(0x0002); await Task.Delay(80);
            Check(pet.IsPettingCandidate, $"head press at {head} is received");
            await Move(destination, 240);
            Check(pet.IsDragging && !pet.IsPettingCandidate
                && System.Windows.Input.Mouse.Captured == pet.FindName("PetCanvas")
                && Math.Abs(pet.Left - startLeft - 24) <= 1 && Math.Abs(pet.Top - startTop - 12) <= 1,
                $"head drag at {head} preserves capture and original anchor");
            Native.Button(0x0004); await Task.Delay(100);
            Check(!pet.IsDragging && !pet.IsPettingCandidate, $"head release at {head} finishes interaction");
        }

        await Reset(); await Move(Point(60, 65));
        Native.Button(0x0008); Native.Button(0x0010); await Task.Delay(300);
        Check(pet.IsContextMenuOpen, "actual right click opens native menu");
        if (pet.IsContextMenuOpen)
        {
            await Move(Point(60, 65)); await Task.Delay(1500);
            Check(pet.CurrentAction == "idle", "native menu suppresses mouse actions");
            Native.Escape(); await Task.Delay(200);
            Check(!pet.IsContextMenuOpen, "Esc dismisses real native menu");
        }

        await Reset(); pet.PauseAnimation();
        await Move(Point(60, 65)); await Task.Delay(1600); await RapidPass();
        Check(pet.CurrentAction == "idle" && pet.IsPlayerPaused, "Pause API blocks real cursor interactions");
        pet.ResumeAnimation(); await Task.Delay(250);
        Check(pet.CurrentAction == "idle", "Resume does not replay paused input immediately");
        await Task.Delay(1500);
        Check(pet.CurrentAction == "curious", "fresh dwell after Resume triggers normally");
    }

    private static async Task ObserveNaturalIdle()
    {
        await Reset();
        var scheduler=(IdleBlinkScheduler)typeof(MainWindow).GetField("_idleBlink",Private)!.GetValue(pet)!;
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var starts=new List<double>(); bool wasBlinking=false;
        var seen=new HashSet<string>(); string last="";
        while(watch.ElapsedMilliseconds<26000)
        {
            var bitmap=(BitmapImage)((Image)pet.FindName("PetImage")).Source;
            string name=Path.GetFileName(bitmap.UriSource.LocalPath);
            seen.Add(name);
            if(name!=last) { Log($"IDLE_FRAME ms={watch.ElapsedMilliseconds}; image={name}"); last=name; }
            if(scheduler.IsBlinking && !wasBlinking) starts.Add(watch.Elapsed.TotalMilliseconds);
            wasBlinking=scheduler.IsBlinking;
            await Task.Delay(15);
        }
        Check(seen.Contains("idle_0.png") && seen.Contains("blink_half.png") && seen.Contains("blink_closed.png")
            && !seen.Contains("content_0.png"),"real idle rendering uses neutral eye-only blink frames, not content smile");
        Check(starts.Count>=3,"real idle observation contains at least three blink cycles");
        bool valid=starts.Count>=3;
        var gaps=new List<double>();
        for(int i=1;i<starts.Count;i++)
        {
            double gap=starts[i]-starts[i-1]; gaps.Add(gap);
            // Includes the previous cycle (220ms, or 580ms for a double) and UI tick rounding.
            valid &= gap>=3150 && gap<=7750;
        }
        Log("BLINK_START_GAPS_MS "+string.Join(",",gaps.Select(g=>g.ToString("F1"))));
        Check(valid,"real blink starts respect quiet 3-7 second intervals plus cycle length/timer tolerance");
        Check(pet.CurrentAction=="idle" && pet.CurrentFrameOffset.y==0,"blink keeps idle action identity and world-foot offset");
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public Union Data; }
        [StructLayout(LayoutKind.Explicit)] private struct Union
        { [FieldOffset(0)] public MOUSE Mouse; [FieldOffset(0)] public KEY Key; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSE
        { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct KEY
        { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        private static void Send(INPUT input)
        { if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1) throw new InvalidOperationException("SendInput failed " + Marshal.GetLastPInvokeError()); }
        internal static void Move(double x, double y)
        {
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            Send(new INPUT { Data = new Union { Mouse = new MOUSE {
                X = (int)Math.Round((x - left) * 65535 / (width - 1)),
                Y = (int)Math.Round((y - top) * 65535 / (height - 1)), Flags = 0xC001 } } });
        }
        internal static void Button(uint flags)
        {
            if ((flags & (0x0002 | 0x0008 | 0x0020)) != 0)
            {
                GetCursorPos(out var pointer);
                GetWindowThreadProcessId(WindowFromPoint(pointer), out var pid);
                if (pid != Environment.ProcessId)
                    throw new InvalidOperationException("Pointer does not hit the owned test window; no button-down sent.");
            }
            Send(new INPUT { Data = new Union { Mouse = new MOUSE { Flags = flags } } });
        }
        internal static void Escape()
        {
            Send(new INPUT { Type = 1, Data = new Union { Key = new KEY { Vk = 0x1B } } });
            Send(new INPUT { Type = 1, Data = new Union { Key = new KEY { Vk = 0x1B, Flags = 2 } } });
        }
    }
}
