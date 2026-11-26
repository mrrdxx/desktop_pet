using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace DesktopPet.App.Tests;

/// <summary>
/// P6-001：App 层本地统计（UsageStatsService）专项测试 + App 隔离接线测试。
/// <para>
/// 时间策略：monotonic 时钟由注入 collector 的 <c>nowMs</c> 委托控制（每
/// tick 推进 5s，缺口/跨日/保留期由 Core 假时钟测试覆盖，此处不重复真实时间
/// 推进）；输入年龄由覆写 <see cref="InputIdleSampler"/> tick 域的假采样器控制；
/// 前台由覆写 <see cref="ForegroundAppSampler.TryGetForegroundAppId"/> 的假
/// 采样器控制。唯一的真实时钟用例是"输入年龄 &gt;60s 关闭当前段"，用
/// MonotonicClock.Instance + 短 Sleep 保证段内时长为正即可。
/// </para>
/// <para>
/// 覆盖（对应交付要求）：默认关闭不产生数据；启停持久化；输入空闲不归属；
/// 识别失败 drop+count；锁屏/睡眠/暂停关段、解锁/唤醒记独立事件；禁用保留
/// 期限内旧数据；清除落盘清空 + 不复生（generation 观察面）；重启恢复已保存
/// 段；清除后重启不恢复；App 接线转发、开关持久化（settings.json）、清除不触
/// 宠物存档。
/// </para>
/// </summary>
[TestClass]
public class UsageStatsServiceTests
{
    /// <summary>生产采样间隔（5s），测试每 tick 推进的量。</summary>
    private const double SampleIntervalMs = 5000.0;

    /// <summary>固定本地日期：2026-11-25 正午（+08:00），段/事件的日期归属确定。</summary>
    private static readonly DateTimeOffset s_fixedNow =
        new(2026, 11, 25, 12, 0, 0, TimeSpan.FromHours(8));

    // ── 测试双（受控 idle / 受控前台） ─────────────────────────────────

    /// <summary>
    /// 受控输入空闲采样器：在 GetTickCount（uint）域注入 last-input 与
    /// current-tick。两者相同 → 年龄 0（活跃）；current &gt; last 且差 &gt;60000
    /// → 空闲。返回 null 表示"输入时间未知"——在 F-02 语义下它是缺测
    /// （每 tick 重置基线、零累计），所以 Harness 默认必须给活跃值，只有
    /// 显式 SetIdle 才把 CurrentTick 推到阈值后。
    /// <para>
    /// F-02 时钟域修正：Service 采样时把 <see cref="MonotonicClock.Instance"/>
    /// 的 now 传给输入采样器，而 collector 用受控时钟——两者域不一致时，
    /// 全量运行（进程已启动数十秒）下的真实 now 会被 collector 的"未来值
    /// 拒绝"判定（lastInputMs &gt; nowMs）当成未来/缺测，导致零累计。本假件
    /// 覆写 <see cref="GetLastInputMs"/>，把 now 基准换成受控
    /// <see cref="NowMs"/>（Harness 绑定到 <see cref="Harness.TickMs"/>，
    /// 与 collector 时钟同域），年龄仍在 uint tick 域计算，语义与基类
    /// 等价但不依赖真实时钟。
    /// </summary>
    private sealed class MutableIdleSampler : InputIdleSampler
    {
        public uint? LastInput = 0;
        public uint? CurrentTick = 0;

        /// <summary>单调 now 基准（毫秒，与 collector 时钟同域的受控值）。</summary>
        public Func<double> NowMs = () => 0;

        internal override uint? GetLastInputTickMs() => LastInput;
        internal override uint? GetCurrentTickMs() => CurrentTick;

        /// <summary>
        /// 与 collector 同域的年龄换算：年龄在 uint tick 域计算，now 基准
        /// 用受控 <see cref="NowMs"/>（基类用入参 monotonicNow，测试下它是
        /// 真实时钟，会造成域不一致）。
        /// </summary>
        public override double? GetLastInputMs(double monotonicNow)
        {
            if (LastInput is null || CurrentTick is null)
                return null;

            // 与基类相同的 uint 无符号减法（处理 ~49.7 天回绕）。
            uint ageMs;
            unchecked
            {
                ageMs = CurrentTick.Value - LastInput.Value;
            }

            const ulong MaxReliableAgeMs = 49UL * 24UL * 60UL * 60UL * 1000UL;
            if ((ulong)ageMs > MaxReliableAgeMs)
                return null;

            return NowMs() - ageMs;
        }
    }

    /// <summary>受控前台采样器：直接给出应用 id 或识别失败标志。</summary>
    private sealed class FakeForeground : ForegroundAppSampler
    {
        public string? AppId;
        public bool ResolutionFailed;
        public int CallCount;

        public override string? TryGetForegroundAppId(out bool resolutionFailed)
        {
            CallCount++;
            resolutionFailed = ResolutionFailed;
            return AppId;
        }
    }

    // ── 服务夹具 ────────────────────────────────────────────────────────

    /// <summary>
    /// 组装一个受控 UsageStatsService：collector 使用本类推进的
    /// <see cref="TickMs"/>（受控单调时间）与固定本地日期；idle/foreground
    /// 为受控假件；dispatcher=null（生命周期调用同步执行）；autoStartTimer=false
    /// （不启动真实 5s 定时器，仅 <see cref="Tick"/> 驱动）。统计文件独立落在
    /// 传入目录，与宠物存档（state.json/settings.json）隔离。
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public readonly string Dir;
        public double TickMs;

        /// <summary>
        /// 默认活跃（LastInput = CurrentTick = 0 → 年龄 0）。F-02 语义下
        /// null=缺测会每 tick 重置基线、永远无段，所以 Harness 默认不得为
        /// null；要制造空闲必须显式 SetIdle。NowMs 在构造函数中绑定到
        /// 受控 TickMs（与 collector 时钟同域，避免真实时钟 now 被判定为
        /// 未来值）。
        /// </summary>
        public readonly MutableIdleSampler Idle = new() { LastInput = 0, CurrentTick = 0 };

        public readonly FakeForeground Foreground = new();
        public readonly UsageStatsService Service;
        private readonly bool _deleteDirOnDispose;

        public Harness(string? dir = null, Func<double>? clock = null, Func<DateTimeOffset>? localNow = null, bool deleteDir = true)
        {
            _deleteDirOnDispose = deleteDir;
            Dir = dir ?? Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            // now 基准与 collector 时钟同域（受控 TickMs），见 MutableIdleSampler 注释。
            Idle.NowMs = () => TickMs;
            var collector = new UsageStatsCollector(
                clock ?? (() => TickMs),
                localNow ?? (() => s_fixedNow));
            Service = new UsageStatsService(
                collector, Idle, Foreground, MonotonicClock.Instance,
                dispatcher: null, saveDirectory: Dir, autoStartTimer: false,
                localNow: localNow ?? (() => s_fixedNow));
        }

        public string StatsPath => Service.StatsFilePath;

        /// <summary>推进一个采样间隔并手动采样（镜像生产 5s tick）。</summary>
        public void Tick(string? appId, bool resolutionFailed = false)
        {
            TickMs += SampleIntervalMs;
            Foreground.AppId = appId;
            Foreground.ResolutionFailed = resolutionFailed;
            Service.SampleTickManually();
        }

        /// <summary>仅推进单调时钟（不改前台/不采样），用于生命周期关段前制造正时长。</summary>
        public void Advance() => TickMs += SampleIntervalMs;

