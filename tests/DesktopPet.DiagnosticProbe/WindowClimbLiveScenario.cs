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
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
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
        // R16：只用于观测的本次链标识。每次起攀（climb-started）递增并写入事件
        // JSON，链内后续事件（step/occluded/completed/cancelled 等）携带同一
        // chainId。生产正确取消路径（land 交接 tracker 释放后发 perch-detached
        // 取消）的 climb-cancelled 事件 targetHwnd=null，无法用目标句柄关联，
        // 驱动以 chainId 关联本次 land/真实拖动链（R16 监理判据）。
        var chainId = 0;
        string HwndHex(long v) => "0x" + v.ToString("X");

        var petHwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;

        // ---- 事件流（攀爬事件） ----
        mainWindow.Climb.ClimbEvent += ev =>
        {
            counts[ev.Type] = counts.TryGetValue(ev.Type, out var c) ? c + 1 : 1;
            lastEvent = ev.Type;
            // R16：chainId 每次起攀递增，链内事件共用；不依赖 ev.TargetHwnd
            //（land 拖动取消时其为 null，驱动以 chainId 关联本次链）。
            if (ev.Type == "climb-started") { chainId++; }
            var line = JsonSerializer.Serialize(new
            {
                tMs = stopwatch.ElapsedMilliseconds,
                type = ev.Type,
                // R16：本次链标识（只用于观测，随事件输出供驱动关联 land 拖动取消）。
                chainId = chainId,
                // R15：事件流同步记录触发当刻的实际 CurrentAction（与 sidecar/心跳
                // 同源），climb-started 与 climb-step 均带；mirror 不再独立承担
                // side 证据（R15 否证：mirror 初值也可能 1，不能凭 mirror 推断 side
                // 播放）；驱动以 action + mirror 共同取证左右侧段。
                action = mainWindow.CurrentAction,
                reason = ev.Reason,
                fraction = ev.Fraction,
                targetHwnd = ev.TargetHwnd is null ? null : HwndHex(ev.TargetHwnd.Value),
                targetBounds = ev.TargetBounds,
                foot = new[] { ev.FootX, ev.FootY },
                // R11 实机轮：side 段心跳在攀爬渲染高峰被饿死（DispatcherBackground
                // 轮询与宠物动画同进程共用 Dispatcher，三次实测 ③④ side 段零心跳），
                // 事件流是同步观测不经过轮询；此处仍记录当刻镜像作方向证据，
                // 但验证是否播放 side 以 action 字段为准。
                mirror = ReadClimbMirrorScaleX(),
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
                else if (line == "pick-entry")
                {
                    // P5B-002: drive the production climb menu entry (same
                    // BeginInvoke dispatch path as the native menu).
                    mainWindow.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdClimbOnWindow);
                }
                else if (line.StartsWith("menu-entry ", StringComparison.Ordinal))
                {
                    // P5B-002: open the REAL native context menu (production
                    // TrackPopupMenuEx popup) at the given physical screen
                    // point. Blocks until the driver's injected mouse input
                    // selects/dismisses an item; command dispatch is the
                    // production path.
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3
                        && int.TryParse(parts[1], out var menuX)
                        && int.TryParse(parts[2], out var menuY))
                    {
                        mainWindow.TestShowNativeContextMenu(menuX, menuY);
                    }
                }
                else if (line.StartsWith("capture ", StringComparison.Ordinal))
                {
                    // P5B-003 R9：按需渲染帧截图（120×120 默认尺寸）。
                    // 复用 ClimbPreviewScenario 的反射取 PetImage +
                    // RenderTargetBitmap 手法；落在 frames/<name>.png，
                    // 并写 frame-captured 事件供驱动断言取证。
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var name = string.Concat(parts[1].Select(ch =>
                            char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_'));
                        if (name.Length > 0)
                        {
                            var dir = Path.Combine(scenarioDir, "frames");
                            Directory.CreateDirectory(dir);
                            var petImage = PreviewGetField(mainWindow, "PetImage") as Image;
                            if (petImage is not null)
                            {
                                var capTMs = stopwatch.ElapsedMilliseconds;
                                PreviewRenderPng(petImage, Path.Combine(dir, name + ".png"));
                                // R10 F-06：sidecar 记录采集瞬间的真实动作/镜像/时间，
                                // 驱动据此证明帧捕到 climb_land（不能只靠文件名）；
                                // 字段与心跳同源（缺镜像=null，不做默认值）。
                                var sidecar = JsonSerializer.Serialize(new
                                {
                                    tMs = capTMs,
                                    action = mainWindow.CurrentAction,
                                    mirror = ReadClimbMirrorScaleX(),
                                    climbActive = mainWindow.Climb.IsActive,
                                    perchActive = mainWindow.Perch.IsActive,
                                    name,
                                });
                                File.WriteAllText(Path.Combine(dir, name + ".json"), sidecar);
                                File.AppendAllText(eventsFile, JsonSerializer.Serialize(new
                                {
                                    tMs = capTMs,
                                    type = "frame-captured",
                                    name,
                                }) + "\n");
                                Console.WriteLine($"[P5B001] t={capTMs}ms frame-captured {name} @{mainWindow.CurrentAction}/mir={ReadClimbMirrorScaleX()}");
                            }
                        }
                    }
                }
                else if (line == "recover")
                {
                    // P5B-003 R9：生产 RecoverPet（public）——攀爬/栖息/
                    // 拾取同步清理入口，与托盘/App 真实调用同一方法，
                    // 探针仅触发、不改产品实现。
                    mainWindow.RecoverPet();
                }
                else if (line == "quit")
                {
                    quit = true;
                }
            }
        }

        // ---- 动作/镜像观测（WriteState 与 capture sidecar 共用） ----
        // P5B-003 R9/R10：CurrentAction 为 public 属性直访；ClimbMirrorTransform
        // 是 XAML 生成 internal 字段（App 仅 InternalsVisibleTo
        // DesktopPet.App.Tests），探针一律反射读取 ScaleX
        // （ScaleTransform public 属性，见 ClimbPreviewScenario 先例）；
        // 反射失败置 null 以便驱动区分，不静默默认值。
        double? ReadClimbMirrorScaleX()
        {
            var mirrorField = typeof(MainWindow).GetField("ClimbMirrorTransform",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mirrorField?.GetValue(mainWindow) is ScaleTransform mirror)
                return mirror.ScaleX;
            return null;
        }

        // ---- 心跳状态（驱动读取） ----
        void WriteState()
        {
            var climb = mainWindow.Climb;
            double? climbMirrorScaleX = ReadClimbMirrorScaleX();
            var state = new
            {
                tMs = stopwatch.ElapsedMilliseconds,
                petHwnd = petHwnd.ToInt64(),
                climbActive = climb.IsActive,
                progress = Math.Round(climb.ProgressFraction, 4),
                currentAction = mainWindow.CurrentAction,
                climbMirrorScaleX,
                climbTargetHwnd = climb.TargetHwndValue is null ? null : HwndHex(climb.TargetHwndValue.Value),
                perchActive = mainWindow.Perch.IsActive,
                perchTargetHwnd = mainWindow.Perch.TargetHwndValue is null ? null : HwndHex(mainWindow.Perch.TargetHwndValue.Value),
                petRectPx = PetRect(),
                petFootPx = PetFoot(),
                lastEvent,
                isDragging = mainWindow.IsDragging,
                climbPickActive = mainWindow.IsClimbPickActive,
                climbPickStatus = mainWindow.LastPerchPickStatus,
                climbPickAwaitRelease = mainWindow.IsPerchPickAwaitingRelease,
                climbPickLeftDown = mainWindow.LastPerchPickLeftDown,
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
