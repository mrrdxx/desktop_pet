// P5A-PLAN: window-perch 场景（TESTING M-14）。
//
// 只读探针：枚举普通/可见/未最小化的外部顶层窗口，按 config 的标题
// 子串选择目标（Z 序最上），以可见外框上缘为候选栖息面做遮挡采样，
// 跟踪目标的移动/最大化/最小化/恢复/关闭。目标失效即清除；恢复或
// 同数值句柄出现时经 PerchTargetTracker 重新验证（防句柄复用）。
//
// 事件流：window-perch-events.jsonl
//   type ∈ select | update | cleared | reacquire | reacquire-denied | occluded | unoccluded
//   update 以上一次已上报外框为基线（PerchUpdateGate）：外框变化才写入，
//   移动后静止不重复上报，移回之前的位置仍会上报；
//   occluded/unoccluded 仅在遮挡状态翻转时写入。
//   cleared(WindowGone/HandleReused) 后，同数值句柄即使 HWND/PID/类名相同也不
//   自动 reacquire，也不会经原筛选条件 Reset+Select 回退重绑（R2 F-02：那不是
//   独立的新选择输入）；探针记一条 reacquire-denied 事件并保持 Cleared，
//   重新绑定只能来自明确的显式新选择（重开探针指定新目标）。
//   最小化/隐藏还原（窗口未销毁）仍自动 reacquire 同句柄。
//
// 坐标单位：虚拟桌面物理像素（DWMWA_EXTENDED_FRAME_BOUNDS 可见外框），
// 允许负坐标（左/上扩展屏）。场景开头设置 Per-Monitor V2 感知并记录
// 实际 awareness。
//
// 配置：<scenarioDir>/window-perch-config.json
//   { "targetTitleSubstr": "记事本", "durationSec": 60, "pollMs": 200 }
//
// 操作序列（移动/最大化/最小化/恢复/关闭）由外部驱动脚本执行，
// 探针只读取状态，不修改任何第三方窗口。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using DesktopPet.Core.Windows;

namespace DesktopPet.DiagnosticProbe;

public static partial class Program
{
    private sealed record WindowPerchConfig(string TargetTitleSubstr, int DurationSec, int PollMs);

    private sealed class WindowPerchRun
    {
        public int SelectCount;
        public int ClearCount;
        public int ReacquireCount;
        public int UpdateCount;
        public int OccludedCount;
        public int UnoccludedCount;
        public readonly List<string> TargetHistory = new();
        public bool EverTracked;
        public bool SameHandleDeniedLogged;
        public double LastVisibleFraction = 1.0;
    }

