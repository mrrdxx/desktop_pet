// P5A-001: window-perch-live 场景（TESTING M-15 隔离预览）。
//
// 真实宠物窗口（生产 CreatePetWindow 路径）+ 真实外部测试窗口（驱动脚本
// 以独立进程启动），显式选中合格目标后落位其可见外框上缘：
//   - 落位：选中后立即移动到上缘中点（WindowPerchLandingPolicy）；
//   - 跟随：目标移动/最大化/还原时经 PerchUpdateGate 跟随（受守卫写入）；
//   - 脱离：目标关闭/最小化/隐藏/失去资格（PerchTargetTracker 清除）或
//     落位处被遮挡（WindowPerchOcclusionPolicy，初版策略=脱离）；
//   - 拖动抢占：用户开始拖动（真实鼠标注入经驱动）立即 CancelForDrag，
//     松手后不回拉旧目标（被取消句柄不自动重选）。
//
// 探针不修改任何第三方窗口；目标窗口的移动/最大化/最小化/遮挡/关闭由
// 外部驱动脚本在其自建的测试窗口上执行。
//
// 文件（scenarioDir）：
//   window-perch-live-config.json  { targetTitleSubstr, durationSec }
//   perch-live-events.jsonl        事件流（select/landed/follow/detached/
//                                  occluded-detach/drag-preempted/paused-detach）
//   perch-live-state.json          心跳状态（驱动读取；宠物外框物理像素、
//                                  当前目标、拖动取消句柄）
//   perch-live-commands.txt        命令流（驱动追加行：select / quit）
//   perch-live-summary.json        汇总（事件计数 + 终态）
//
// 坐标：虚拟桌面物理像素（PMv2），允许负值。场景开头设置 Per-Monitor V2
// 并在真实 WPF Application 创建之前生效（Program.Main 先例）。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Windows;

namespace DesktopPet.DiagnosticProbe;

public static partial class Program
{
    private sealed record WindowPerchLiveConfig(string TargetTitleSubstr, int DurationSec);

