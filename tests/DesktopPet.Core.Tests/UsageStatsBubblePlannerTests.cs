using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// P6-004（第一小步）：主动气泡判定与只读统计入口文案的 Core 规则测试。
/// <para>
/// 覆盖：当前半小时时段匹配、每日去重（含重启读回的日期）、暂停/锁屏/睡眠
/// 抑制、无可接受展示宠物、无候选静默、文案只表达统计依据（不出现指令式措辞
/// 或准确率）、已记录统计的名称与汇总口径、时间偏好行的统计依据标注。
/// </para>
/// </summary>
[TestClass]
public class UsageStatsBubblePlannerTests
{
    private static readonly DateTimeOffset s_now =
        new(2026, 11, 25, 12, 10, 0, TimeSpan.FromHours(8)); // 时段 12:00

    private static UsageStatsFrequencyCandidate Candidate(
        string appId = "notepad",
        string slotStart = "12:00",
        int supportDates = 5,
        int sessions = 6)
        => new()
        {
            AppId = appId,
            SlotStart = slotStart,
            SupportDateCount = supportDates,
            SessionCount = sessions
        };

    private static UsageStatsFrequencyResult Result(
        bool enabled = true,
        params UsageStatsFrequencyCandidate[] candidates)
        => new()
        {
            IsEnabled = enabled,
            WindowDays = 29,
            ObservedDateCount = 12,
            Candidates = candidates
        };

    private static UsageStatsBucket Bucket(
        string appId,
        string date,
        double activeMs = 60000,
        int sessions = 1)
        => new()
        {
            AppId = appId,
            Date = date,
            BucketStartLocal = $"{date}T12:00:00.0000000+08:00",
            ActiveMs = activeMs,
            SessionCount = sessions
        };

    // ── 时段 ────────────────────────────────────────────────────────────

    [DataTestMethod]
    [DataRow(0, 0, "00:00")]
    [DataRow(12, 29, "12:00")]
    [DataRow(12, 30, "12:30")]
    [DataRow(23, 59, "23:30")]
    public void GetCurrentSlotStart_FloorsToHalfHour(int hour, int minute, string expected)
    {
        var now = new DateTimeOffset(2026, 11, 25, hour, minute, 0, TimeSpan.FromHours(8));
        Assert.AreEqual(expected, UsageStatsBubblePlanner.GetCurrentSlotStart(now));
    }

    [TestMethod]
    public void FormatSlotLabel_RendersHalfHourInterval()
    {
        Assert.AreEqual("12:00–12:29", UsageStatsBubblePlanner.FormatSlotLabel("12:00"));
        Assert.AreEqual("12:30–12:59", UsageStatsBubblePlanner.FormatSlotLabel("12:30"));
        Assert.AreEqual("23:30–23:59", UsageStatsBubblePlanner.FormatSlotLabel("23:30"));
    }

    // ── 文案：只表达统计依据 ────────────────────────────────────────────

    [TestMethod]
    public void FormatBubbleMessage_StatesEvidenceWithoutInstructionOrAccuracy()
    {
        var message = UsageStatsBubblePlanner.FormatBubbleMessage(Candidate());

        StringAssert.Contains(message, "5 天");
        StringAssert.Contains(message, "12:00–12:29");
        StringAssert.Contains(message, "notepad");
        Assert.IsFalse(message.Contains("该打开"), "文案不得出现指令式措辞。");
        Assert.IsFalse(message.Contains("准确率"), "文案不得包装准确率。");
        Assert.IsFalse(message.Contains("%"), "文案不得出现百分比式准确率。");
        Assert.IsFalse(message.Contains("样本不足"), "无候选时应安静，不输出样本不足提醒。");
    }

    // ── 判定 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Decide_NullResult_IsSilent()
    {
        var decision = UsageStatsBubblePlanner.Decide(null, s_now, null, false, true);
        Assert.IsFalse(decision.ShouldShow);
    }

    [TestMethod]
    public void Decide_Disabled_IsSilent()
    {
        var decision = UsageStatsBubblePlanner.Decide(
            Result(enabled: false, Candidate()), s_now, null, false, true);
        Assert.IsFalse(decision.ShouldShow);
    }

    [TestMethod]
    public void Decide_NoCandidates_IsSilent()
    {
        var decision = UsageStatsBubblePlanner.Decide(Result(), s_now, null, false, true);
        Assert.IsFalse(decision.ShouldShow);
        Assert.AreEqual("", decision.Message);
    }