    private static void WindowPerchVerification(string scenarioDir)
    {
        // ---- 配置 ----
        var configFile = Path.Combine(scenarioDir, "window-perch-config.json");
        if (!File.Exists(configFile))
        {
            WriteResult(1, $"window-perch: config not found: {configFile}");
            Environment.Exit(1);
            return;
        }

        WindowPerchConfig config;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configFile));
            var root = doc.RootElement;
            config = new WindowPerchConfig(
                root.GetProperty("targetTitleSubstr").GetString() ?? "",
                root.TryGetProperty("durationSec", out var d) ? d.GetInt32() : 60,
                root.TryGetProperty("pollMs", out var p) ? p.GetInt32() : 200);
        }
        catch (Exception ex)
        {
            WriteResult(1, $"window-perch: invalid config: {ex.Message}");
            Environment.Exit(1);
            return;
        }

        if (string.IsNullOrEmpty(config.TargetTitleSubstr))
        {
            WriteResult(1, "window-perch: targetTitleSubstr must not be empty.");
            Environment.Exit(1);
            return;
        }

        // ---- DPI 感知（PMv2）：记录实际 awareness ----
        var awareness = WindowPerchInterop.TrySetPerMonitorV2Awareness();

        // ---- 显示器布局（物理像素 + 有效 DPI） ----
        var monitors = WindowPerchInterop.EnumerateMonitors();
        File.WriteAllText(
            Path.Combine(scenarioDir, "monitors.json"),
            JsonSerializer.Serialize(monitors, new JsonSerializerOptions { WriteIndented = true }));

        // ---- 事件流 ----
        var eventsFile = Path.Combine(scenarioDir, "window-perch-events.jsonl");
        File.WriteAllText(eventsFile, ""); // 清空旧内容（每次场景运行独立目录，双保险）

        var run = new WindowPerchRun();
        var stopwatch = Stopwatch.StartNew();
        var selfPid = (uint)Environment.ProcessId;
        var tracker = new PerchTargetTracker();
        // F-01：外框更新以“上一次已上报外框”为比较基线（而非选择时的初始外框）。
        var updateGate = new PerchUpdateGate();

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

        string HwndHex(long v) => "0x" + v.ToString("X");

        void WriteEvent(string type, ExternalWindowSnapshot? snap = null, string? reason = null,
            double? fraction = null, List<string>? occluders = null, int[]? bounds = null)
        {
            var ev = new Dictionary<string, object?>
            {
                ["tMs"] = stopwatch.ElapsedMilliseconds,
                ["type"] = type,
            };
            if (snap is not null)
            {
                ev["hwnd"] = HwndHex(snap.HwndValue);
                ev["title"] = snap.Title;
                ev["className"] = snap.ClassName;
                ev["pid"] = snap.ProcessId;
                ev["processName"] = snap.ProcessName;
                ev["bounds"] = bounds ?? new[] { snap.BoundsLeft, snap.BoundsTop, snap.BoundsRight, snap.BoundsBottom };
            }
            if (reason is not null) ev["reason"] = reason;
            if (fraction is not null) ev["fraction"] = Math.Round(fraction.Value, 3);
            if (occluders is not null && occluders.Count > 0) ev["occluders"] = occluders;
            File.AppendAllText(eventsFile, JsonSerializer.Serialize(ev) + "\n");
        }

        // ---- 首轮全量枚举：candidates.jsonl（每个顶层窗口一行，含筛选结论） ----
        void WriteCandidatesOnce()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var s in WindowPerchInterop.EnumerateTopLevelWindows(ResolveProcessName))
            {
                var verdict = ExternalWindowEligibility.Evaluate(s, selfPid);
                var line = JsonSerializer.Serialize(new
                {
                    hwnd = HwndHex(s.HwndValue),
                    title = s.Title,
                    className = s.ClassName,
                    pid = s.ProcessId,
                    processName = s.ProcessName,
                    verdict = verdict.ToString(),
                    visible = s.IsVisible,
                    minimized = s.IsMinimized,
                    cloaked = s.IsCloaked,
                    toolWindow = s.IsToolWindow,
                    bounds = new[] { s.BoundsLeft, s.BoundsTop, s.BoundsRight, s.BoundsBottom },
                });
                sb.AppendLine(line);
            }
            File.WriteAllText(Path.Combine(scenarioDir, "candidates.jsonl"), sb.ToString());
        }
        WriteCandidatesOnce();

        ExternalWindowSnapshot? FindEligibleByTitle(List<ExternalWindowSnapshot> snaps) =>
            snaps.FirstOrDefault(s =>
                s.Title.Contains(config.TargetTitleSubstr, StringComparison.OrdinalIgnoreCase)
                && ExternalWindowEligibility.Evaluate(s, selfPid) == ExternalWindowExclusion.None);

        static ExternalWindowSnapshot DeadSnapshot(long hwndValue) => new(
            hwndValue, "", "", 0, "", false, false, false, false, false, 0, 0, 0, 0);

        // ---- 轮询：全量快照重验 + 遮挡采样 ----
        void PollOnce()
        {
            var snaps = WindowPerchInterop.EnumerateTopLevelWindows(ResolveProcessName);

            if (tracker.Status == PerchTargetStatus.Tracking)
            {
                var target = tracker.Target!;
                var current = snaps.FirstOrDefault(s => s.HwndValue == target.HwndValue);
                // 句柄未出现在枚举中：构造 dead 快照，由 Observe 判 WindowGone。
                var observed = current ?? DeadSnapshot(target.HwndValue);
                var outcome = tracker.Observe(observed, selfPid);

                if (outcome == PerchObservationOutcome.Cleared)
                {
                    run.ClearCount++;
                    WriteEvent("cleared", observed, tracker.ClearReason.ToString());
                    return;
                }

                if (outcome == PerchObservationOutcome.Updated)
                {
                    var latest = tracker.LastObserved!;
                    if (updateGate.ShouldReport(latest.BoundsLeft, latest.BoundsTop,
                        latest.BoundsRight, latest.BoundsBottom))
                    {
                        run.UpdateCount++;
                        WriteEvent("update", latest);
                    }

                    // 上缘遮挡采样（top+2px，9 个等距点）。
                    var (fraction, occluders) = WindowPerchInterop.SampleTopEdgeOcclusion(latest, 9, _ => "");
                    if (fraction < 1.0 && run.LastVisibleFraction >= 1.0)
                    {
                        run.OccludedCount++;
                        WriteEvent("occluded", latest, fraction: fraction, occluders: occluders);
                    }
                    else if (fraction >= 1.0 && run.LastVisibleFraction < 1.0)
                    {
                        run.UnoccludedCount++;
                        WriteEvent("unoccluded", latest, fraction: fraction);
                    }
                    run.LastVisibleFraction = fraction;
                }
                return;
            }

            // NoTarget / Cleared：按标题重新选择。
            var candidate = FindEligibleByTitle(snaps);
            if (candidate is null) return;

            if (tracker.Status == PerchTargetStatus.Cleared)
            {
                // R2 F-02：WindowGone/HandleReused 后同值句柄被拒绝时，不得再用原筛选
                // 条件做 Reset+Select 自动回退（那不是独立的新选择输入）。持续轮询保持
                // Cleared；重新绑定只能来自明确的显式新选择（重开探针指定新目标）。
                var sameClearedHandle = tracker.ClearedTargetHwndValue == candidate.HwndValue;
                if (WindowPerchSelectionPolicy.DecideCleared(tracker.ClearReason, sameClearedHandle)
                    == PerchPollAction.RejectedSameHandle)
                {
                    if (!run.SameHandleDeniedLogged)
                    {
                        run.SameHandleDeniedLogged = true;
                        WriteEvent("reacquire-denied", candidate, reason: tracker.ClearReason.ToString());
                    }
                    return;
                }
                if (tracker.TryReacquire(candidate, selfPid))
                {
                    run.ReacquireCount++;
                    run.SameHandleDeniedLogged = false;
                    run.EverTracked = true;
                    run.TargetHistory.Add(HwndHex(candidate.HwndValue));
                    run.LastVisibleFraction = 1.0;
                    updateGate.Reset(candidate.BoundsLeft, candidate.BoundsTop, candidate.BoundsRight, candidate.BoundsBottom);
                    WriteEvent("reacquire", candidate);
                }
                return;
            }

            if (tracker.Status == PerchTargetStatus.NoTarget && tracker.Select(candidate, selfPid))
            {
                run.SelectCount++;
                run.EverTracked = true;
                run.TargetHistory.Add(HwndHex(candidate.HwndValue));
                updateGate.Reset(candidate.BoundsLeft, candidate.BoundsTop, candidate.BoundsRight, candidate.BoundsBottom);
                WriteEvent("select", candidate);
            }
        }

        // ---- 主循环（同步轮询，不依赖 Dispatcher；探针窗口不参与交互） ----
        while (stopwatch.Elapsed.TotalSeconds < config.DurationSec)
        {
            PollOnce();
            Thread.Sleep(config.PollMs);
        }

        // ---- 结果 ----
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        var summary = new
        {
            scenario = "window-perch",
            awareness,
            awarenessNote = "0=Unaware 1=System 2=PerMonitor (Per-Monitor V2 requested)",
            coordinateUnit = "virtual-desktop physical px (DWMWA_EXTENDED_FRAME_BOUNDS), negative allowed",
            targetTitleSubstr = config.TargetTitleSubstr,
            durationSec = config.DurationSec,
            pollMs = config.PollMs,
            monitorCount = monitors.Count,
            primaryDpi = primary?.DpiX ?? 0,
            trackerStatus = tracker.Status.ToString(),
            clearReason = tracker.ClearReason.ToString(),
            selectCount = run.SelectCount,
            reacquireCount = run.ReacquireCount,
            clearCount = run.ClearCount,
            updateCount = run.UpdateCount,
            occludedCount = run.OccludedCount,
            unoccludedCount = run.UnoccludedCount,
            targetHistory = run.TargetHistory,
            lastVisibleFraction = Math.Round(run.LastVisibleFraction, 3),
        };
        File.WriteAllText(
            Path.Combine(scenarioDir, "window-perch-result.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        var ok = run.EverTracked;
        var msg = $"window-perch: awareness={awareness}, monitors={monitors.Count}, " +
                  $"select={run.SelectCount}, reacquire={run.ReacquireCount}, cleared={run.ClearCount}, " +
                  $"updates={run.UpdateCount}, occluded={run.OccludedCount}, unoccluded={run.UnoccludedCount}";
        s_app?.DiagnosticLogger?.Dispose();
        WriteResult(ok ? 0 : 1, msg);
        Environment.Exit(ok ? 0 : 1);
    }
}
