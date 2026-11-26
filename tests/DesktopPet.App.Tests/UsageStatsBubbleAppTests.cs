using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Social;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace DesktopPet.App.Tests;

/// <summary>
/// P6-004（第一小步）：主动统计气泡与只读统计查看入口的 App 隔离测试。
/// <para>
/// 规则判定与文案已由 Core <c>UsageStatsBubblePlannerTests</c> 覆盖；这里只验证
/// App 接线与持久化边界：
/// <list type="bullet">
/// <item>仅当前半小时时段达标候选触发气泡，命中文案含统计依据、无指令/准确率；</item>
/// <item>每本地日期最多一次，去重日期落盘并在重启后继续生效；</item>
/// <item>无匹配时段时保持安静且不消费额度；</item>
/// <item>暂停/锁屏/睡眠期间不展示，恢复后只在下一 tick 重新判定（不补播、不消费额度）；</item>
/// <item>无可用宠物不展示且不消费额度；多宠只选一只可用宠物、只有一个气泡；</item>
/// <item>统计关闭立即隐藏并保留当天标记；清除立即隐藏并重置标记；</item>
/// <item>查看窗分两段展示，名称沿用 R-09 口径，无准确率/使用次数等误导名称。</item>
/// </list>
/// 全部使用隔离目录（<c>%TEMP%/P6004_*</c>），不写真实宠物存档与正式统计目录。
/// </para>
/// </summary>
[TestClass]
public class UsageStatsBubbleAppTests
{
    /// <summary>固定本地时间：2026-11-25 09:35（+08:00），时段起点 09:30。</summary>
    private static readonly DateTimeOffset s_fixedNow =
        new(2026, 11, 25, 9, 35, 0, TimeSpan.FromHours(8));

    // ── 夹具 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 受控统计服务：预置聚合桶写入隔离 stats.json，服务只负责恢复、判定与
    /// 持久化；不启动采样定时器（本类不验证采样，P6-001/002 已覆盖）。
    /// </summary>
    private sealed class StatsHarness : IDisposable
    {
        public readonly string Dir;
        public DateTimeOffset LocalNow = s_fixedNow;
        public readonly UsageStatsService Service;
        private readonly bool _deleteDirOnDispose;

        public StatsHarness(string dir, bool seed = true, bool deleteDir = true)
        {
            Dir = dir;
            _deleteDirOnDispose = deleteDir;
            Directory.CreateDirectory(Dir);
            if (seed)
                WriteBuckets(Path.Combine(Dir, "stats.json"), DefaultBuckets());

            var collector = new UsageStatsCollector(() => 0, () => LocalNow);
            Service = new UsageStatsService(
                collector,
                new InputIdleSampler(),
                new ForegroundAppSampler(),
                MonotonicClock.Instance,
                dispatcher: null,
                saveDirectory: Dir,
                autoStartTimer: false,
                localNow: () => LocalNow);
            Service.Start();
        }

        public string StatsPath => Path.Combine(Dir, "stats.json");

