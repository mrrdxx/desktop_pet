using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// P6-003: Core 假时钟/构造桶测试 for <see cref="UsageStatsFrequencyJudge"/>.
/// <para>
/// 覆盖 PRODUCT R-09 / ARCHITECTURE §7 频次判断口径：
/// - 关闭（全局统计开关）时不输出预测；
/// - 覆盖门槛（窗口内 ≥7 个不同日期有有效统计）；
/// - 候选门槛（同一 AppId+半小时时段 ≥5 个不同日期且会话数 ≥5）；
/// - 排除尚未结束的当天、有效窗口与 P6-002 保留期取交集（today−29 计入、today−30 过期）；
/// - 非法/空/零活跃桶跳过；
/// - 输出仅为统计依据（应用、时段、支持日期数、会话数）并按规则排序。
/// 默认门槛为保守起步值，测试不断言真实预测质量。
/// </para>
/// </summary>
[TestClass]
public class UsageStatsFrequencyJudgeTests
{
    /// <summary>固定本地时间：2026-11-25 12:00 (+08:00)；有效窗口起点 2026-10-27（today−29）。</summary>
    private static readonly DateTimeOffset s_now =
        new(2026, 11, 25, 12, 0, 0, TimeSpan.FromHours(8));

    private static UsageStatsBucket Bucket(
        string appId,
        string date,
        int hour,
        int minute,
        double activeMs = 5000,
        int sessions = 1)
        => new()
        {
            AppId = appId,
            Date = date,
            BucketStartLocal = $"{date}T{hour:D2}:{minute:D2}:00.0000000+08:00",
            ActiveMs = activeMs,
            SessionCount = sessions
        };

    /// <summary>在给定日期集合上补齐"其他应用"的有效桶，仅用于凑覆盖日期。</summary>
    private static void AddCoverageDates(
        List<UsageStatsBucket> buckets, int fromDay, int toDay)
    {
        for (var day = fromDay; day <= toDay; day++)
            buckets.Add(Bucket("coverageapp", $"2026-11-{day:D2}", 18, 0));
    }