    private static void WindowPerchLiveVerification(string scenarioDir)
    {
        // ---- 配置 ----
        var configFile = Path.Combine(scenarioDir, "window-perch-live-config.json");
        if (!File.Exists(configFile))
        {
            WriteResult(1, $"window-perch-live: config not found: {configFile}");
            Environment.Exit(1);
            return;
        }

        WindowPerchLiveConfig config;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configFile));
            var root = doc.RootElement;
            config = new WindowPerchLiveConfig(
                root.GetProperty("targetTitleSubstr").GetString() ?? "",
                root.TryGetProperty("durationSec", out var d) ? d.GetInt32() : 420);
        }
        catch (Exception ex)
        {
            WriteResult(1, $"window-perch-live: invalid config: {ex.Message}");
            Environment.Exit(1);
            return;
        }

        if (string.IsNullOrEmpty(config.TargetTitleSubstr))
        {
            WriteResult(1, "window-perch-live: targetTitleSubstr must not be empty.");
            Environment.Exit(1);
            return;
        }

        // ---- 隔离启动 + 真实宠物窗口（生产路径） ----
        s_app!.TestSetupIsolatedSave(scenarioDir);
        s_app.TestState!.Pets = new List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 }
        };
        s_app.CreatePetWindow(s_app.TestState!.Pets[0]);

        var mainWindow = s_app.MainWindow;
        if (mainWindow is null)
        {
            WriteResult(1, "window-perch-live: MainWindow is null after CreatePetWindow.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }
        mainWindow.Show();

        // ---- 文件 ----
        var eventsFile = Path.Combine(scenarioDir, "perch-live-events.jsonl");
        File.WriteAllText(eventsFile, "");
        var stateFile = Path.Combine(scenarioDir, "perch-live-state.json");
        var commandsFile = Path.Combine(scenarioDir, "perch-live-commands.txt");
        File.WriteAllText(commandsFile, "");

        var stopwatch = Stopwatch.StartNew();
        var selfPid = (uint)Environment.ProcessId;
        var counts = new Dictionary<string, int>();
        var lastEvent = "";
        string HwndHex(long v) => "0x" + v.ToString("X");

        var petHwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;

        // ---- 事件流 ----
        mainWindow.Perch.PerchEvent += ev =>
        {
            counts[ev.Type] = counts.TryGetValue(ev.Type, out var c) ? c + 1 : 1;
            lastEvent = ev.Type;
            var line = JsonSerializer.Serialize(new
            {
                tMs = stopwatch.ElapsedMilliseconds,
                type = ev.Type,
                reason = ev.Reason,
                fraction = ev.Fraction,
                targetHwnd = ev.TargetHwnd is null ? null : HwndHex(ev.TargetHwnd.Value),
                targetBounds = ev.TargetBounds,
                foot = new[] { Math.Round(ev.FootX, 1), Math.Round(ev.FootY, 1) },
                petRectPx = PetRect(),
                petFootPx = PetFoot(),
            });
            File.AppendAllText(eventsFile, line + "\n");
            Console.WriteLine($"[P5A001] t={stopwatch.ElapsedMilliseconds}ms {ev.Type} {ev.Reason ?? ""}");

            // P5A-002 F-03 取证：接管处理前后各写一条带时间戳的角度记录。
            // drag-preempted 事件在 CancelForDrag 内同步发出，此刻 EndPerchWobble
            // 尚未执行（DetachInternal 在 Emit 之后）——pre 记录捕获接管前角度；
            // post 记录经 Dispatcher 异步排队，在 CancelForDrag 完整返回后执行，
            // 捕获接管完成时刻角度。验证非零角度在接管完成时归零。
            if (ev.Type == "drag-preempted")
            {
                void WriteAngleRecord(string recordType)
                {
                    var rec = JsonSerializer.Serialize(new
                    {
                        tMs = stopwatch.ElapsedMilliseconds,
                        type = recordType,
                        wobbling = mainWindow.IsPerchWobbling,
                        wobbleAngle = Math.Round(mainWindow.PerchWobbleAngleDeg, 3),
                    });
                    File.AppendAllText(eventsFile, rec + "\n");
                    Console.WriteLine($"[P5A001] t={stopwatch.ElapsedMilliseconds}ms {recordType} wobbling={mainWindow.IsPerchWobbling} angle={mainWindow.PerchWobbleAngleDeg}");
                }
                WriteAngleRecord("preempt-angle-pre");
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Send,
                    () => WriteAngleRecord("preempt-angle-post"));
            }
        };

        // ---- 只读采集（探针自有 interop，与 App 采集层同源） ----
        var pidNameCache = new Dictionary<uint, string>();
        string ResolveProcessName(uint pid)
        {
            if (pidNameCache.TryGetValue(pid, out var cached)) return cached;
            string name;
            try { name = Process.GetProcessById((int)pid).ProcessName; }
            catch { name = ""; }
            pidNameCache[pid] = name;
            return name;
        }

        WindowPerchInterop.RECT PetRectRaw()
        {
            _ = WindowPerchInterop.GetVisibleFrameBounds(petHwnd);
            return WindowPerchInterop.GetVisibleFrameBounds(petHwnd);
        }

        int[] PetRect() => new[] { PetRectRaw().Left, PetRectRaw().Top, PetRectRaw().Right, PetRectRaw().Bottom };
        double[] PetFoot()
        {
            var r = PetRectRaw();
            return new[] { (r.Left + r.Right) / 2.0, r.Bottom };
        }

        ExternalWindowSnapshot? FindEligibleByTitle() =>
            WindowPerchInterop.EnumerateTopLevelWindows(ResolveProcessName).FirstOrDefault(s =>
                s.Title.Contains(config.TargetTitleSubstr, StringComparison.OrdinalIgnoreCase)
                && ExternalWindowEligibility.Evaluate(s, selfPid) == ExternalWindowExclusion.None);

        bool TrySelect()
        {
            var candidate = FindEligibleByTitle();
            if (candidate is null)
                return false;
            return mainWindow.Perch.TrySelectExplicit(candidate, selfPid);
        }

        // ---- 心跳状态（驱动读取） ----
        void WriteState()
        {
            var perch = mainWindow.Perch;
            var state = new
            {
                tMs = stopwatch.ElapsedMilliseconds,
                perchActive = perch.IsActive,
                targetHwnd = perch.TargetHwndValue is null ? null : HwndHex(perch.TargetHwndValue.Value),
                dragCancelledHwnd = perch.DragCancelledHwndValue is null ? null : HwndHex(perch.DragCancelledHwndValue.Value),
                petRectPx = PetRect(),
                petFootPx = PetFoot(),
                lastEvent,
                wobbling = mainWindow.IsPerchWobbling,
                wobbleAngle = Math.Round(mainWindow.PerchWobbleAngleDeg, 3),
                perchPickActive = mainWindow.IsPerchPickActive,
                perchPickStatus = mainWindow.LastPerchPickStatus,
                perchPickAwaitRelease = mainWindow.IsPerchPickAwaitingRelease,
                perchPickLeftDown = mainWindow.LastPerchPickLeftDown,
            };
            // 驱动并发读取 stateFile（Get-Content）会短暂持有句柄，MoveFile 可能
            // 抛 UnauthorizedAccessException；心跳写入失败只跳过本轮，不崩溃探针。
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.WriteAllText(stateFile + ".tmp", JsonSerializer.Serialize(state));
                    File.Move(stateFile + ".tmp", stateFile, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= 2)
                        return; // 三次仍失败：跳过本心跳，下轮 200ms 后重试
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        // ---- 命令流（驱动追加：select / quit） ----
        long commandOffset = 0;
        bool quit = false;
        void PollCommands()
        {
            using var fs = new FileStream(commandsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(commandOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            var content = reader.ReadToEnd();
            commandOffset = fs.Position;
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line == "select")
                {
                    _ = TrySelect();
                }
                else if (line == "pick-entry")
                {
                    // P5A-003: drive the production menu entry (same
                    // BeginInvoke dispatch path as the native menu).
                    mainWindow.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdPerchOnWindow);
                }
                else if (line == "leave-entry")
                {
                    mainWindow.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdLeavePerch);
                }
                else if (line.StartsWith("menu-entry ", StringComparison.Ordinal))
                {
                    // P5A-003 R1 F-02: open the REAL native context menu
                    // (production TrackPopupMenuEx popup) at the given
                    // physical screen point. Blocks until the driver's
                    // injected mouse input selects/dismisses an item;
                    // command dispatch is the production path.
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3
                        && int.TryParse(parts[1], out var menuX)
                        && int.TryParse(parts[2], out var menuY))
                    {
                        mainWindow.TestShowNativeContextMenu(menuX, menuY);
                    }
                }
                else if (line == "quit")
                {
                    quit = true;
                }
            }
        }

        // ---- 汇总与退出 ----
        void WriteSummaryAndExit(int exitCode, string message)
        {
            var summary = new
            {
                scenario = "window-perch-live",
                targetTitleSubstr = config.TargetTitleSubstr,
                durationSec = config.DurationSec,
                selfPid,
                counts,
                lastEvent,
                perchActive = mainWindow.Perch.IsActive,
                petRectPx = PetRect(),
                petFootPx = PetFoot(),
                wobblingAtExit = mainWindow.IsPerchWobbling,
                wobbleAngleAtExit = Math.Round(mainWindow.PerchWobbleAngleDeg, 3),
            };
            File.WriteAllText(
                Path.Combine(scenarioDir, "perch-live-summary.json"),
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            s_app?.DiagnosticLogger?.Dispose();
            WriteResult(exitCode, message);
            Environment.Exit(exitCode);
        }

        // ---- 主循环（Dispatcher 泵 + 低频轮询） ----
        var pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        pollTimer.Tick += (_, _) =>
        {
            PollCommands();
            WriteState();
            if (quit || stopwatch.Elapsed.TotalSeconds > config.DurationSec)
            {
                pollTimer.Stop();
                WriteSummaryAndExit(quit ? 0 : 1,
                    quit ? "window-perch-live: quit command received."
                         : "window-perch-live: duration timeout.");
            }
        };
        pollTimer.Start();

        Dispatcher.Run();

        WriteSummaryAndExit(1, "window-perch-live: Dispatcher.Run returned (unexpected).");
    }
}