        /// <summary>空闲：输入年龄相对 <paramref name="activeTick"/> 的当前 tick。</summary>
        public void SetActive(uint tick = 0x1000) => Idle.LastInput = Idle.CurrentTick = tick;

        public void SetIdle(uint activeTick = 0x1000)
        {
            Idle.LastInput = activeTick;
            Idle.CurrentTick = activeTick + (uint)(UsageStatsCollector.DefaultInputAgeThresholdMs + 1);
        }

        public void Dispose()
        {
            Service.Dispose();
            if (!_deleteDirOnDispose) return;
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }
            catch { /* best-effort temp cleanup */ }
        }
    }

    private static UsageStatsArchive? ReadStats(string statsPath)
    {
        if (!File.Exists(statsPath)) return null;
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.Deserialize<UsageStatsArchive>(File.ReadAllText(statsPath), opts);
    }

    // ── 1. 默认关闭 ─────────────────────────────────────────────────────

    /// <summary>
    /// R-09：统计默认关闭。未 Enable 前多次采样不产生任何段/事件/不匹配计数，
    /// 也不落盘 stats.json。
    /// </summary>
    [STATestMethod]
    public void DisabledByDefault_NeverSamples_NoStatsFile()
    {
        using var h = new Harness();
        h.SetActive();
        h.Tick("notepad");
        h.Tick("notepad");
        h.Tick("chrome");
        Assert.IsFalse(h.Service.IsEnabled, "默认必须关闭。");
        Assert.AreEqual(0, h.Service.Segments.Count);
        Assert.AreEqual(0, h.Service.Events.Count);
        Assert.AreEqual(0UL, h.Service.CoverageMismatchCount);
        Assert.IsFalse(File.Exists(h.StatsPath), "默认关闭不得创建 stats.json。");
    }

    // ── 2. 启用 → 采样 → 落盘（schemaVersion=1，与应用 id 归一化） ──────

    /// <summary>
    /// 启用后多 tick 采样，切换应用时已确认区间落盘：stats.json 独立存在、
    /// schemaVersion=1、段的应用 id 为规范化基名、时长为受控单调差。F-02：
    /// 段只按"相邻成功 tick 的已确认区间"逐步提交——两次 notepad 提交
    /// [5s,10s)+[10s,15s) 两段，chrome 切来后仅作新基线（未确认尾部不提交）。
    /// </summary>
    [STATestMethod]
    public void Enable_SampleSwitchApp_PersistsSegmentWithSchemaVersion1()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();
        Assert.IsTrue(h.Service.IsEnabled);

        h.Tick("notepad"); // 5000 → notepad 基线
        h.Tick("notepad"); // 10000 → 已确认提交 notepad [5s,10s)
        h.Tick("chrome");  // 15000 → 已确认提交 notepad [10s,15s)；chrome 新基线

        Assert.AreEqual(2, h.Service.Segments.Count, "两个已确认区间各自成段。");
        Assert.AreEqual("notepad", h.Service.Segments[0].AppId);
        Assert.AreEqual(5000.0, h.Service.Segments[0].DurationMs, 0.001);
        Assert.AreEqual("notepad", h.Service.Segments[1].AppId);
        Assert.AreEqual(5000.0, h.Service.Segments[1].DurationMs, 0.001,
            "段时长必须是单调差，非墙钟差。");
        Assert.AreEqual("2026-11-25", h.Service.Segments[0].Date);
        Assert.AreEqual(0, h.Service.Events.Count, "前台观测不产生生命周期事件。");

        var arc = ReadStats(h.StatsPath);
        Assert.IsNotNull(arc, "stats.json 必须落盘。");
        Assert.AreEqual(UsageStatsArchive.CurrentSchemaVersion, arc!.SchemaVersion);
        Assert.AreEqual(2, arc.Segments.Count);
        Assert.AreEqual("notepad", arc.Segments[0].AppId);
        Assert.IsTrue(File.Exists(h.StatsPath), "stats.json 独立存在于统计目录。");
    }

    // ── 3. 输入年龄 >60s：空闲不归属 ───────────────────────────────────

    /// <summary>
    /// 输入年龄超过阈值后未确认尾部被丢弃（不归属也不补计）；持续空闲不产生
    /// 新段；恢复活跃后从新基线再开段。使用真实时钟域（MonotonicClock.Instance）
    /// + 短 Sleep，保证第一个已确认段时长 &gt;0。
    /// F-02：段只在相邻成功 tick 间提交——需要两次活跃 tick 才有一段。
    /// </summary>
    [STATestMethod]
    public void InputAgeBeyondThreshold_ClosesSegment_IdleNotAttributed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var idle = new MutableIdleSampler
            {
                LastInput = 0x1000,
                CurrentTick = 0x1000,
                // 本测试刻意用真实时钟域（collector 同样用真实时钟），
                // NowMs 绑定真实时钟保持两域一致。
                NowMs = () => MonotonicClock.Instance.GetElapsedMs(),
            };
            var fg = new FakeForeground { AppId = "notepad" };
            var collector = new UsageStatsCollector(
                () => MonotonicClock.Instance.GetElapsedMs(), () => s_fixedNow);
            using var svc = new UsageStatsService(
                collector, idle, fg, MonotonicClock.Instance,
                dispatcher: null, saveDirectory: dir, autoStartTimer: false);

            svc.Enable();
            svc.SampleTickManually(); // 活跃 → notepad 基线
            System.Threading.Thread.Sleep(25);
            svc.SampleTickManually(); // 活跃 → 已确认提交一段（>0ms）
            Assert.AreEqual(1, svc.Segments.Count, "两次活跃 tick 必须提交一段。");
            Assert.AreEqual("notepad", svc.Segments[0].AppId);
            Assert.IsTrue(svc.Segments[0].DurationMs > 0.0, "已确认段时长保持。");

            idle.CurrentTick = 0x1000 + (uint)(UsageStatsCollector.DefaultInputAgeThresholdMs + 1);
            svc.SampleTickManually(); // 空闲 → 未确认尾部丢弃，无新段
            Assert.AreEqual(1, svc.Segments.Count, "空闲不得产生新段。");

            svc.SampleTickManually(); // 仍空闲 → 无新段
            Assert.AreEqual(1, svc.Segments.Count, "持续空闲不得产生新段。");

            idle.CurrentTick = idle.LastInput = 0x1000; // 恢复活跃
            svc.SampleTickManually(); // 新基线
            svc.SampleTickManually(); // 相邻成功 → 新段
            Assert.AreEqual(2, svc.Segments.Count, "恢复后新段经两次成功 tick 提交。");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    // ── 4. 前台识别失败：drop + count ──────────────────────────────────

    /// <summary>
    /// 前台存在但无法识别（识别失败）：区段丢弃且 CoverageMismatchCount 计数
    /// （单一策略，无 unknown 分支），并持久化到 stats.json。
    /// </summary>
    [STATestMethod]
    public void ForegroundResolutionFailed_DropsIntervalAndCountsMismatch()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();
        h.Tick(appId: null, resolutionFailed: true);
        h.Tick(appId: null, resolutionFailed: true);

        Assert.AreEqual(2UL, h.Service.CoverageMismatchCount);
        Assert.AreEqual(0, h.Service.Segments.Count, "识别失败的区间不得归属任何应用。");
        var arc = ReadStats(h.StatsPath);
        Assert.IsNotNull(arc);
        Assert.AreEqual(2UL, arc!.CoverageMismatchCount, "计数必须持久化。");
    }

    // ── 5. 生命周期：锁屏/睡眠/暂停关段；解锁/唤醒记独立事件 ─────────────

    /// <summary>
    /// 生命周期：lock(7)/suspend(3)/pause(true) 各自挂起（期间 tick 零累计、
    /// 不记事件）；unlock(8)/resume(1) 各记独立事件并解除对应原因（其他原因
    /// 仍挂起时保持挂起）；app-start 记独立事件。四类事件互不混用（无 boot）。
    /// </summary>
    [STATestMethod]
    public void Lifecycle_LockSuspendPauseCloseSegment_UnlockResumeRecordDistinctEvents()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();

        h.Tick("notepad"); h.Tick("notepad"); // 1 个已确认段
        h.Service.OnSessionSwitch(7); // lock → 挂起
        Assert.AreEqual(1, h.Service.Segments.Count);
        Assert.AreEqual(0, h.Service.Events.Count, "锁屏不得记事件。");
        h.Tick("notepad"); h.Tick("notepad");
        Assert.AreEqual(1, h.Service.Segments.Count, "锁屏挂起期间不得累计。");

        h.Service.OnSessionSwitch(8); // unlock → 解除 lock 并记 unlock
        Assert.AreEqual(1, h.Service.Events.Count);
        Assert.AreEqual(UsageStatsEventTypes.Unlock, h.Service.Events[0].EventType);
        h.Tick("notepad"); h.Tick("notepad");
        Assert.AreEqual(2, h.Service.Segments.Count, "解除后相邻成功 tick 提交新段。");

        h.Service.OnPowerModeChanged(1); // resume → 记 resume（无 sleep 原因，仅事件）
        Assert.AreEqual(2, h.Service.Events.Count);
        Assert.AreEqual(UsageStatsEventTypes.Resume, h.Service.Events[1].EventType,
            "unlock 与 resume 必须分开记录。");

        h.Service.OnPowerModeChanged(3); // suspend → 挂起（sleep 原因）
        Assert.AreEqual(2, h.Service.Segments.Count);
        Assert.AreEqual(2, h.Service.Events.Count, "睡眠不得记事件。");
        h.Tick("notepad"); h.Tick("notepad");
        Assert.AreEqual(2, h.Service.Segments.Count, "睡眠挂起期间不得累计。");

        h.Service.OnPauseToggled(true); // 暂停 → 追加原因，仍挂起
        Assert.AreEqual(2, h.Service.Segments.Count);
        Assert.AreEqual(2, h.Service.Events.Count, "暂停不得记事件。");

        h.Service.OnAppStart(); // 桌宠自身启动 → 独立事件（enable 时）
        Assert.AreEqual(3, h.Service.Events.Count);
        Assert.AreEqual(UsageStatsEventTypes.AppStart, h.Service.Events[2].EventType);

        h.Service.DrainPending(); // app-start 只入 pending，需 drain 才落盘
        var arc = ReadStats(h.StatsPath);
        Assert.IsNotNull(arc);
        Assert.AreEqual(3, arc!.Events.Count, "事件必须持久化。");
        CollectionAssert.AreEquivalent(
            new[] { UsageStatsEventTypes.AppStart, UsageStatsEventTypes.Unlock, UsageStatsEventTypes.Resume },
            arc.Events.Select(e => e.EventType).ToArray(),
            "事件类型必须恰好为 app-start/unlock/resume 三者，且互不混用。");
    }

    // ── 6. 禁用保留期限内旧数据 ────────────────────────────────────────

    /// <summary>
    /// Disable 停止新采集（不再开新段/新事件），保留期限内旧数据；禁用后
    /// 采样不产生任何新数据，且保留期清理在禁用期间仍会执行（此处验证旧数据
    /// 保留与禁用后零新增）。
    /// </summary>
    [STATestMethod]
    public void Disable_KeepsOldData_StopsNewCollection()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();
        h.Tick("notepad"); h.Tick("notepad");
        h.Service.OnPauseToggled(true); // 关闭当前段并落盘
        Assert.AreEqual(1, h.Service.Segments.Count);

        h.Service.Disable();
        Assert.IsFalse(h.Service.IsEnabled);
        Assert.AreEqual(1, h.Service.Segments.Count, "禁用必须保留期限内旧数据。");

        h.Tick("chrome");
        Assert.AreEqual(1, h.Service.Segments.Count, "禁用后不得再采集。");
        Assert.AreEqual(0, h.Service.Events.Count);

        var arc = ReadStats(h.StatsPath);
        Assert.IsNotNull(arc, "禁用也会执行保留期清理 + 落盘。");
        Assert.AreEqual(1, arc!.Segments.Count, "旧数据随禁用落盘保留。");
    }

    // ── 7. 清除：落盘清空 + 不复生（generation 观察面） ─────────────────

    /// <summary>
    /// ClearAll 立即清除段/事件/不匹配计数并落盘空快照，generation 递增；
    /// 清除后采集的新数据是新数据（旧段不复生）。generation guard 的观察面
    /// 是：清除后不存在任何旧代写回。
    /// </summary>
    [STATestMethod]
    public void ClearAll_EmptiesArchive_NewDataDoesNotResurrectOld()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();
        int genBefore = h.Service.Generation;

        h.Tick("notepad"); h.Tick("notepad"); h.Tick("chrome"); // 2 个已确认 notepad 段
        Assert.AreEqual(2, h.Service.Segments.Count);
        Assert.AreEqual(10000.0, h.Service.Segments.Sum(s => s.DurationMs), 0.001);

        h.Service.ClearAll();
        Assert.IsTrue(h.Service.Generation > genBefore, "清除必须递增 generation。");
        Assert.AreEqual(0, h.Service.Segments.Count);
        Assert.AreEqual(0, h.Service.Events.Count);
        Assert.AreEqual(0UL, h.Service.CoverageMismatchCount);

        var cleared = ReadStats(h.StatsPath);
        Assert.IsNotNull(cleared, "清除必须立即持久化清空快照。");
        Assert.AreEqual(0, cleared!.Segments.Count);
        Assert.AreEqual(0, cleared.Events.Count);

        h.Tick("edge"); h.Tick("edge"); h.Tick("vscode"); // 清除后新段是新数据
        Assert.AreEqual(2, h.Service.Segments.Count);
        Assert.IsTrue(h.Service.Segments.All(s => s.AppId == "edge"), "旧 notepad 段不得复生。");

        var after = ReadStats(h.StatsPath);
        Assert.AreEqual(2, after!.Segments.Count);
        Assert.IsTrue(after.Segments.All(s => s.AppId == "edge"), "落盘内容也必须只有清除后的新数据。");
    }

    // ── 7b. 清除损坏副本与临时副本（F-03 反例） ─────────────────────────

    /// <summary>
    /// F-03 监理反例「Clear removes preserved historical payload」等价
    /// 场景：预写损坏 stats.json（Start 时被保留为 .corrupted）并残留
    /// .corrupted/.tmp 副本后，ClearAll 必须删除本存储的两种精确路径副本
    /// （不扩大范围），并立即落盘清空快照；目录中不得遗留任何含旧哨兵
    /// 内容的文件（清除后旧内容无法继续存活）。
    /// </summary>
    [STATestMethod]
    public void ClearAll_RemovesCorruptedAndTempSideArtifacts_OnlyThisStorage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var statsPath = Path.Combine(dir, "stats.json");
            File.WriteAllText(statsPath, "old-sentinel-garbage-not-json");
            File.WriteAllText(statsPath + ".corrupted", "stale-corrupted-sentinel");
            File.WriteAllText(statsPath + ".tmp", "stale-tmp-sentinel");

            using (var h = new Harness(dir))
            {
                h.Service.Start(); // TryLoadArchive：损坏 → 保留为 .corrupted（替换旧残留）
                h.Service.ClearAll(); // 先删 .corrupted/.tmp，再落盘清空快照

                Assert.IsTrue(File.Exists(h.StatsPath), "清除后必须落盘新的空快照。");
                var arc = ReadStats(h.StatsPath);
                Assert.IsNotNull(arc);
                Assert.AreEqual(0, arc!.Segments.Count);
                Assert.AreEqual(0, arc.Events.Count);
                Assert.AreEqual(0UL, arc.CoverageMismatchCount);

                var files = Directory.GetFiles(dir);
                Assert.IsFalse(
                    files.Any(f => f.EndsWith(".corrupted", StringComparison.OrdinalIgnoreCase)),
                    "损坏副本（.corrupted）必须随清除删除。");
                Assert.IsFalse(
                    files.Any(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)),
                    "临时副本（.tmp）必须随清除删除。");
                Assert.IsFalse(
                    files.Any(f => File.ReadAllText(f).Contains("sentinel", StringComparison.Ordinal)),
                    "清除后不得残留任何含旧内容的文件。");
            }
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    // ── 7c. 写盘异常隔离与失败可见（F-04 反例） ─────────────────────────

    /// <summary>
    /// F-04 监理反例「Optional statistics I/O failure contained」等价
    /// 场景：有效采样后把 stats.json 替换为同名目录使写盘失败——失败不得
    /// 抛给调用方（Disable 照常完成），LastWriteError 可见且段仍保留在
    /// 内存；移除障碍后下次 drain 强制重试成功并清空错误状态（有界重试，
    /// 不静默丢弃）。清除之外的文件保持不变（不扩大到任意目录）。
    /// </summary>
    [STATestMethod]
    public void StatsWriteFailure_IsContainedVisible_AndRecoversOnNextDrain()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();

        h.Tick("notepad"); h.Tick("notepad"); // 1 个已确认段，落盘成功
        Assert.AreEqual(1, h.Service.Segments.Count);
        Assert.IsNull(h.Service.LastWriteError, "正常写盘不得残留错误状态。");

        // 制造写失败：stats.json 改为同名目录，SafeWrite 的 Move 必然失败。
        File.Delete(h.StatsPath);
        Directory.CreateDirectory(h.StatsPath);

        h.Tick("notepad"); // 相邻成功 tick → 提交段2 → drain → 写失败但不得抛出
        Assert.AreEqual(2, h.Service.Segments.Count, "写失败不得影响内存提交。");
        Assert.IsNotNull(h.Service.LastWriteError, "写失败必须通过 LastWriteError 暴露。");
        var firstError = h.Service.LastWriteError;

        h.Tick("notepad"); // 又提交段3 → 仍失败
        Assert.AreEqual(3, h.Service.Segments.Count);
        Assert.IsNotNull(h.Service.LastWriteError);

        // Disable 走同一条 drain 路径：障碍未移除 → 仍失败但绝不抛出。
        h.Service.Disable();
        Assert.IsFalse(h.Service.IsEnabled);
        Assert.AreEqual(3, h.Service.Segments.Count, "禁用保留期限内旧数据。");
        Assert.IsNotNull(h.Service.LastWriteError, "失败状态在障碍期间保持可见。");

        // 移除障碍 → 下次 drain（_persistRetryPending 强制重试）恢复并清错。
        Directory.Delete(h.StatsPath); // 同名目录为障碍，先移除
        h.Service.Enable();
        h.Tick("notepad"); // 重建基线；_persistRetryPending 强制重试 → 本次 drain 即恢复并清错
        Assert.IsNull(h.Service.LastWriteError, "障碍移除后下次 drain 必须重试成功并清空错误。");
        Assert.AreNotEqual(firstError, h.Service.LastWriteError ?? "", "错误状态已复位。");
        Assert.AreEqual(3, h.Service.Segments.Count, "重试本身不新增段。");
        h.Tick("notepad"); // 相邻成功 tick → 提交段4
        Assert.AreEqual(4, h.Service.Segments.Count);

        var recovered = ReadStats(h.StatsPath);
        Assert.IsNotNull(recovered, "恢复后 stats.json 必须重新落盘。");
        Assert.AreEqual(4, recovered!.Segments.Count, "失败期间提交的段必须随重试补写。");
        Assert.IsFalse(Directory.Exists(h.StatsPath), "stats.json 必须是普通文件而非目录。");
    }

    // ── 7d. R2 边界：挂起期间不读前台采样器（F-01 收尾） ───────────────

    /// <summary>
    /// R2 监理反例「does not read foreground」等价场景：挂起（pause）后
    /// 采样 tick 不得再读前台采样器（调用计数不增），段零累计、已验证总时长
    /// 保持；DrainPending 保留期清理仍照常执行（无数据变化时仅无文件写入）。
    /// </summary>
    [STATestMethod]
    public void Suspended_DoesNotReadForegroundSampler_KeepsVerifiedDuration()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();

        h.Tick("notepad"); // 5000 → 基线
        h.Tick("notepad"); // 10000 → 提交 [0,5000)
        Assert.AreEqual(1, h.Service.Segments.Count);
        Assert.AreEqual(5000.0, h.Service.Segments.Sum(s => s.DurationMs), 0.001);
        int callsWhileActive = h.Foreground.CallCount;
        Assert.IsTrue(callsWhileActive >= 2, "启用采样期间必须读前台采样器。");

        h.Service.OnPauseToggled(true);
        h.Tick("notepad");
        h.Tick("notepad");
        Assert.AreEqual(callsWhileActive, h.Foreground.CallCount,
            "挂起期间不得读前台/空闲采样器（F-01 R2 门控在读取之前）。");
        Assert.AreEqual(1, h.Service.Segments.Count, "挂起期间不得累计。");
        Assert.AreEqual(5000.0, h.Service.Segments.Sum(s => s.DurationMs), 0.001,
            "已验证时长必须保持。");
    }

    // ── 7e. R2 边界：清除副件删除失败可见 + 有界重试闭环（F-03/F-04 收尾） ──

    /// <summary>
    /// R2 监理反例「clear incomplete not visible」等价场景：.corrupted 副件
    /// 被锁（FileShare.Read）时 ClearAll 必须让失败可见（LastWriteError 非空）、
    /// 被锁副件不得被强删、stats.json 仍落盘清空快照且不得掩盖副件删除失败；
    /// 解锁后下一次 drain 自动重试删除成功并清错误状态——有界重试闭环，不静默
    /// 留下损坏副本。
    /// </summary>
    [STATestMethod]
    public void ClearAll_SideArtifactLocked_FailureVisible_RetriedOnDrain()
    {
        using var h = new Harness();
        h.SetActive();
        File.WriteAllText(h.StatsPath, "old-sentinel-garbage-not-json");
        h.Service.Start(); // TryLoadArchive：损坏 → 保留为 .corrupted
        var corruptedPath = h.StatsPath + ".corrupted";
        Assert.IsTrue(File.Exists(corruptedPath), "前置：损坏副本必须已保留。");
        int genBefore = h.Service.Generation;

        using (new FileStream(corruptedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // 持有读锁：他人不能覆盖/删除该副件（锁在 using 块作用域内）。
            h.Service.ClearAll();

            Assert.IsTrue(h.Service.Generation > genBefore, "清除必须递增 generation。");
            Assert.IsNotNull(h.Service.LastWriteError,
                "副件删除失败必须通过 LastWriteError 可见（不能只写日志）。");
            Assert.IsTrue(File.Exists(corruptedPath), "被锁副件在解锁前不得被删除。");

            // stats.json 本身正常落盘清空快照——但不能掩盖副件删除失败。
            var cleared = ReadStats(h.StatsPath);
            Assert.IsNotNull(cleared, "清除必须落盘空快照。");
            Assert.AreEqual(0, cleared!.Segments.Count);
            Assert.IsNotNull(h.Service.LastWriteError,
                "落盘成功不得清掉仍在等待重试的副件删除失败。");
        }

        // 解锁后：下一次 drain 自动重试删除成功并清空错误状态。
        h.Service.DrainPending();
        Assert.IsFalse(File.Exists(corruptedPath), "解锁后重试必须删除损坏副本。");
        Assert.IsNull(h.Service.LastWriteError, "重试成功后必须清除错误状态。");
    }

    // ── 8. 重启恢复已保存统计 ──────────────────────────────────────────

    /// <summary>
    /// 重启（同目录重建服务并 Start → TryLoadArchive → Restore）恢复已保存
    /// 的段与事件：进程重启后保留期限内旧统计仍在（R-09：关闭采集时旧数据也
    /// 保留，这里验证启用期间数据的重启恢复）。
    /// </summary>
    [STATestMethod]
    public void Restart_RestoresPreviouslySavedStats()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            using (var h1 = new Harness(dir, deleteDir: false)) // 重启语义：保留目录与统计文件
            {
                h1.SetActive();
                h1.Service.Enable();
                h1.Service.OnAppStart();
                h1.Tick("notepad"); h1.Tick("notepad"); h1.Tick("chrome"); // 2 个已确认 notepad 段
                Assert.AreEqual(2, h1.Service.Segments.Count);
                Assert.AreEqual(10000.0, h1.Service.Segments.Sum(s => s.DurationMs), 0.001);
                Assert.AreEqual(1, h1.Service.Events.Count);
                Assert.IsTrue(File.Exists(path));
            } // dispose → Stop 落盘

            using (var h2 = new Harness(dir))
            {
                h2.Service.Start(); // 重启恢复
                Assert.AreEqual(2, h2.Service.Segments.Count, "重启必须恢复已保存段。");
                Assert.AreEqual("notepad", h2.Service.Segments[0].AppId);
                Assert.AreEqual(1, h2.Service.Events.Count);
                Assert.AreEqual(UsageStatsEventTypes.AppStart, h2.Service.Events[0].EventType);

                var arc = ReadStats(path);
                Assert.AreEqual(2, arc!.Segments.Count, "恢复后文件内容仍然完整。");
            }
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    // ── 9. 清除后重启不恢复 ────────────────────────────────────────────

    /// <summary>
    /// 清除后重启（Restore 有意不恢复已清除历史）：新服务从空快照开始，
    /// 不出现任何已清除的段/事件。
    /// </summary>
    [STATestMethod]
    public void ClearThenRestart_DoesNotRestoreClearedHistory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            using (var h1 = new Harness(dir, deleteDir: false)) // 重启语义：保留目录与统计文件
            {
                h1.SetActive();
                h1.Service.Enable();
                h1.Tick("notepad"); h1.Tick("notepad"); h1.Tick("chrome"); // 2 个已确认 notepad 段
                Assert.AreEqual(2, h1.Service.Segments.Count);
                h1.Service.ClearAll();
                Assert.AreEqual(0, h1.Service.Segments.Count);
            }

            using (var h2 = new Harness(dir))
            {
                h2.Service.Start();
                Assert.AreEqual(0, h2.Service.Segments.Count, "清除后重启不得恢复旧数据。");
                Assert.AreEqual(0, h2.Service.Events.Count);
                Assert.AreEqual(0UL, h2.Service.CoverageMismatchCount);
            }
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    // ── 10. 应用 id 归一化（单一口径） ─────────────────────────────────

    /// <summary>
    /// 归一化：仅规范化可执行文件基名（小写、无扩展名、无路径/PID/标题），
    /// 空输入返回空字符串（不猜测归属）。
    /// </summary>
    [TestMethod]
    public void NormalizeAppId_KeepsOnlyLowercasedBaseName()
    {
        Assert.AreEqual("notepad", ForegroundAppSampler.NormalizeAppId(@"C:\Windows\System32\NOTEPAD.EXE"));
        Assert.AreEqual("code", ForegroundAppSampler.NormalizeAppId(@"C:\Program Files\Microsoft VS Code\Code.exe"));
        Assert.AreEqual("desktoppet", ForegroundAppSampler.NormalizeAppId(@"C:\Apps\DesktopPet.exe"));
        Assert.AreEqual("", ForegroundAppSampler.NormalizeAppId(""));
        Assert.AreEqual("", ForegroundAppSampler.NormalizeAppId(null!), "识别失败不猜测归属。");
    }

    // ════════════════════════════════════════════════════════════════════
    // App 隔离接线（TestSetupIsolatedSave + SetUsageStatsServiceForTest）
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// App 系统/暂停事件转发：OnPauseToggled/OnSystemSessionSwitch/
    /// OnSystemPowerModeChanged 均转发到注入的统计服务（pause/lock/suspend
    /// 挂起期间 tick 零累计且不记事件；unlock/resume 各自记独立事件）；
    /// 不向 MainWindow 更新循环堆逻辑。
    /// </summary>
    [STATestMethod]
    public void AppWiring_SystemAndPauseLifecycle_ForwardedToStatsService()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        Harness? h = null;
        try
        {
            h = new Harness();
            app.TestSetupIsolatedSave(h.Dir);
            app.SetUsageStatsServiceForTest(h.Service);
            h.SetActive();
            h.Service.Enable();

            h.Tick("notepad"); h.Tick("notepad"); // 1 个已确认段
            Assert.AreEqual(1, h.Service.Segments.Count);

            app.OnPauseToggled(null, true);
            Assert.AreEqual(1, h.Service.Segments.Count);
            Assert.AreEqual(0, h.Service.Events.Count, "暂停不得记事件。");

            // 挂起期间采样零累计（F-01 门控：暂停原因未解除前不采集）。
            h.Tick("notepad"); h.Tick("notepad");
            Assert.AreEqual(1, h.Service.Segments.Count, "挂起期间不得累计。");

            app.OnPauseToggled(null, false); // 解除暂停
            app.OnSystemSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));
            Assert.AreEqual(1, h.Service.Events.Count);
            Assert.AreEqual(UsageStatsEventTypes.Unlock, h.Service.Events[0].EventType);

            app.OnSystemPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));
            Assert.AreEqual(2, h.Service.Events.Count);
            Assert.AreEqual(UsageStatsEventTypes.Resume, h.Service.Events[1].EventType);

            // 解锁/唤醒后新基线：相邻成功 tick 提交新段。
            h.Tick("notepad"); h.Tick("notepad");
            Assert.AreEqual(2, h.Service.Segments.Count);

            app.OnSystemSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock));
            Assert.AreEqual(2, h.Service.Segments.Count);
            Assert.AreEqual(2, h.Service.Events.Count, "锁屏不得记事件。");
            h.Tick("notepad"); h.Tick("notepad");
            Assert.AreEqual(2, h.Service.Segments.Count, "锁屏挂起期间不得累计。");

            app.OnSystemPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Suspend));
            Assert.AreEqual(2, h.Service.Segments.Count);
            Assert.AreEqual(2, h.Service.Events.Count, "睡眠不得记事件。");
            h.Tick("notepad"); h.Tick("notepad");
            Assert.AreEqual(2, h.Service.Segments.Count, "睡眠挂起期间不得累计。");
        }
        finally
        {
            app.TestCleanup();
            h?.Dispose();
        }
    }

    /// <summary>
    /// 开关持久化 + 关闭保留旧数据：SetUsageStatsEnabled(true/false) 同时
    /// 驱动统计服务启停并写 settings.json（usageStatsEnabled 布尔值）；默认
    /// 关闭；关闭后采样零新增但旧数据保留；repo 开关重启后仍关闭（文件中
    /// 滞留 false）。
    /// </summary>
    [STATestMethod]
    public void AppWiring_SetUsageStatsEnabled_PersistsToggleAndDisableKeepsData()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        Harness? h = null;
        try
        {
            h = new Harness();
            app.TestSetupIsolatedSave(h.Dir);
            app.SetUsageStatsServiceForTest(h.Service);
            h.SetActive();

            Assert.IsFalse(app.IsUsageStatsEnabled, "统计必须默认关闭。");

            app.SetUsageStatsEnabled(true);
            Assert.IsTrue(app.IsUsageStatsEnabled);
            Assert.IsTrue(h.Service.IsEnabled);
            app.TestSaveCoordinator!.Flush();
            Assert.IsTrue(ReadSettingsFlag(app.TestArchive!.SettingsFilePath), "settings.json 必须持久化开关=true。");

            h.Tick("notepad"); h.Tick("notepad"); h.Tick("chrome");
            Assert.AreEqual(2, h.Service.Segments.Count);
            Assert.AreEqual(10000.0, h.Service.Segments.Sum(s => s.DurationMs), 0.001);

            app.SetUsageStatsEnabled(false);
            Assert.IsFalse(app.IsUsageStatsEnabled);
            Assert.IsFalse(h.Service.IsEnabled);
            Assert.AreEqual(2, h.Service.Segments.Count, "关闭必须保留期限内旧数据。");
            h.Tick("edge");
            Assert.AreEqual(2, h.Service.Segments.Count, "关闭后采样不得新增。");

            app.TestSaveCoordinator!.Flush();
            Assert.IsFalse(ReadSettingsFlag(app.TestArchive!.SettingsFilePath), "settings.json 必须持久化开关=false。");
        }
        finally
        {
            app.TestCleanup();
            h?.Dispose();
        }
    }

    /// <summary>
    /// 清除入口：ClearUsageStats 删除统计与待写缓存（stats.json 清空），保留
    /// 开关与配置（settings.json 不变），不触碰宠物存档（state.json 内容不变），
    /// 清除后新采集不复生旧数据。
    /// </summary>
    [STATestMethod]
    public void AppWiring_ClearUsageStats_KeepsToggle_DoesNotTouchPetArchive()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        Harness? h = null;
        try
        {
            h = new Harness();
            app.TestSetupIsolatedSave(h.Dir);
            app.SetUsageStatsServiceForTest(h.Service);
            h.SetActive();

            // 宠物存档先落一份带宠物的 state.json。
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.TestSaveCoordinator!.MarkStateDirty(app.TestState);
            app.TestSaveCoordinator!.Flush();
            var stateBefore = File.ReadAllText(app.TestArchive!.StateFilePath);
            Assert.IsTrue(stateBefore.Contains("bear-1"), "前置：宠物存档必须存在。");

            app.SetUsageStatsEnabled(true);
            app.TestSaveCoordinator!.Flush();
            var settingsBefore = File.ReadAllText(app.TestArchive.SettingsFilePath);

            h.Tick("notepad"); h.Tick("notepad"); h.Tick("chrome");
            Assert.AreEqual(2, h.Service.Segments.Count);
            Assert.AreEqual(10000.0, h.Service.Segments.Sum(s => s.DurationMs), 0.001);

            app.ClearUsageStats();
            Assert.AreEqual(0, h.Service.Segments.Count);
            Assert.AreEqual(0, h.Service.Events.Count);
            Assert.AreEqual(0UL, h.Service.CoverageMismatchCount);
            Assert.IsTrue(app.IsUsageStatsEnabled, "清除必须保留开关（统计仍开启）。");
            Assert.IsTrue(h.Service.IsEnabled, "清除不得关闭采集开关。");

            var clearedArchive = ReadStats(h.StatsPath);
            Assert.AreEqual(0, clearedArchive!.Segments.Count, "清除必须落盘清空统计。");

            app.TestSaveCoordinator!.Flush();
            var settingsAfter = File.ReadAllText(app.TestArchive.SettingsFilePath);
            var stateAfter = File.ReadAllText(app.TestArchive.StateFilePath);
            Assert.AreEqual(settingsBefore, settingsAfter, "清除不得改动 settings.json（开关/配置保留）。");
            Assert.AreEqual(stateBefore, stateAfter, "清除不得触碰宠物存档 state.json。");

            // 清除后新数据是新数据，旧段不复生。
            h.Tick("edge"); h.Tick("edge"); h.Tick("vscode");
            Assert.AreEqual(2, h.Service.Segments.Count);
            Assert.IsTrue(h.Service.Segments.All(s => s.AppId == "edge"), "旧 notepad 段不得复生。");
        }
        finally
        {
            app.TestCleanup();
            h?.Dispose();
        }
    }

    /// <summary>
    /// R2 监理反例「paused start + default-off → enabling samples at once」
    /// 等价场景（走生产初始化路径）：统计默认关闭且全局暂停早于统计初始化
    /// （暂停启动）；初始化后服务必须保持挂起；用户在暂停期间开启统计时，
    /// 关闭不可解除挂起——采样门控在读取采样器之前就生效（F-01 R2 关键），
    /// 解除暂停后才恢复采样能力。
    /// </summary>
    [STATestMethod]
    public void AppWiring_PausedStart_ToggleOff_EnablingKeepsSuspended()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        var app = (App?)System.Windows.Application.Current ?? new App();
        try
        {
            Directory.CreateDirectory(dir);
            app.TestSetupIsolatedSave(dir); // 统计默认关闭 + _isPaused=false

            // 模拟“全局暂停启动”：暂停状态在统计初始化前已生效（验证器语义）。
            typeof(App).GetField("_isPaused", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(app, true);

            // 生产初始化路径（验证器同样经反射调用）。内部重建统计服务。
            typeof(App).GetMethod("InitializeUsageStats", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, new object?[] { dir });
            var service = app.UsageStatsService;
            Assert.IsNotNull(service, "初始化必须创建统计服务。");
            var collector = (UsageStatsCollector)typeof(UsageStatsService)
                .GetField("_collector", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(service!)!;
            Assert.IsTrue(collector.IsSuspended, "暂停启动时初始化后必须先挂起。");
            Assert.IsFalse(collector.IsEnabled, "统计默认关闭（开关未开启）。");

            // 开启统计：不解除挂起，不立即采样累计。
            app.SetUsageStatsEnabled(true);
            Assert.IsTrue(collector.IsEnabled, "开关开启后采集使能。");
            Assert.IsTrue(collector.IsSuspended,
                "全局暂停未解除前，开启统计必须保持挂起，不得立即采样（F-01 R2）。");

            // 解除暂停 → 挂起解除，恢复采样能力。
            app.OnPauseToggled(null, false);
            Assert.IsFalse(collector.IsSuspended, "解除全局暂停后挂起解除。");
        }
        finally
        {
            app.TestCleanup();
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    // ── 11. P6-002 聚合桶：落盘 / 重启不重计 / 关闭时清理 / 清除 ─────────

    /// <summary>
    /// 聚合桶必须随原始段一同幂等落盘，重启后恢复且不被重新计次（不重计）。
    /// </summary>
    [STATestMethod]
    public void Buckets_PersistedAndRestored_NoDoubleCountOnRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            using (var h1 = new Harness(dir, deleteDir: false))
            {
                h1.SetActive();
                h1.Service.Enable();
                h1.Tick("notepad"); h1.Tick("notepad"); h1.Tick("notepad"); // 2 段 = 10000ms
                Assert.AreEqual(1, h1.Service.Buckets.Count, "同一桶内的连续会话只产生一个桶。");
                var b1 = h1.Service.Buckets[0];
                Assert.AreEqual("notepad", b1.AppId);
                Assert.AreEqual(10000.0, b1.ActiveMs, 0.001);
                Assert.AreEqual(1, b1.SessionCount, "连续会话计 1 次使用。");

                var arc = ReadStats(path);
                Assert.IsNotNull(arc);
                Assert.AreEqual(1, arc!.Buckets.Count, "聚合桶必须落盘 stats.json。");
                Assert.AreEqual(1, arc.Buckets[0].SessionCount);
            } // dispose → Stop 落盘

            using (var h2 = new Harness(dir))
            {
                h2.Service.Start(); // 重启恢复
                Assert.AreEqual(1, h2.Service.Buckets.Count, "重启必须恢复已保存桶。");
                Assert.AreEqual(1, h2.Service.Buckets[0].SessionCount,
                    "恢复的桶不得因重启被重新计次。");
                Assert.AreEqual(10000.0, h2.Service.Buckets[0].ActiveMs, 0.001);
            }
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// 关闭采集时仍按被统计日期清理过期聚合桶（不等待原始记录过期）。
    /// </summary>
    [STATestMethod]
    public void Buckets_ExpiredByCountedDate_PrunedWhileDisabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            // 预置一个远超 30 天聚合保留期的桶（s_fixedNow = 2026-11-25）。
            var seed = new UsageStatsArchive();
            seed.Buckets.Add(new UsageStatsBucket
            {
                AppId = "oldapp",
                Date = "2026-09-01",
                BucketStartLocal = "2026-09-01T09:00:00.0000000+08:00",
                ActiveMs = 5000,
                SessionCount = 1
            });
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            File.WriteAllText(path, JsonSerializer.Serialize(seed, opts));

            using var h = new Harness(dir);
            h.Service.Start();
            Assert.IsFalse(h.Service.IsEnabled, "默认关闭。");
            Assert.AreEqual(1, h.Service.Buckets.Count, "Start 先恢复已保存桶。");

            h.Service.Disable(); // 关闭时同一条 drain：prune + 落盘
            Assert.AreEqual(0, h.Service.Buckets.Count, "关闭时过期聚合桶仍须清理。");
            var after = ReadStats(path);
            Assert.AreEqual(0, after!.Buckets.Count, "清理结果必须落盘，不等原始过期。");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// 清除统计必须同时清空聚合桶并落盘，且开启开关保留（不触碰宠物存档）。
    /// </summary>
    [STATestMethod]
    public void Buckets_ClearedWithStats_ToggleKept()
    {
        using var h = new Harness();
        h.SetActive();
        h.Service.Enable();
        h.Tick("notepad"); h.Tick("notepad"); h.Tick("notepad");
        Assert.AreEqual(1, h.Service.Buckets.Count);

        var genBefore = h.Service.Generation;
        h.Service.ClearAll();

        Assert.IsTrue(h.Service.Generation > genBefore);
        Assert.AreEqual(0, h.Service.Buckets.Count, "清除必须清空聚合桶。");
        Assert.IsTrue(h.Service.IsEnabled, "清除保留采集开关。");
        var cleared = ReadStats(h.StatsPath);
        Assert.AreEqual(0, cleared!.Buckets.Count, "清除后的聚合桶必须落盘为空。");
    }

    private static bool ReadSettingsFlag(string settingsPath)
    {
        Assert.IsTrue(File.Exists(settingsPath), "settings.json 必须已落盘。");
        using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return doc.RootElement.TryGetProperty("usageStatsEnabled", out var v) && v.GetBoolean();
    }

    // ── 12. P6-003 频次判断：门槛 / 关闭 / 清除 / 重启 ──────────────────

    /// <summary>P6-003：预置聚合桶写入 stats.json（供服务重算预测）。</summary>
    private static void WriteBuckets(string statsPath, params UsageStatsBucket[] buckets)
    {
        var archive = new UsageStatsArchive { Buckets = buckets.ToList() };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(statsPath, JsonSerializer.Serialize(archive, opts));
    }

    /// <summary>P6-003：构造一个 +08:00 本地的半小时桶。</summary>
    private static UsageStatsBucket Bucket(
        string appId, string date, int hour, int minute,
        double activeMs = 5000, int sessionCount = 1)
        => new()
        {
            AppId = appId,
            Date = date,
            BucketStartLocal = $"{date}T{hour:D2}:{minute:D2}:00.0000000+08:00",
            ActiveMs = activeMs,
            SessionCount = sessionCount
        };

    /// <summary>
    /// P6-003：满足门槛时输出内部候选（应用/时段/支持日期数/会话数），
    /// 且重启后由恢复的桶重算得到相同结果（不恢复旧预测、不误判）。
    /// 固定本地日期 2026-11-25：窗口 2026-10-26～2026-11-24。
    /// </summary>
    [STATestMethod]
    public void Frequency_EnoughData_ReturnsCandidate_AndRecalculatedAfterRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            var seed = new List<UsageStatsBucket>();
            for (var day = 10; day <= 14; day++) // 5 个不同日期 @ 09:30
                seed.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30));
            seed.Add(Bucket("chrome", "2026-11-15", 14, 0)); // 覆盖日期补到 7 个
            seed.Add(Bucket("chrome", "2026-11-16", 14, 0));
            WriteBuckets(path, seed.ToArray());

            using (var h1 = new Harness(dir, deleteDir: false))
            {
                h1.Service.Start();
                h1.Service.Enable();

                var result = h1.Service.GetFrequencyPredictions();
                Assert.IsTrue(result.IsEnabled);
                Assert.AreEqual(7, result.ObservedDateCount, "窗口内 7 个不同日期有记录。");
                Assert.AreEqual(1, result.Candidates.Count, "只有 notepad/09:30 满足门槛。");
                var c = result.Candidates[0];
                Assert.AreEqual("notepad", c.AppId);
                Assert.AreEqual("09:30", c.SlotStart);
                Assert.AreEqual(5, c.SupportDateCount);
                Assert.AreEqual(5, c.SessionCount);
            }

            using (var h2 = new Harness(dir))
            {
                h2.Service.Start(); // 重启仅恢复桶，预测重算
                h2.Service.Enable();
                var again = h2.Service.GetFrequencyPredictions();
                Assert.AreEqual(1, again.Candidates.Count, "重启后结果必须由桶重算，不恢复旧预测。");
                Assert.AreEqual("notepad", again.Candidates[0].AppId);
                Assert.AreEqual(5, again.Candidates[0].SupportDateCount);
            }
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// P6-003：单一全局开关语义——默认关闭时不输出预测；开启后输出；
    /// 再次关闭后立即为空（不保留预测缓存）。
    /// </summary>
    [STATestMethod]
    public void Frequency_Disabled_NoPrediction_ImmediatelyEmptyAfterDisable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            var seed = new List<UsageStatsBucket>();
            for (var day = 10; day <= 14; day++)
                seed.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30));
            seed.Add(Bucket("chrome", "2026-11-15", 14, 0));
            seed.Add(Bucket("chrome", "2026-11-16", 14, 0));
            WriteBuckets(path, seed.ToArray());

            using var h = new Harness(dir);
            h.Service.Start();

            var off = h.Service.GetFrequencyPredictions();
            Assert.IsFalse(off.IsEnabled, "默认关闭。");
            Assert.IsTrue(off.IsSuppressed, "关闭时不得输出预测。");
            Assert.AreEqual(0, off.Candidates.Count);

            h.Service.Enable();
            Assert.AreEqual(1, h.Service.GetFrequencyPredictions().Candidates.Count);

            h.Service.Disable();
            var afterOff = h.Service.GetFrequencyPredictions();
            Assert.IsTrue(afterOff.IsSuppressed, "关闭后必须立即无预测（无缓存残留）。");
            Assert.AreEqual(0, afterOff.Candidates.Count);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// P6-003：清除统计后旧预测立即失效并落盘为空；开关保留。
    /// </summary>
    [STATestMethod]
    public void Frequency_ClearAll_MakesPredictionEmptyImmediately()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            var seed = new List<UsageStatsBucket>();
            for (var day = 10; day <= 14; day++)
                seed.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30));
            seed.Add(Bucket("chrome", "2026-11-15", 14, 0));
            seed.Add(Bucket("chrome", "2026-11-16", 14, 0));
            WriteBuckets(path, seed.ToArray());

            using var h = new Harness(dir);
            h.Service.Start();
            h.Service.Enable();
            Assert.AreEqual(1, h.Service.GetFrequencyPredictions().Candidates.Count);

            h.Service.ClearAll();

            var cleared = h.Service.GetFrequencyPredictions();
            Assert.IsTrue(cleared.IsSuppressed, "清除后旧预测必须立即失效。");
            Assert.AreEqual(0, cleared.ObservedDateCount);
            Assert.IsTrue(h.Service.IsEnabled, "清除保留采集开关。");
            Assert.AreEqual(0, ReadStats(path)!.Buckets.Count, "清除后桶必须落盘为空。");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// P6-003：窗口边界——当天（未结束）、未来、today−30 及更旧桶不计入覆盖；
    /// today−29 为有效窗口起点（2026-11-25 的有效起点为 2026-10-27）。
    /// </summary>
    [STATestMethod]
    public void Frequency_TodayAndExpiredBuckets_AreIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            WriteBuckets(path,
                Bucket("notepad", "2026-11-25", 9, 30),          // 当天：排除
                Bucket("notepad", "2026-11-26", 9, 30),          // 未来：排除
                Bucket("notepad", "2026-10-25", 9, 30),          // today−31：过期
                Bucket("notepad", "2026-10-26", 9, 30),          // today−30：超出保留期
                Bucket("chrome", "2026-10-27", 14, 0));          // today−29：有效起点

            using var h = new Harness(dir);
            h.Service.Start();
            h.Service.Enable();

            var result = h.Service.GetFrequencyPredictions();
            Assert.AreEqual(1, result.ObservedDateCount, "仅 today−29 的有效桶计入覆盖。");
            Assert.AreEqual(29, result.WindowDays,
                "有效窗口为保留期交集，排除当天后为 29 个完整日期。");
            Assert.AreEqual(0, result.Candidates.Count, "当天/未来/过期数据不得产生候选。");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// P6-003 F-01 反例：today−30 从首次查询起就被排除，不得在正常清理前补足
    /// 门槛；同一时刻生产 <see cref="UsageStatsService.DrainPending"/> 前后以及
    /// 重启后的有效样本集与候选必须一致。
    /// </summary>
    [STATestMethod]
    public void Frequency_TodayMinus30_ExcludedFromFirstQuery_StableAcrossDrainAndRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            // notepad 出现于 today−30 与 today−1..−4：含 today−30 才有 5 个支持日期。
            var seed = new List<UsageStatsBucket>
            {
                Bucket("notepad", "2026-10-26", 9, 30), // today−30：已超出保留期
                Bucket("notepad", "2026-11-24", 9, 30),
                Bucket("notepad", "2026-11-23", 9, 30),
                Bucket("notepad", "2026-11-22", 9, 30),
                Bucket("notepad", "2026-11-21", 9, 30),
                Bucket("chrome", "2026-11-20", 14, 0),  // 其他应用补覆盖日期
                Bucket("chrome", "2026-11-19", 14, 0)
            };
            WriteBuckets(path, seed.ToArray());

            using (var h1 = new Harness(dir, deleteDir: false))
            {
                h1.Service.Start();
                h1.Service.Enable();

                var before = h1.Service.GetFrequencyPredictions();
                Assert.AreEqual(6, before.ObservedDateCount,
                    "today−30 从首次查询起即排除，只剩 6 个覆盖日期。");
                Assert.AreEqual(0, before.Candidates.Count,
                    "today−30 不得在清理前补足支持日期门槛。");

                h1.Service.DrainPending(); // 生产正常清理路径（会删掉 today−30 桶）

                var after = h1.Service.GetFrequencyPredictions();
                Assert.AreEqual(6, after.ObservedDateCount, "正常清理不得改变有效覆盖日期集。");
                Assert.AreEqual(0, after.Candidates.Count, "正常清理不得改变候选。");
            }

            using (var h2 = new Harness(dir))
            {
                h2.Service.Start();
                h2.Service.Enable();
                var restarted = h2.Service.GetFrequencyPredictions();
                Assert.AreEqual(6, restarted.ObservedDateCount, "重启后有效覆盖日期集保持一致。");
                Assert.AreEqual(0, restarted.Candidates.Count, "重启后候选保持一致。");
            }
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// P6-003 F-01 正例：today−29 位于保留期内，首次查询、正常清理后与重启后
    /// 都应有效并产生相同候选。
    /// </summary>
    [STATestMethod]
    public void Frequency_TodayMinus29_ValidBeforeAndAfterDrainAndRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DP6Stats_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stats.json");
            var seed = new List<UsageStatsBucket>
            {
                Bucket("notepad", "2026-10-27", 9, 30), // today−29：有效起点
                Bucket("notepad", "2026-11-24", 9, 30),
                Bucket("notepad", "2026-11-23", 9, 30),
                Bucket("notepad", "2026-11-22", 9, 30),
                Bucket("notepad", "2026-11-21", 9, 30),
                Bucket("chrome", "2026-11-20", 14, 0),
                Bucket("chrome", "2026-11-19", 14, 0)
            };
            WriteBuckets(path, seed.ToArray());

            using (var h1 = new Harness(dir, deleteDir: false))
            {
                h1.Service.Start();
                h1.Service.Enable();

                var before = h1.Service.GetFrequencyPredictions();
                Assert.AreEqual(7, before.ObservedDateCount, "today−29 计入覆盖。");
                Assert.AreEqual(1, before.Candidates.Count, "today−29 有效，门槛满足。");
                Assert.AreEqual(5, before.Candidates[0].SupportDateCount);

                h1.Service.DrainPending(); // today−29 在保留期内，应原样保留

                var after = h1.Service.GetFrequencyPredictions();
                Assert.AreEqual(7, after.ObservedDateCount, "正常清理不得删除 today−29。");
                Assert.AreEqual(1, after.Candidates.Count, "正常清理不得改变候选。");
            }

            using (var h2 = new Harness(dir))
            {
                h2.Service.Start();
                h2.Service.Enable();
                var restarted = h2.Service.GetFrequencyPredictions();
                Assert.AreEqual(7, restarted.ObservedDateCount, "重启后 today−29 仍有效。");
                Assert.AreEqual(1, restarted.Candidates.Count, "重启后候选保持一致。");
                Assert.AreEqual("notepad", restarted.Candidates[0].AppId);
            }
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }

}