    [TestMethod]
    public void Decide_MatchingSlot_ShowsWithEvidenceMessage()
    {
        var decision = UsageStatsBubblePlanner.Decide(
            Result(true, Candidate()), s_now, null, false, true);

        Assert.IsTrue(decision.ShouldShow);
        Assert.AreEqual("notepad", decision.AppId);
        Assert.AreEqual("12:00", decision.SlotStart);
        Assert.AreEqual("2026-11-25", decision.LocalDate);
        StringAssert.Contains(decision.Message, "notepad");
    }

    [TestMethod]
    public void Decide_NonMatchingSlot_IsSilent()
    {
        var decision = UsageStatsBubblePlanner.Decide(
            Result(true, Candidate(slotStart: "09:30")), s_now, null, false, true);
        Assert.IsFalse(decision.ShouldShow);
    }

    [TestMethod]
    public void Decide_OnlyFirstMatchingCandidateIsUsed()
    {
        var decision = UsageStatsBubblePlanner.Decide(
            Result(true, Candidate(appId: "other", slotStart: "09:30"), Candidate(appId: "notepad")),
            s_now, null, false, true);
        Assert.IsTrue(decision.ShouldShow);
        Assert.AreEqual("notepad", decision.AppId);
    }

    [TestMethod]
    public void Decide_AlreadyShownToday_IsSilent_EvenAfterRestart()
    {
        // 去重日期来自 stats.json 恢复值（跨重启同样生效）。
        var decision = UsageStatsBubblePlanner.Decide(
            Result(true, Candidate()), s_now, "2026-11-25", false, true);
        Assert.IsFalse(decision.ShouldShow);
    }

    [TestMethod]
    public void Decide_ShownOnAnotherDate_ShowsAgain()
    {
        var decision = UsageStatsBubblePlanner.Decide(
            Result(true, Candidate()), s_now, "2026-11-24", false, true);
        Assert.IsTrue(decision.ShouldShow);
    }

    [TestMethod]
    public void Decide_Suspended_IsSilent()
    {
        var decision = UsageStatsBubblePlanner.Decide(
            Result(true, Candidate()), s_now, null, isSuspended: true, hasAvailablePet: true);
        Assert.IsFalse(decision.ShouldShow);
    }

    [TestMethod]
    public void Decide_NoAvailablePet_IsSilent()
    {
        var decision = UsageStatsBubblePlanner.Decide(
            Result(true, Candidate()), s_now, null, isSuspended: false, hasAvailablePet: false);
        Assert.IsFalse(decision.ShouldShow);
    }

    // ── 已记录的统计 ────────────────────────────────────────────────────

    [TestMethod]
    public void SummarizeRecorded_SumsOnlyActiveBucketsAndDistinctKeys()
    {
        var buckets = new[]
        {
            Bucket("notepad", "2026-11-24", activeMs: 120000, sessions: 2),
            Bucket("notepad", "2026-11-24", activeMs: 60000, sessions: 1),
            Bucket("browser", "2026-11-25", activeMs: 30000, sessions: 1),
            Bucket("ignored", "2026-11-25", activeMs: 0, sessions: 3) // 零活跃不计
        };

        var summary = UsageStatsBubblePlanner.SummarizeRecorded(buckets, s_now);

        Assert.IsTrue(summary.HasRecords);
        Assert.AreEqual(2, summary.RecordedDateCount);
        // 零活跃桶既不计时长也不计入“涉及应用”。
        Assert.AreEqual(2, summary.AppCount);
        Assert.AreEqual(4, summary.SessionCount);
        Assert.AreEqual(210000.0, summary.ApproximateForegroundActiveMs, 0.001);
    }

    [TestMethod]
    public void SummarizeRecorded_Empty_HasNoRecords()
    {
        var summary = UsageStatsBubblePlanner.SummarizeRecorded(Array.Empty<UsageStatsBucket>(), s_now);
        Assert.IsFalse(summary.HasRecords);
        CollectionAssert.AreEqual(
            new[] { "暂无已记录的统计。" },
            UsageStatsBubblePlanner.FormatRecordedSummaryLines(summary).ToArray());
    }

    // ── P6-004 R1 F-02：读取侧保留期过滤（含今天） ──────────────────────

