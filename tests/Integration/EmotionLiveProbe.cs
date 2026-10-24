using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Forms;
using DesktopPet.Core.Animation;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;

namespace DesktopPet.App;

// Included only in an isolated copy of the production App. Real cursor, real
// event handlers, real clocks, actual tray handlers and debounced archive writes.
public partial class App
{
    private int _liveFailures;
    private string LiveDir => Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR")!;
    private void LiveLog(string message) => File.AppendAllText(Path.Combine(LiveDir, "live.log"), $"{DateTimeOffset.Now:O} {message}\n");
    private void LiveCheck(bool ok, string message)
    { if (!ok) _liveFailures++; LiveLog($"{(ok ? "PASS" : "FAIL")} {message}"); }
    private static T LiveField<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private Image LiveImage => (Image)_mainWindow!.FindName("PetImage");
    private Point LivePoint(double x, double y) => LiveImage.PointToScreen(new Point(x, y));
    private async Task LiveMove(Point target, int durationMs = 0)
    {
        LiveInput.GetCursorPos(out var start);
        int steps = Math.Max(1, durationMs / 20);
        for (int i = 1; i <= steps; i++)
        {
            LiveInput.Move(start.X + (target.X - start.X) * i / steps, start.Y + (target.Y - start.Y) * i / steps);
            if (durationMs > 0) await Task.Delay(durationMs / steps);
        }
    }
    private void LiveTray(string name)
    {
        var icon = LiveField<NotifyIcon>(_tray!, "_notifyIcon");
        LiveLog($"Actual tray ToolStripMenuItem.PerformClick({name}); not an Explorer icon click");
        icon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == name).PerformClick();
    }
    private async Task LiveIdle()
    {
        await LiveMove(LivePoint(-160, 65));
        LiveField<AnimationPlayer>(_mainWindow!, "_animPlayer").PlayAction("idle");
        await Task.Delay(100);
    }
    private async Task LiveStroke(int count, int intervalMs)
    {
        await LiveMove(LivePoint(60, 20), 160);
        LiveInput.Button(0x0002);
        await Task.Delay(60);
        LiveCheck(_mainWindow!.IsPettingCandidate && !_mainWindow.IsDragging, "real head press starts petting, not dragging");
        for (int i = 0; i < count; i++)
        {
            await LiveMove(LivePoint(i % 2 == 0 ? 62 : 60, 20));
            await Task.Delay(intervalMs);
        }
        LiveLog($"Real stroke accumulatedMs={LiveField<double>(_mainWindow, "_pettingAccumulatedMs")}");
        LiveInput.Button(0x0004);
        await Task.Delay(60);
    }
    private async Task RunEmotionLiveAsync()
    {
        LiveInput.GetCursorPos(out var original);
        try
        {
            await Task.Delay(300);
            _mainWindow!.Left = 350; _mainWindow.Top = 300;
            MarkStateDirty();
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle;
            LiveLog($"HWND={hwnd}; DPI={LiveInput.GetDpiForWindow(hwnd)}; visible={LiveInput.IsWindowVisible(hwnd)}; affinity={EmotionAccumulator!.AffinityInt}; real cursor/time; production App startup/save; isolated archive");
            LiveCheck(typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(f => f.Name.StartsWith("Test", StringComparison.Ordinal)).All(f => f.GetValue(_mainWindow) is null), "all Test input/time/emotion injection fields remain null");
            await LiveIdle();
            LiveCheck(_mainWindow.IsInHeadZone(new Point(60,20)) && !_mainWindow.IsInHeadZone(new Point(60,5)), "actual image has opaque head and transparent control point");

            int startAffinity = EmotionAccumulator.AffinityInt;
            await LiveMove(LivePoint(60,20),160);
            LiveInput.Button(0x0002); await Task.Delay(1500); LiveInput.Button(0x0004); await Task.Delay(100);
            LiveCheck(EmotionAccumulator.AffinityInt == startAffinity, "stationary head hold does not award affection");

            await LiveIdle();
            await LiveStroke(30,60);
            LiveCheck(EmotionAccumulator.AffinityInt == startAffinity + 5, "real micro-movement and release awards exactly +5 affinity");
            await LiveStroke(22,50);
            LiveCheck(EmotionAccumulator.AffinityInt == startAffinity + 5, "second real gesture inside two-second cooldown does not award twice");
            await LiveMove(LivePoint(-160,65));
            await Task.Delay(5600);
            using (var json = JsonDocument.Parse(File.ReadAllText(_archive!.StateFilePath)))
                LiveCheck(json.RootElement.GetProperty("affinity").GetInt32() == startAffinity + 5, "actual default five-second debounced state.json persists affinity +5");

            await LiveIdle();
            await LiveMove(LivePoint(60,20),160); LiveInput.Button(0x0002); await Task.Delay(60);
            // Exit toward transparent pixels slowly: no large drag-conversion jump.
            await LiveMove(LivePoint(60,5),400);
            LiveCheck(!_mainWindow.IsPettingCandidate, "real move into transparent head region ends petting");
            LiveInput.Button(0x0004); await Task.Delay(80);
            LiveCheck(EmotionAccumulator.AffinityInt == startAffinity + 5, "incomplete transparent-exit gesture awards no affection");

            await LiveIdle();
            await LiveMove(LivePoint(60,70),160);
            var before = _mainWindow.PointToScreen(new Point(0,0));
            LiveInput.Button(0x0002); await Task.Delay(80);
            LiveCheck(_mainWindow.IsDragging && !_mainWindow.IsPettingCandidate && _mainWindow.CurrentAction == "drag", "real body press selects drag, not petting");
            await LiveMove(LivePoint(140,110),320);
            var after = _mainWindow.PointToScreen(new Point(0,0));
            double scale = LiveInput.GetDpiForWindow(hwnd)/96.0;
            LiveCheck(Math.Abs(after.X-before.X-80*scale)<=3 && Math.Abs(after.Y-before.Y-40*scale)<=3,
                $"body drag follows pointer displacement; windowDelta=({after.X-before.X},{after.Y-before.Y}), scale={scale}");
            LiveInput.Button(0x0004); await Task.Delay(100);
            var released = _mainWindow.PointToScreen(new Point(0,0));
            await LiveMove(LivePoint(-160,65)); await Task.Delay(200);
            var settled = _mainWindow.PointToScreen(new Point(0,0));
            LiveCheck(!_mainWindow.IsDragging && released == settled, "real mouse release stops following");
            LiveCheck(EmotionAccumulator.AffinityInt == startAffinity + 5, "body dragging does not award petting affinity");

            await LiveIdle();
            var timer = LiveField<System.Windows.Threading.DispatcherTimer>(_mainWindow, "_updateTimer");
            var player = LiveField<AnimationPlayer>(_mainWindow, "_animPlayer");
            LiveTray("Pause");
            var pausedSource = LiveImage.Source;
            double pausedGrievance = EmotionAccumulator.GrievanceFloat;
            LiveCheck(_isPaused && _mainWindow.IsAppPaused && player.IsPaused && !timer.IsEnabled, "actual tray Pause handler pauses app, player and timer together");
            await LiveMove(LivePoint(60,65),300); await Task.Delay(1500);
            LiveCheck(ReferenceEquals(pausedSource,LiveImage.Source) && EmotionAccumulator.GrievanceFloat == pausedGrievance && _mainWindow.CurrentAction == "idle", "during pause real cursor dwell does not animate or trigger curious");
            LiveTray("Resume");
            LiveCheck(!_isPaused && !_mainWindow.IsAppPaused && !player.IsPaused && timer.IsEnabled, "actual tray Resume handler resumes app, player and timer together");
            await LiveMove(LivePoint(-160,65)); await Task.Delay(650);
            await LiveMove(LivePoint(60,65),500); await Task.Delay(1500);
            LiveCheck(_mainWindow.CurrentAction == "curious", "fresh real cursor dwell works after Resume");
            await LiveMove(LivePoint(-160,65));
            LiveLog($"FINAL affinity={EmotionAccumulator.AffinityInt}; checksFailures={_liveFailures}");
        }
        catch (Exception ex) { _liveFailures++; LiveLog("ERROR " + ex); }
        finally
        {
            LiveInput.Button(0x0004);
            LiveInput.Move(original.X,original.Y);
            LiveLog("Cursor restored; normal tray Exit follows");
            LiveTray("Exit");
            LiveLog($"RESULT failures={_liveFailures}; real input, no Test providers; tray event handlers not Shell clicks");
            Environment.ExitCode = _liveFailures == 0 ? 0 : 1;
        }
    }
    private static class LiveInput
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public INPUTUNION Data; }
        [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public MOUSE Mouse; [FieldOffset(0)] public KEY Key; }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSE { public int X,Y; public uint MouseData,Flags,Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct KEY { public ushort Vk,Scan; public uint Flags,Time; public UIntPtr Extra; }
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int n);
        [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint n,INPUT[] inputs,int size);
        private static void Send(MOUSE mouse)
        { if(SendInput(1,[new INPUT {Data=new INPUTUNION {Mouse=mouse}}],Marshal.SizeOf<INPUT>())!=1) throw new InvalidOperationException("SendInput: "+Marshal.GetLastPInvokeError()); }
        internal static void Button(uint flags) => Send(new MOUSE {Flags=flags});
        internal static void Move(double x,double y) => Send(new MOUSE {
            X=(int)Math.Round((x-GetSystemMetrics(76))*65535/(GetSystemMetrics(78)-1)),
            Y=(int)Math.Round((y-GetSystemMetrics(77))*65535/(GetSystemMetrics(79)-1)),Flags=0xC001 });
    }
}