        public void Dispose()
        {
            Service.Dispose();
            if (!_deleteDirOnDispose) return;
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }
            catch { /* best-effort temp cleanup */ }
        }
    }

    private static App GetOrCreateApp()
        => (App?)System.Windows.Application.Current ?? new App();

    /// <summary>
    /// 关闭当前气泡窗口。App 复用单一气泡实例；若只在测试间 Hide，窗口仍
    /// 属于上一个 STA 测试线程，下一个用例复用时会触发跨线程访问异常。关闭
    /// 会通过 Closed 处理器把 App 字段置空，下一用例在本线程重新创建。
    /// </summary>
    private static void CloseStatsBubble(App app)
    {
        var bubble = app.TestStatsBubbleWindow;
        if (bubble is null) return;
        if (bubble.Dispatcher.CheckAccess())
            bubble.Close();
        else
            bubble.Dispatcher.Invoke(bubble.Close);
    }

    private static void DeleteTempDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { /* best-effort temp cleanup */ }
    }

    private static string GetTempDir(string prefix)
        => Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");

    /// <summary>
    /// 标准样本：5 个日期 @ 09:30 的 notepad（达 7/5/5 门槛），另加 2 个
    /// chrome 日期把覆盖补到 7 个有记录日期。窗口相对固定本地日期成立。
    /// </summary>
    private static UsageStatsBucket[] DefaultBuckets()
    {
        var list = new List<UsageStatsBucket>();
        for (var day = 10; day <= 14; day++)
            list.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30));
        list.Add(Bucket("chrome", "2026-11-15", 14, 0));
        list.Add(Bucket("chrome", "2026-11-16", 14, 0));
        return list.ToArray();
    }

    private static void WriteBuckets(string statsPath, params UsageStatsBucket[] buckets)
    {
        var archive = new UsageStatsArchive { Buckets = buckets.ToList() };
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(statsPath, JsonSerializer.Serialize(archive, opts));
    }

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

    private static UsageStatsArchive? ReadStats(string statsPath)
    {
        if (!File.Exists(statsPath)) return null;
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.Deserialize<UsageStatsArchive>(File.ReadAllText(statsPath), opts);
    }

    /// <summary>气泡文案必须陈述依据，禁止指令式与准确率/意图包装。</summary>
    private static void AssertEvidenceOnlyWording(string message)
    {
        Assert.IsTrue(message.Contains("过去有记录的日期里"), "文案须陈述统计依据：" + message);
        Assert.IsTrue(message.Contains("这个时段使用"), "文案须说明时段内使用：" + message);
        Assert.IsTrue(message.Contains("notepad"), "文案须包含应用标识：" + message);
        foreach (var forbidden in new[] { "你现在", "应该", "建议你", "准确率", "预测准确", "我知道你", "%" })
            Assert.IsFalse(message.Contains(forbidden), $"文案不得包含「{forbidden}」：" + message);
    }

    /// <summary>P6-004 R1 F-02：汇总在 drain / 重启前后必须完全一致。</summary>
    private static void AssertSameSummary(
        UsageStatsRecordedSummary expected, UsageStatsRecordedSummary actual, string context)
    {
        Assert.AreEqual(expected.RecordedDateCount, actual.RecordedDateCount, context + "：有记录日期数");
        Assert.AreEqual(expected.AppCount, actual.AppCount, context + "：涉及应用数");
        Assert.AreEqual(expected.SessionCount, actual.SessionCount, context + "：会话次数");
        Assert.AreEqual(expected.ApproximateForegroundActiveMs, actual.ApproximateForegroundActiveMs, 0.001,
            context + "：近似前台活跃时长");
    }

    private static void AddPets(App app, params string[] petIds)
    {
        app.TestState!.Pets = new List<PetInstanceArchive>();
        var left = 100.0;
        foreach (var petId in petIds)
        {
            app.TestState.Pets.Add(new PetInstanceArchive
            {
                PetId = petId,
                Personality = Personality.Proactive,
                Grievance = 50,
                WindowLeft = left,
                WindowTop = 100
            });
            left += 400;
        }
    }

    // ── 1. 命中当前时段 + 每日去重跨重启 ────────────────────────────────

    [STATestMethod]
    public void Bubble_ShowsForCurrentSlot_ThenDailyDedupPersistsAcrossRestart()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        StatsHarness? h2 = null;
        var dir = GetTempDir("P6004_BubbleDedup");
        try
        {
            h = new StatsHarness(dir, deleteDir: false);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-1");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);
            w!.Show();

            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible, "当前时段有达标候选时应展示气泡。");
            AssertEvidenceOnlyWording(app.TestStatsBubbleMessage);
            Assert.IsTrue(app.TestStatsBubbleMessage.Contains("09:30–09:59"),
                "文案须使用当前时段的可见区间：" + app.TestStatsBubbleMessage);

            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate);
            Assert.AreEqual("2026-11-25", ReadStats(h.StatsPath)?.BubbleLastShownLocalDate,
                "每日去重日期必须落盘 stats.json。");

            // 同日再次判定：不展示（不重复消费，也不重复提示）。
            app.HideUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible);
            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "同一本地日期最多展示一次。");

            // 重启：同一隔离目录、同一本地日期，去重日期必须读回并继续生效。
            CloseStatsBubble(app);
            app.TestCleanup();
            h.Dispose(); // deleteDir=false，保留 stats.json 供模拟重启读取
            h = null;

            h2 = new StatsHarness(dir, seed: false, deleteDir: false);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h2.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-1");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w2 = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w2);
            w2!.Show();

            Assert.AreEqual("2026-11-25", h2.Service.BubbleLastShownLocalDate,
                "重启后必须恢复去重日期。");
            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "重启后同日仍不得重复展示。");
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
            h2?.Dispose();
            DeleteTempDir(dir);
        }
    }

    // ── 2. 无匹配时段：安静，不消费额度 ────────────────────────────────

    [STATestMethod]
    public void Bubble_NoMatchingSlot_StaysQuiet_AndDoesNotConsumeQuota()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_BubbleQuiet");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            // 候选在 09:30；当前 16:20 无匹配时段。
            app.TestStatsBubbleLocalNow = () => new DateTimeOffset(2026, 11, 25, 16, 20, 0, TimeSpan.FromHours(8));

            AddPets(app, "bear-1");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.FindWindowByPetId("bear-1")!.Show();

            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "无匹配当前时段的候选时必须保持安静。");
            Assert.AreEqual("", app.TestStatsBubbleMessage, "不得输出“样本不足”等提醒。");
            Assert.IsNull(h.Service.BubbleLastShownLocalDate, "未展示不得消费当日额度。");
            Assert.IsNull(ReadStats(h.StatsPath)?.BubbleLastShownLocalDate);
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 3. 暂停/锁屏/睡眠：不展示、不消费；恢复后按当时状态重新判定 ────

    [STATestMethod]
    public void Bubble_SuspendedReasons_DoNotShowOrConsume_ThenShowAfterResume()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_BubbleSuspended");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-1");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.FindWindowByPetId("bear-1")!.Show();

            // 暂停
            app.OnPauseToggled(null, true);
            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "暂停期间不得展示。");
            Assert.IsNull(h.Service.BubbleLastShownLocalDate, "暂停不得消费额度。");
            app.OnPauseToggled(null, false);

            // 锁屏
            app.OnSystemSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock));
            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "锁屏期间不得展示。");
            Assert.IsNull(h.Service.BubbleLastShownLocalDate, "锁屏不得消费额度。");
            app.OnSystemSessionSwitch(null, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock));

            // 睡眠
            app.OnSystemPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Suspend));
            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "睡眠期间不得展示。");
            Assert.IsNull(h.Service.BubbleLastShownLocalDate, "睡眠不得消费额度。");
            app.OnSystemPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume));

            // 恢复后下一次判定正常展示（不补播积压，只按当时状态判定一次）。
            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible, "恢复后应能正常展示。");
            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate);
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 4. 无可用宠物：不展示、不消费额度 ──────────────────────────────

    [STATestMethod]
    public void Bubble_NoAvailablePet_DoesNotShowOrConsumeQuota()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_BubbleNoPet");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-1");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);
            Assert.IsFalse(w!.IsVisible, "前置：窗口只创建不展示。");

            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "没有可用宠物时不得展示。");
            Assert.IsNull(h.Service.BubbleLastShownLocalDate, "无可用宠物不得消费额度。");

            // 宠物可见后同一时刻可正常展示。
            w.Show();
            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible);
            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate);
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 5. 多宠：只选一只可用宠物，且只有一个气泡 ──────────────────────

    [STATestMethod]
    public void Bubble_MultiPet_SelectsSingleAvailablePet_AndShowsSingleBubble()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_BubbleMultiPet");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-1", "bear-2");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState!.Pets[1]);
            var wA = app.FindWindowByPetId("bear-1");
            var wB = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(wA);
            Assert.IsNotNull(wB);

            // bear-1 不可见（不可用），bear-2 可见 → 只选 bear-2。
            wB!.Show();
            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible, "应选中唯一可用宠物展示。");

            var bubble = app.TestStatsBubbleWindow;
            Assert.IsNotNull(bubble);
            Assert.IsTrue(bubble!.IsVisible, "多宠只允许一个可见气泡（单一实例）。");

            var petRect = wB.GetPetRectDipForStatsBubble();
            Assert.IsTrue(bubble.Left + bubble.ActualWidth <= petRect.Left + 0.5,
                "气泡须放在宠物矩形外侧，不与宠物重叠。");

            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate, "展示一次只消费一次额度。");

            // 次日：两只均可用，仍只有一个气泡。
            app.HideUsageStatsBubble();
            app.TestStatsBubbleLocalNow = () => new DateTimeOffset(2026, 11, 26, 9, 35, 0, TimeSpan.FromHours(8));
            wA!.Show();
            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible);
            Assert.AreSame(bubble, app.TestStatsBubbleWindow,
                "两只宠物均可用时也复用同一个气泡实例，不会为每只宠物各建一个。");
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 6. 查看入口：两段内容、准确名称、状态与关闭/清除刷新 ────────────

    [STATestMethod]
    public void Viewer_ShowsRecordedAndPreferenceSections_RefreshesOnDisableAndClear()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_Viewer");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);

            app.ShowUsageStatsViewer();
            var viewer = app.TestStatsViewerWindow;
            Assert.IsNotNull(viewer, "主动查看入口必须能打开只读查看窗。");

            Assert.IsTrue(viewer!.TestStatusText.Contains("已开启"), viewer.TestStatusText);

            var recorded = string.Join("\n", viewer.TestRecordedLines);
            Assert.IsTrue(recorded.Contains("有记录的日期：7 天"), recorded);
            Assert.IsTrue(recorded.Contains("近似前台活跃时长"), recorded);
            Assert.IsTrue(recorded.Contains("会话次数：7 次"), recorded);
            Assert.IsTrue(recorded.Contains("涉及应用：2 个"), recorded);
            foreach (var forbidden in new[] { "使用次数", "启动次数", "准确率", "预测准确" })
                Assert.IsFalse(recorded.Contains(forbidden), "已记录统计不得使用误导名称：" + recorded);

            Assert.AreEqual(1, viewer.TestPreferenceLines.Count, "只应输出达标候选。");
            var preference = viewer.TestPreferenceLines[0];
            Assert.IsTrue(preference.Contains("notepad"), preference);
            Assert.IsTrue(preference.Contains("09:30–09:59"), preference);
            Assert.IsTrue(preference.Contains("支持日期 5 天"), preference);
            Assert.IsTrue(preference.Contains("会话 5 次"), preference);
            foreach (var forbidden in new[] { "准确率", "预测准确", "你现在" })
                Assert.IsFalse(preference.Contains(forbidden), "时间偏好只陈述统计依据：" + preference);

            // 关闭统计：窗口刷新为关闭状态，但已有记录保留。
            app.SetUsageStatsEnabled(false);
            Assert.IsTrue(viewer.TestStatusText.Contains("已关闭"), viewer.TestStatusText);
            Assert.IsTrue(string.Join("\n", viewer.TestRecordedLines).Contains("有记录的日期：7 天"),
                "关闭统计不得隐藏已有记录。");
            CollectionAssert.AreEqual(new[] { "暂无可显示的时间偏好。" }, viewer.TestPreferenceLines.ToArray(),
                "关闭统计后不得再输出时间偏好。");

            // 清除统计：两段立即变为空状态。
            app.ClearUsageStats();
            CollectionAssert.AreEqual(new[] { "暂无已记录的统计。" }, viewer.TestRecordedLines.ToArray());
            CollectionAssert.AreEqual(new[] { "暂无可显示的时间偏好。" }, viewer.TestPreferenceLines.ToArray());
        }
        finally
        {
            app.TestStatsViewerWindow?.Close();
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 7. 关闭/清除立即撤销气泡；关闭保留标记、清除重置标记 ────────────

    [STATestMethod]
    public void DisableAndClear_HideBubbleImmediately_KeepOrResetMarker()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_BubbleRevoke");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-1");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.FindWindowByPetId("bear-1")!.Show();

            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible);
            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate);

            // 关闭：立即隐藏，但保留当天已展示标记（当天不重复提示）。
            app.SetUsageStatsEnabled(false);
            Assert.IsFalse(app.TestStatsBubbleVisible, "关闭统计必须立即隐藏气泡。");
            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate,
                "关闭统计保留当天已展示标记。");

            app.SetUsageStatsEnabled(true);
            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "当天已展示过，重新开启也不得重复展示。");

            // 清除：重置已展示标记，并立即隐藏（清除后无候选，因此保持安静）。
            app.ClearUsageStats();
            Assert.IsFalse(app.TestStatsBubbleVisible, "清除统计必须立即隐藏气泡。");
            Assert.IsNull(h.Service.BubbleLastShownLocalDate, "清除统计必须重置已展示标记。");
            Assert.IsNull(ReadStats(h.StatsPath)?.BubbleLastShownLocalDate,
                "清除后落盘去重日期必须为空。");
            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "清除后没有记录，不得展示。");
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 8. R1 F-01：共享社交会话中的宠物不得被选中（本地旗未更新） ────

    [STATestMethod]
    public void Bubble_SharedSocialSessionBusyPet_NotSelectedOrConsumed_ThenResumesAfterSession()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_BubbleSharedSocial");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-2");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(w);
            w!.Show();

            // 跨窗口共享会话：协调器已持有含 bear-2 的活跃会话，而该窗口的
            // 本地 _isSocialActive 仍为 false（本用例不 tick 该窗口）。
            var coordinator = new SocialCoordinator();
            coordinator.TestSeedSession(new SocialSession(
                "stats-shared-test", "bear-2", "bear-1", SocialPhase.Noticing, 0, 0));
            w.TestSocialCoordinator = coordinator;

            Assert.IsFalse(w.IsAvailableForStatsBubble,
                "共享社交会话活跃时（本地旗未更新）不得判定为可展示。");

            app.TryShowUsageStatsBubble();
            Assert.IsFalse(app.TestStatsBubbleVisible, "共享会话中的宠物不得被选中展示气泡。");
            Assert.IsNull(h.Service.BubbleLastShownLocalDate, "未展示不得消费每日标记。");
            Assert.IsNull(ReadStats(h.StatsPath)?.BubbleLastShownLocalDate);

            // 会话结束（非活跃阶段）：同一宠物恢复可展示，额度正常消费。
            coordinator.TestSeedSession(new SocialSession(
                "stats-shared-test", "bear-2", "bear-1", SocialPhase.Idle, 0, 0));
            Assert.IsTrue(w.IsAvailableForStatsBubble, "会话结束后应恢复可展示。");

            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible, "会话结束后同一时刻应能正常展示。");
            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate);
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 9. R1 F-01：另有真正空闲且不参与会话的宠物时按原规则选取 ───────

    [STATestMethod]
    public void Bubble_SharedSocialBusyPet_Skipped_FreeUninvolvedPetSelected()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        try
        {
            var dir = GetTempDir("P6004_BubbleSharedSocialSkip");
            h = new StatsHarness(dir);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            AddPets(app, "bear-1", "bear-2");
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState.Pets[1]);
            var wA = app.FindWindowByPetId("bear-1");
            var wB = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(wA);
            Assert.IsNotNull(wB);
            wA!.Show();
            wB!.Show();

            // 只有 bear-2 参与共享会话；bear-1 真正空闲且未参与。
            var coordinator = new SocialCoordinator();
            coordinator.TestSeedSession(new SocialSession(
                "stats-skip-test", "bear-2", "bear-1-skips", SocialPhase.Chasing, 0, 0));
            wB.TestSocialCoordinator = coordinator;

            Assert.IsFalse(wB.IsAvailableForStatsBubble, "会话中的 bear-2 不可展示。");
            Assert.IsTrue(wA.IsAvailableForStatsBubble, "空闲的 bear-1 应保持可展示。");

            app.TryShowUsageStatsBubble();
            Assert.IsTrue(app.TestStatsBubbleVisible, "存在空闲宠物时应正常展示气泡。");
            Assert.AreEqual("2026-11-25", h.Service.BubbleLastShownLocalDate);
        }
        finally
        {
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
        }
    }

    // ── 10. R1 F-02：首查即按含今天的 30 天保留期过滤，且 drain/重启一致 ──

    [STATestMethod]
    public void Viewer_RecordedSummary_FiltersRetentionFirstQuery_StableAcrossDrainRestartAndClear()
    {
        var app = GetOrCreateApp();
        StatsHarness? h = null;
        StatsHarness? h2 = null;
        var dir = GetTempDir("P6004_Retention");
        try
        {
            // 今天 2026-11-25 → 保留窗口 [2026-10-27, 2026-11-25]（含今天）。
            Directory.CreateDirectory(dir);
            WriteBuckets(Path.Combine(dir, "stats.json"),
                Bucket("expired", "2026-10-26", 9, 30, activeMs: 600000, sessionCount: 9), // today−30
                Bucket("edge", "2026-10-27", 9, 30, activeMs: 30000, sessionCount: 1),      // today−29
                Bucket("today", "2026-11-25", 9, 30, activeMs: 90000, sessionCount: 2));    // 今天

            h = new StatsHarness(dir, seed: false, deleteDir: false);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h.Service);
            app.SetUsageStatsEnabled(true);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;

            // 首次查询即过滤（尚未发生任何 drain / 物理清理）。
            var first = h.Service.GetRecordedSummary();
            Assert.AreEqual(2, first.RecordedDateCount, "today−30 必须首查即排除。");
            Assert.AreEqual(2, first.AppCount, "过期日期的应用不得计入。");
            Assert.AreEqual(3, first.SessionCount, "过期日期的会话不得计入。");
            Assert.AreEqual(120000.0, first.ApproximateForegroundActiveMs, 0.001,
                "含今天窗口内 today−29 与今天保留，today−30 排除。");

            // 查看入口同样只反映保留窗口内记录。
            app.ShowUsageStatsViewer();
            var viewer = app.TestStatsViewerWindow;
            Assert.IsNotNull(viewer, "查看入口必须能打开。");
            var recorded = string.Join("\n", viewer!.TestRecordedLines);
            Assert.IsTrue(recorded.Contains("有记录的日期：2 天"), recorded);
            Assert.IsTrue(recorded.Contains("会话次数：3 次"), recorded);

            // drain（关闭统计 → 剪枝落盘）前后同一时刻汇总一致。
            app.SetUsageStatsEnabled(false);
            var afterDrain = h.Service.GetRecordedSummary();
            AssertSameSummary(first, afterDrain, "drain 前后");

            // 关闭统计后仍可查看期限内旧记录。
            Assert.IsFalse(h.Service.IsEnabled, "前置：统计已关闭。");
            recorded = string.Join("\n", viewer.TestRecordedLines);
            Assert.IsTrue(recorded.Contains("有记录的日期：2 天"), "关闭后仍应显示期限内旧记录：" + recorded);

            // 重启：同一隔离目录重新加载，汇总不变。
            viewer.Close();
            app.TestCleanup();
            h.Dispose(); // deleteDir=false，保留 stats.json 供模拟重启读取
            h = null;

            h2 = new StatsHarness(dir, seed: false, deleteDir: false);
            app.TestSetupIsolatedSave(dir);
            app.SetUsageStatsServiceForTest(h2.Service);
            app.TestStatsBubbleLocalNow = () => s_fixedNow;
            var afterRestart = h2.Service.GetRecordedSummary();
            AssertSameSummary(first, afterRestart, "重启后");

            // 清除：立即为空。
            h2.Service.ClearAll();
            var cleared = h2.Service.GetRecordedSummary();
            Assert.IsFalse(cleared.HasRecords, "清除后不得再有任何已记录统计。");
            CollectionAssert.AreEqual(
                new[] { "暂无已记录的统计。" },
                UsageStatsBubblePlanner.FormatRecordedSummaryLines(cleared).ToArray());
        }
        finally
        {
            app.TestStatsViewerWindow?.Close();
            CloseStatsBubble(app);
            app.TestStatsBubbleLocalNow = null;
            app.TestCleanup();
            h?.Dispose();
            h2?.Dispose();
            DeleteTempDir(dir);
        }
    }
}
