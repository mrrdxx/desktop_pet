using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Threading;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Persistence;

namespace DesktopPet.App;

// Test-copy instrumentation only. Production startup/shutdown handlers remain intact.
public partial class App
{
    private string ValidationDirectory => Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_DIR")!;
    private void Record(string message) => File.AppendAllText(Path.Combine(ValidationDirectory,"probe.log"), message + Environment.NewLine);
    private void Check(bool condition,string label) => Record((condition ? "PASS: " : "FAIL: ") + label);
    private T ReadField<T>(object instance,string name) => (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
    private void ClickTray(string text)
    {
        var icon=ReadField<NotifyIcon>(_tray!,"_notifyIcon");
        var item=icon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(x=>x.Text==text);
        Record("Invoking actual ToolStripMenuItem.PerformClick: " + text);
        item.PerformClick();
    }
    [StructLayout(LayoutKind.Sequential)] private struct ValidationRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out ValidationRect rect);
    [DllImport("user32.dll")] private static extern int GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetDpiForSystem();
    private IntPtr _recordedHwnd;
    private void RecordHwndDpi()
    {
        _recordedHwnd=new System.Windows.Interop.WindowInteropHelper(_mainWindow!).Handle;
        var dpi=GetDpiForWindow(_recordedHwnd);
        var sysDpi=GetDpiForSystem();
        Record($"HWND=0x{_recordedHwnd.ToInt64():X}; GetDpiForWindow={dpi}; GetDpiForSystem={sysDpi}; scale={(int)Math.Round(dpi/96.0*100)}%");
    }
    private bool WindowVisibleInWorkArea()
    {
        GetWindowRect(_recordedHwnd,out var rect);
        var w=rect.Right-rect.Left; var h=rect.Bottom-rect.Top;
        Record($"Native window rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}); size={w}x{h} physical px");
        var primary=Screen.PrimaryScreen!;
        Record($"PrimaryScreen working area=({primary.WorkingArea.Left},{primary.WorkingArea.Top},{primary.WorkingArea.Right},{primary.WorkingArea.Bottom}); bounds=({primary.Bounds.Left},{primary.Bounds.Top},{primary.Bounds.Right},{primary.Bounds.Bottom})");
        return Screen.AllScreens.Any(s=>rect.Left>=s.WorkingArea.Left && rect.Top>=s.WorkingArea.Top && rect.Right<=s.WorkingArea.Right && rect.Bottom<=s.WorkingArea.Bottom);
    }
    private async Task RunValidationAsync()
    {
        try
        {
            await Task.Delay(200);
            var scenario=Environment.GetEnvironmentVariable("DESKTOPPET_VALIDATION_SCENARIO") ?? "normal";
            Record("READY: scenario="+scenario);
            RecordHwndDpi();
            // F-05: Use _updateTimer (renamed from _frameTimer in P1-001).
            var timer=ReadField<DispatcherTimer>(_mainWindow!,"_updateTimer");
            var icon=ReadField<NotifyIcon>(_tray!,"_notifyIcon");
            string? protectedPath=null;
            string? protectedContent=null;
            Check(icon.Visible && icon.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Count()==3,"tray object visible with three actionable menu items (not a Shell click test)");
            Record($"Loaded position=({_mainWindow!.Left},{_mainWindow.Top}); paused={_isPaused}; timerEnabled={timer.IsEnabled}; currentAction={_mainWindow.CurrentAction}");
            // F-05: Verify actual character resources are loaded (not fallback).
            var imageCache=ReadField<Dictionary<string,System.Windows.Media.Imaging.BitmapImage>>(_mainWindow,"_imageCache");
            Check(imageCache.Count>0,"character PNG frames loaded into image cache");
            Check(imageCache.ContainsKey("idle_0.png"),"idle_0.png present in image cache (real character loaded)");
            Record($"Image cache count={imageCache.Count}; keys={string.Join(",",imageCache.Keys.OrderBy(k=>k))}");
            if(scenario=="hold") { await Task.Delay(12000); }
            else if(scenario=="normal")
            {
                // F-05: Verify pause freezes animation time, not just stops the timer.
                // Use AnimationPlayer's IsPaused property via the public CurrentAction accessor.
                var animPlayer=ReadField<DesktopPet.Core.Animation.AnimationPlayer>(_mainWindow,"_animPlayer");
                ClickTray("Pause");
                Check(!timer.IsEnabled,"Pause stops animation timer");
                Check(animPlayer.IsPaused,"Pause freezes animation player time");
                // Record current action to verify it doesn't change during pause.
                var actionBeforePause=_mainWindow.CurrentAction;
                await Task.Delay(1000);
                Check(animPlayer.IsPaused,"animation player remains paused during wall-clock delay");
                Check(_mainWindow.CurrentAction==actionBeforePause,"animation action stays unchanged while paused");
                ClickTray("Recover");
                Check(WindowVisibleInWorkArea() && !timer.IsEnabled,"Recover remains usable while paused");
                ClickTray("Resume");
                Check(timer.IsEnabled,"Resume restarts animation timer");
                Check(!animPlayer.IsPaused,"Resume unfreezes animation player time");
                // After resume, animation should be active (action may or may not change,
                // but the player should not be paused).
                await Task.Delay(1000);
                Check(!animPlayer.IsPaused,"animation player not paused after resume with active timer");
            }
            else if(scenario=="paused-start")
            {
                Check(_isPaused && !timer.IsEnabled,"restart restores pause behavior as well as stored flag");
                var animPlayer=ReadField<DesktopPet.Core.Animation.AnimationPlayer>(_mainWindow,"_animPlayer");
                Check(animPlayer.IsPaused,"saved paused state freezes animation player on startup");
                var actionBefore=_mainWindow.CurrentAction;
                await Task.Delay(1000);
                Check(_mainWindow.CurrentAction==actionBefore,"saved paused state keeps animation action unchanged");
            }
            else if(scenario=="offscreen-start")
            {
                Check(WindowVisibleInWorkArea(),"startup recovers saved offscreen/missing-monitor position");
            }
            else if(scenario=="restart-position")
            {
                Check(Math.Abs(_mainWindow.Left-350)<0.01 && Math.Abs(_mainWindow.Top-260)<0.01,"restart loads position written by previous normal tray exit");
                Check(_settings!.AnimationSpeed==1.7,"startup loads stored settings value (not animation-speed behavior test)");
            }
            else if(scenario=="readonly-state" || scenario=="readonly-settings")
            {
                bool settings=scenario=="readonly-settings";
                protectedPath=settings?_archive!.SettingsFilePath:_archive!.StateFilePath;
                protectedContent=File.ReadAllText(protectedPath);
                Check(settings?_archive.IsSettingsReadOnly:_archive.IsStateReadOnly,"startup recognizes newer archive with incompatible field types");
                Check(!File.Exists(protectedPath+".corrupted"),"future archive was not renamed as corrupted");
                Check(!string.IsNullOrEmpty(icon.BalloonTipText),"tray read-only notification text configured (OS display not asserted)");
                _mainWindow.Left=350; _mainWindow.Top=260;
                MarkStateDirty();
                if(settings)
                {
                    _settings!.AnimationSpeed=2;
                    _settings.SchemaVersion=1;
                    _saveCoordinator!.MarkSettingsDirty(_settings);
                }
                else _state!.SchemaVersion=1;
            }
            else if(scenario=="fast-final")
            {
                _mainWindow.Left=350; _mainWindow.Top=260;
                MarkStateDirty();
                Check(!File.Exists(_archive!.StateFilePath),"debounced dirty state has not written before five seconds");
            }
            else if(scenario=="recover-after-save")
            {
                _mainWindow.Left=300; _mainWindow.Top=240;
                MarkStateDirty();
                await Task.Delay(5700);
                Check(File.Exists(_archive!.StateFilePath),"dirty state writes after default five-second debounce");
                ClickTray("Recover");
                Check(WindowVisibleInWorkArea(),"tray Recover moves window to visible area");
            }
            else if(scenario=="archive-cases") ValidateArchives();

            File.WriteAllText(Path.Combine(ValidationDirectory,"expected-position.json"),JsonSerializer.Serialize(new{left=_mainWindow.Left,top=_mainWindow.Top}));
            Record("Exiting using actual tray Exit handler");
            ClickTray("Exit");
            if(protectedPath is not null)
                Check(File.ReadAllText(protectedPath)==protectedContent,"normal tray exit preserves future archive bytes despite final state changes");
            Record("Tray Exit handler returned");
        }
        catch(Exception ex)
        {
            Record("FAIL: probe exception="+ex);
            Shutdown(2);
        }
    }
    private void ValidateArchives()
    {
        string Root(string name) {var d=Path.Combine(ValidationDirectory,name);Directory.CreateDirectory(d);return d;}
        var save=new ArchiveService(Root("roundtrip"));
        save.SaveState(new PetStateArchive{WindowLeft=123,WindowTop=234,IsPaused=true});
        save.SaveSettings(new PetSettingsArchive{AnimationSpeed=1.7});
        var loaded=save.LoadState();
        Check(loaded.State.WindowLeft==123 && loaded.State.WindowTop==234 && loaded.State.IsPaused,"isolated state service roundtrip");
        Check(save.LoadSettings().Settings.AnimationSpeed==1.7,"isolated settings service roundtrip");

        var backup=new ArchiveService(Root("backup"));
        backup.SaveState(new PetStateArchive{WindowLeft=111});
        backup.SaveState(new PetStateArchive{WindowLeft=222});
        const string broken="{ broken JSON";
        File.WriteAllText(backup.StateFilePath,broken);
        var recovered=backup.LoadState();
        Check(recovered.FromBackup && recovered.State.WindowLeft==111 && File.ReadAllText(backup.StateFilePath+".corrupted")==broken,"corrupt primary preserved byte-for-byte and valid backup selected");
        var defaults=new ArchiveService(Root("defaults"));
        File.WriteAllText(defaults.StateFilePath,broken);
        Check(defaults.LoadState().State.WindowLeft==100 && File.ReadAllText(defaults.StateFilePath+".corrupted")==broken,"corrupt file without backup falls back to defaults");

        foreach(bool incompatible in new[]{false,true})
        {
            var archive=new ArchiveService(Root(incompatible?"unknown-incompatible":"unknown-compatible"));
            string json=incompatible?"{\"schemaVersion\":99,\"windowLeft\":{\"newFormat\":1},\"futurePayload\":42}":"{\"schemaVersion\":99,\"windowLeft\":500,\"futurePayload\":42}";
            File.WriteAllText(archive.StateFilePath,json);
            var future=archive.LoadState();
            archive.SaveState(future.State);
            Check(future.IsUnknownVersion && File.Exists(archive.StateFilePath) && File.ReadAllText(archive.StateFilePath)==json,"higher version state remains read-only; incompatibleFields="+incompatible);
        }
        var settings=new ArchiveService(Root("unknown-settings"));
        const string futureSettings="{\"schemaVersion\":99,\"animationSpeed\":{\"newFormat\":2},\"futurePayload\":77}";
        File.WriteAllText(settings.SettingsFilePath,futureSettings);
        var futureConfig=settings.LoadSettings();
        settings.SaveSettings(futureConfig.Settings);
        Check(futureConfig.IsUnknownVersion && File.ReadAllText(settings.SettingsFilePath)==futureSettings,"higher version incompatible settings remain read-only");
    }
}
