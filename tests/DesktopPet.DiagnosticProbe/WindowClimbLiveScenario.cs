// P5B-001（R-08）: window-climb-live 场景（窗口有限攀爬隔离预览）。
//
// 真实宠物窗口（生产 CreatePetWindow 路径）+ 真实外部测试窗口（驱动脚本
// 以独立进程启动）。驱动通过命令流显式起攀（按标题找合格快照），
// 宠物沿「靠近目标可见外框侧边 → 爬升到上缘 → 顶部落位脚点」折线路线
// 爬行，完成即交接栖息（TrySelectExplicit，落位点一致无可见跳动）。
// 路线推进由 MainWindow 的 OnUpdateTick 生产路径驱动（探针只观测）。
//
// 目标窗口的移动/最小化/关闭/遮挡由外部驱动脚本在其自建测试窗口上
// 执行；探针不修改任何第三方窗口。用户拖动/暂停由驱动注入真实输入。
//
// 文件（scenarioDir）：
//   window-climb-live-config.json  { targetTitleSubstr, durationSec }
//   climb-live-events.jsonl        事件流（climb-started/climb-step/
//                                  climb-occluded/climb-completed[-no-handoff]/
//                                  climb-cancelled(reason)/climb-drag-preempted/
//                                  start-rejected(result)）
//   climb-live-state.json          心跳状态（驱动读取）
//   climb-live-commands.txt        命令流（驱动追加行：start/pause/resume/quit）
//   climb-live-summary.json        汇总（事件计数 + 终态）
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
    private sealed record WindowClimbLiveConfig(string TargetTitleSubstr, int DurationSec);

    private static void WindowClimbLiveVerification(string scenarioDir)
    {
        // ---- 配置 ----
        var configFile = Path.Combine(scenarioDir, "window-climb-live-config.json");
        if (!File.Exists(configFile))
        {
            WriteResult(1, $"window-climb-live: config not found: {configFile}");
            Environment.Exit(1);
            return;
        }

        WindowClimbLiveConfig config;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configFile));
            var root = doc.RootElement;
            config = new WindowClimbLiveConfig(
                root.GetProperty("targetTitleSubstr").GetString() ?? "",
                root.TryGetProperty("durationSec", out var d) ? d.GetInt32() : 420);
        }
        catch (Exception ex)
        {
            WriteResult(1, $"window-climb-live: invalid config: {ex.Message}");
            Environment.Exit(1);
            return;
        }

        if (string.IsNullOrEmpty(config.TargetTitleSubstr))
        {
            WriteResult(1, "window-climb-live: targetTitleSubstr must not be empty.");
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
            WriteResult(1, "window-climb-live: MainWindow is null after CreatePetWindow.");
            s_app?.DiagnosticLogger?.Dispose();
            Environment.Exit(1);
            return;
        }
        mainWindow.Show();

        // ---- 文件 ----
        var eventsFile = Path.Combine(scenarioDir, "climb-live-events.jsonl");
        File.WriteAllText(eventsFile, "");
        var stateFile = Path.Combine(scenarioDir, "climb-live-state.json");
        var commandsFile = Path.Combine(scenarioDir, "climb-live-commands.txt");
        File.WriteAllText(commandsFile, "");

        var stopwatch = Stopwatch.StartNew();
        var selfPid = (uint)Environment.ProcessId;
        var counts = new Dictionary<string, int>();
        var lastEvent = "";
        string HwndHex(long v) => "0x" + v.ToString("X");

        var petHwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;

        // ---- 事件流（攀爬事件） ----
        mainWindow.Climb.ClimbEvent += ev =>
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
                foot = new[] { ev.FootX, ev.FootY },
            });
            File.AppendAllText(eventsFile, line + "\n");
            Console.WriteLine($"[P5B001] t={stopwatch.ElapsedMilliseconds}ms {ev.Type} {ev.Reason ?? ""}");
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

        // ---- 命令流（驱动追加：start / pause / resume / quit） ----
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
                if (line == "start")
                {
                    // 显式起攀：按标题找合格快照 → 生产 TryStart（含目标校验/
                    // 起攀距离/顶部落点遮挡）。结果写入事件流供驱动断言。
                    var snapshot = FindEligibleByTitle();
                    // F-02：起攀时间源必须与生产 Tick 同一单调钟
                    // （MonotonicClock.Instance）——探针生造的 Stopwatch 起点
                    // 与生产不同，会让首步 dt 失真（曾实测 t=1289 起攀、
                    // t=1300 首步 11ms 却位移 91.95px）。日志时间戳仍用
                    // stopwatch，仅作观测尺度。
                    var result = snapshot is null
                        ? PetClimbStartResult.RejectedIneligible
                        : mainWindow.Climb.TryStart(snapshot, selfPid, MainWindow.GetElapsedMsPublic());
                    var rec = JsonSerializer.Serialize(new
                    {
                        tMs = stopwatch.ElapsedMilliseconds,
                        type = "start-rejected",
                        result = result.ToString(),
                        targetHwnd = snapshot is null ? null : HwndHex(snapshot.HwndValue),
                    });
                    if (result != PetClimbStartResult.Started)
                    {
                        File.AppendAllText(eventsFile, rec + "\n");
                        Console.WriteLine($"[P5B001] t={stopwatch.ElapsedMilliseconds}ms start-rejected {result}");
                    }
                }
                else if (line == "pause")
                {
                    mainWindow.PauseAnimation();
                }
                else if (line == "resume")
                {
                    mainWindow.ResumeAnimation();
                }
                else if (line == "quit")
                {
                    quit = true;
                }
            }
        }

        // ---- 心跳状态（驱动读取） ----
        void WriteState()
        {
            var climb = mainWindow.Climb;
            var state = new
            {
                tMs = stopwatch.ElapsedMilliseconds,
                climbActive = climb.IsActive,
                progress = Math.Round(climb.ProgressFraction, 4),
                climbTargetHwnd = climb.TargetHwndValue is null ? null : HwndHex(climb.TargetHwndValue.Value),
                perchActive = mainWindow.Perch.IsActive,
                perchTargetHwnd = mainWindow.Perch.TargetHwndValue is null ? null : HwndHex(mainWindow.Perch.TargetHwndValue.Value),
                petRectPx = PetRect(),
                petFootPx = PetFoot(),
                lastEvent,
                isDragging = mainWindow.IsDragging,
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

        // ---- 汇总与退出 ----
        void WriteSummaryAndExit(int exitCode, string message)
        {
            var summary = new
            {
                scenario = "window-climb-live",
                targetTitleSubstr = config.TargetTitleSubstr,
                durationSec = config.DurationSec,
                selfPid,
                counts,
                lastEvent,
                climbActive = mainWindow.Climb.IsActive,
                perchActive = mainWindow.Perch.IsActive,
                petRectPx = PetRect(),
                petFootPx = PetFoot(),
            };
            File.WriteAllText(
                Path.Combine(scenarioDir, "climb-live-summary.json"),
                JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            s_app?.DiagnosticLogger?.Dispose();
            WriteResult(exitCode, message);
            Environment.Exit(exitCode);
        }

        // ---- 主循环（Dispatcher 泵 + 低频轮询；攀爬推进由 MainWindow
        //      的生产 OnUpdateTick 计时器驱动，探针只观测） ----
        var pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        pollTimer.Tick += (_, _) =>
        {
            PollCommands();
            WriteState();
            if (quit || stopwatch.Elapsed.TotalSeconds > config.DurationSec)
            {
                pollTimer.Stop();
                WriteSummaryAndExit(quit ? 0 : 1,
                    quit ? "window-climb-live: quit command received."
                         : "window-climb-live: duration timeout.");
            }
        };
        pollTimer.Start();

        Dispatcher.Run();

        WriteSummaryAndExit(1, "window-climb-live: Dispatcher.Run returned (unexpected).");
    }
}