    [TestMethod]
    public void SummarizeRecorded_ExcludesBucketsBeforeInclusiveRetentionWindow()
    {
        // 今天 2026-11-25，聚合保留 30 天含今天 → 窗口 [2026-10-27, 2026-11-25]。
        var buckets = new[]
        {
            Bucket("expired", "2026-10-26", activeMs: 600000, sessions: 9), // today−30：首查即排除
            Bucket("edge", "2026-10-27", activeMs: 30000, sessions: 1),     // today−29：保留
            Bucket("today", "2026-11-25", activeMs: 90000, sessions: 2)     // 今天：保留
        };

        var summary = UsageStatsBubblePlanner.SummarizeRecorded(buckets, s_now);

        Assert.AreEqual(2, summary.RecordedDateCount, "只统计保留窗口内的两个日期。");
        Assert.AreEqual(2, summary.AppCount, "过期日期的应用不得计入。");
        Assert.AreEqual(3, summary.SessionCount, "过期日期的会话不得计入。");
        Assert.AreEqual(120000.0, summary.ApproximateForegroundActiveMs, 0.001,
            "过期日期的活跃时长不得计入（含今天窗口：today−29 与今天保留）。");
    }

    [TestMethod]
    public void SummarizeRecorded_ExcludesFutureDatedBuckets_RelativeToToday()
    {
        var buckets = new[]
        {
            Bucket("today", "2026-11-25", activeMs: 60000, sessions: 1),
            Bucket("future", "2026-11-26", activeMs: 600000, sessions: 9)
        };

        var summary = UsageStatsBubblePlanner.SummarizeRecorded(buckets, s_now);

        Assert.AreEqual(1, summary.RecordedDateCount);
        Assert.AreEqual(1, summary.AppCount);
        Assert.AreEqual(60000.0, summary.ApproximateForegroundActiveMs, 0.001);
    }

    [TestMethod]
    public void SummarizeRecorded_RetentionWindowShiftsWithToday_NoExtendedRetention()
    {
        // 第二天（2026-11-26）窗口右移：10-27 变成 today−30，被排除。
        var buckets = new[]
        {
            Bucket("edge", "2026-10-27", activeMs: 30000, sessions: 1),
            Bucket("today", "2026-11-26", activeMs: 60000, sessions: 1)
        };
        var nextDay = new DateTimeOffset(2026, 11, 26, 9, 35, 0, TimeSpan.FromHours(8));

        var summary = UsageStatsBubblePlanner.SummarizeRecorded(buckets, nextDay);

        Assert.AreEqual(1, summary.RecordedDateCount, "保留期不因查询而延长。");
        Assert.AreEqual(60000.0, summary.ApproximateForegroundActiveMs, 0.001);
    }

    [TestMethod]
    public void FormatRecordedSummaryLines_UsesAccurateNames()
    {
        var summary = UsageStatsBubblePlanner.SummarizeRecorded(
            new[] { Bucket("notepad", "2026-11-24", activeMs: 3600000, sessions: 2) }, s_now);
        var lines = UsageStatsBubblePlanner.FormatRecordedSummaryLines(summary);

        Assert.IsTrue(lines.Any(l => l.Contains("有记录的日期")), "应报告有记录的日期数。");
        Assert.IsTrue(lines.Any(l => l.Contains("近似前台活跃时长")), "应使用近似前台活跃时长。");
        Assert.IsTrue(lines.Any(l => l.Contains("会话次数")), "应使用会话次数。");
        Assert.IsFalse(lines.Any(l => l.Contains("使用次数")), "不得把采样或启动称作使用次数。");
        Assert.IsFalse(lines.Any(l => l.Contains("启动")), "不得声称应用启动次数。");
    }

    [TestMethod]
    public void FormatPreferenceLine_IsStatisticalEvidenceNotAccuracy()
    {
        var line = UsageStatsBubblePlanner.FormatPreferenceLine(Candidate());

        StringAssert.Contains(line, "notepad");
        StringAssert.Contains(line, "支持日期 5 天");
        StringAssert.Contains(line, "会话 6 次");
        Assert.IsFalse(line.Contains("准确率"), "时间偏好行不得包装准确率。");
    }

    [DataTestMethod]
    [DataRow(0.0, "0 分钟")]
    [DataRow(30000.0, "不足 1 分钟")]
    [DataRow(60000.0, "1 分钟")]
    [DataRow(3600000.0, "1 小时")]
    [DataRow(5400000.0, "1 小时 30 分钟")]
    public void FormatDuration_RendersReadableDurations(double ms, string expected)
    {
        Assert.AreEqual(expected, UsageStatsBubblePlanner.FormatDuration(ms));
    }
}