    // ── 关闭 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disabled_ReturnsNoPrediction_EvenWithQualifyingData()
    {
        var buckets = new List<UsageStatsBucket>();
        for (var day = 10; day <= 14; day++)
            buckets.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30));
        AddCoverageDates(buckets, 15, 16);

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: false);

        Assert.IsFalse(result.IsEnabled);
        Assert.IsTrue(result.IsSuppressed);
        Assert.AreEqual(0, result.Candidates.Count, "关闭时不得输出预测。");
        Assert.AreEqual(0, result.ObservedDateCount);
    }

    // ── 覆盖门槛 ────────────────────────────────────────────────────────

    [TestMethod]
    public void SixObservedDates_NoCandidate()
    {
        var buckets = new List<UsageStatsBucket>();
        for (var day = 10; day <= 14; day++)
            buckets.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30));
        buckets.Add(Bucket("chrome", "2026-11-15", 14, 0)); // 仅 6 个不同日期

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(6, result.ObservedDateCount);
        Assert.AreEqual(0, result.Candidates.Count, "覆盖日期不足 7 时不得输出候选。");
    }

    // ── 候选门槛：支持日期 / 会话数 ─────────────────────────────────────

    [TestMethod]
    public void FourSupportDates_NoCandidate()
    {
        var buckets = new List<UsageStatsBucket>();
        for (var day = 10; day <= 13; day++) // 只出现于 4 个日期
            buckets.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30, sessions: 2));
        AddCoverageDates(buckets, 14, 16); // 覆盖日期 7 个

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(7, result.ObservedDateCount);
        Assert.AreEqual(0, result.Candidates.Count, "支持日期不足 5 不得成为候选。");
    }

    [TestMethod]
    public void FiveSupportDatesButOnlyFourSessions_NoCandidate()
    {
        var buckets = new List<UsageStatsBucket>
        {
            Bucket("notepad", "2026-11-10", 9, 30, sessions: 1),
            Bucket("notepad", "2026-11-11", 9, 30, sessions: 1),
            Bucket("notepad", "2026-11-12", 9, 30, sessions: 1),
            Bucket("notepad", "2026-11-13", 9, 30, sessions: 1),
            // 第 5 个日期只有活跃时长、没有会话起始（会话计在起始桶）
            Bucket("notepad", "2026-11-14", 9, 30, sessions: 0)
        };
        AddCoverageDates(buckets, 15, 16);

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(7, result.ObservedDateCount);
        Assert.AreEqual(0, result.Candidates.Count, "累计会话不足 5 不得成为候选。");
    }

    // ── 足够样本：输出字段 ──────────────────────────────────────────────

    [TestMethod]
    public void EnoughData_ReturnsCandidateWithStatisticalFields()
    {
        var buckets = new List<UsageStatsBucket>();
        for (var day = 10; day <= 14; day++)
            buckets.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30));
        AddCoverageDates(buckets, 15, 16);

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.IsTrue(result.IsEnabled);
        Assert.AreEqual(7, result.ObservedDateCount);
        Assert.AreEqual(29, result.WindowDays,
            "保留期 30 且排除当天，实际可用为 29 个完整日期。");
        Assert.AreEqual(1, result.Candidates.Count);
        var candidate = result.Candidates[0];
        Assert.AreEqual("notepad", candidate.AppId);
        Assert.AreEqual("09:30", candidate.SlotStart);
        Assert.AreEqual(5, candidate.SupportDateCount);
        Assert.AreEqual(5, candidate.SessionCount);
    }

    // ── 排除当天 ────────────────────────────────────────────────────────

    [TestMethod]
    public void TodayExcluded_FromSupportAndObservation()
    {
        var buckets = new List<UsageStatsBucket>();
        for (var day = 10; day <= 13; day++) // 当天之前只有 4 个日期
            buckets.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30, sessions: 2));
        buckets.Add(Bucket("notepad", "2026-11-25", 9, 30, sessions: 10)); // 当天：排除
        AddCoverageDates(buckets, 14, 16); // 加上 today 才凑到 8；排除后为 7

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(7, result.ObservedDateCount, "当天不计入覆盖日期。");
        Assert.AreEqual(0, result.Candidates.Count,
            "当天数据既不得补足支持日期，也不得补足会话数。");

        // 反证：若包含当天则会满足门槛（证明上面的拒绝确实来自排除当天）。
        var inclusive = UsageStatsFrequencyJudge.Evaluate(
            buckets, s_now, enabled: true,
            new UsageStatsFrequencyOptions { ExcludeToday = false });
        Assert.AreEqual(1, inclusive.Candidates.Count);
        Assert.AreEqual(5, inclusive.Candidates[0].SupportDateCount);
        Assert.AreEqual(18, inclusive.Candidates[0].SessionCount);
    }

    // ── 窗口边界 ────────────────────────────────────────────────────────

    [TestMethod]
    public void WindowBoundary_TodayMinus29Included_TodayMinus30Expired()
    {
        var buckets = new List<UsageStatsBucket>
        {
            Bucket("chrome", "2026-10-27", 14, 0), // today−29：保留期内，计入
            Bucket("chrome", "2026-10-26", 14, 0), // today−30：已超出保留期，排除
            Bucket("chrome", "2026-10-25", 14, 0)  // today−31：过期
        };

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(1, result.ObservedDateCount, "只有 today−29 在有效窗口内。");
        Assert.AreEqual(29, result.WindowDays,
            "有效窗口为名义 30 日与保留期 [today−29,today] 的交集，排除当天后为 29 个完整日期。");
    }

    // ── 非法输入跳过 ────────────────────────────────────────────────────

    [TestMethod]
    public void InvalidBuckets_AreSkipped()
    {
        var buckets = new List<UsageStatsBucket>
        {
            new() { AppId = "notepad", Date = "2026-11-10", BucketStartLocal = "not-a-date", ActiveMs = 5000, SessionCount = 1 },
            new() { AppId = "   ", Date = "2026-11-10", BucketStartLocal = "2026-11-10T09:30:00.0000000+08:00", ActiveMs = 5000, SessionCount = 1 },
            Bucket("notepad", "2026-11-11", 9, 30, activeMs: 0),  // 零活跃不算"出现"
            Bucket("notepad", "2026-11-12", 9, 30, sessions: 0),
            Bucket("notepad", "2026-11-13", 9, 30, sessions: 0),
            Bucket("notepad", "2026-11-14", 9, 30, sessions: 0),
            Bucket("notepad", "2026-11-15", 9, 30, sessions: 0),
            Bucket("notepad", "2026-11-16", 9, 30, sessions: 0),
            Bucket("notepad", "2026-11-17", 9, 30, sessions: 0)
        };

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(6, result.ObservedDateCount,
            "非法时间与零活跃桶不参与；空 AppId 不参与。");
        Assert.AreEqual(0, result.Candidates.Count, "0 会话不构成候选。");
    }

    [TestMethod]
    public void NullBuckets_ReturnsEmptyAndNoThrow()
    {
        var result = UsageStatsFrequencyJudge.Evaluate(null, s_now, enabled: true);

        Assert.IsTrue(result.IsEnabled);
        Assert.IsTrue(result.IsSuppressed);
        Assert.AreEqual(0, result.ObservedDateCount);
    }

    // ── 排序 ────────────────────────────────────────────────────────────

    [TestMethod]
    public void MultipleCandidates_OrderedBySupportThenSessionsThenAppId()
    {
        var buckets = new List<UsageStatsBucket>();
        // a：6 个日期，6 会话（支持日期最多 → 第一）
        for (var day = 10; day <= 15; day++)
            buckets.Add(Bucket("app-a", $"2026-11-{day:D2}", 9, 30));
        // b/c：5 个日期、9 会话（支持相同、会话相同 → 按 AppId 序数）
        for (var day = 10; day <= 14; day++)
        {
            buckets.Add(Bucket("app-c", $"2026-11-{day:D2}", 10, 0, sessions: 2));
            buckets.Add(Bucket("app-b", $"2026-11-{day:D2}", 10, 0, sessions: 2));
        }
        buckets.Add(Bucket("filler", "2026-11-16", 20, 0)); // 补 7 个覆盖日期

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(7, result.ObservedDateCount);
        Assert.AreEqual(3, result.Candidates.Count);
        Assert.AreEqual("app-a", result.Candidates[0].AppId);
        Assert.AreEqual("app-b", result.Candidates[1].AppId);
        Assert.AreEqual("app-c", result.Candidates[2].AppId);
        Assert.AreEqual(10, result.Candidates[1].SessionCount, "2 会话 × 5 个日期。");
    }

    // ── 会话只按起始桶求和 ──────────────────────────────────────────────

    [TestMethod]
    public void SessionsAreSummedPerSlotBucket()
    {
        var buckets = new List<UsageStatsBucket>();
        for (var day = 10; day <= 12; day++)
            buckets.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30, sessions: 2));
        for (var day = 13; day <= 14; day++)
            buckets.Add(Bucket("notepad", $"2026-11-{day:D2}", 9, 30, sessions: 1));
        AddCoverageDates(buckets, 15, 16);

        var result = UsageStatsFrequencyJudge.Evaluate(buckets, s_now, enabled: true);

        Assert.AreEqual(1, result.Candidates.Count);
        Assert.AreEqual(5, result.Candidates[0].SupportDateCount);
        Assert.AreEqual(8, result.Candidates[0].SessionCount, "会话数按桶内 SessionCount 求和。");
    }
}
