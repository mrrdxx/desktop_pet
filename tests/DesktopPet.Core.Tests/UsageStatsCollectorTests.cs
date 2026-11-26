using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// P6-001: Core 假时钟测试 for <see cref="UsageStatsCollector"/>.
/// <para>
/// Verifies PRODUCT R-09 / ARCHITECTURE §7 contracts:
/// - Disabled by default: no sampling, no events while disabled.
/// - Enable/disable: disable stops new collection, keeps old data,
///   retention cleanup still runs while disabled.
/// - Foreground sampling: 5 s ticks; input age &gt; 60 s is idle and never
///   attributed; broken sample gaps close the segment without back-fill.
/// - Lifecycle events: app-start / unlock / resume are distinct and never
///   mixed with foreground observations; lock/sleep/pause suspend.
/// - Cross-midnight splitting per local day; monotonic duration.
/// - Identification failure: single policy drop + count (no unknown bucket).
/// - 7-day raw retention keyed by counted local date.
/// - Clear bumps generation, wipes data and pending writes; writes drained
///   before a clear carry the old generation and are rejected (no
///   resurrection), restart cannot restore cleared history.
/// </para>
/// </summary>
[TestClass]
public class UsageStatsCollectorTests
{
    private const double MsPerSec = 1000.0;
    private const double MsPerMin = 60000.0;

    /// <summary>Controlled dual clock: one monotonic ms field plus a base
    /// local offset, so monotonic time and wall clock advance together.</summary>
    private sealed class FakeClock
    {
        public double NowMs;
        public DateTimeOffset BaseLocal;

        public double MonotonicNow() => NowMs;
        public DateTimeOffset LocalNow() => BaseLocal.AddMilliseconds(NowMs);
    }

    private static FakeClock NewClock(DateTimeOffset baseLocal)
        => new() { NowMs = 0, BaseLocal = baseLocal };

    private static UsageStatsCollector NewCollector(
        FakeClock clock,
        double inputAgeThresholdMs = UsageStatsCollector.DefaultInputAgeThresholdMs,
        double maxSampleGapMs = UsageStatsCollector.DefaultMaxSampleGapMs)
        => new(clock.MonotonicNow, clock.LocalNow, inputAgeThresholdMs, maxSampleGapMs);

    // ── default off ────────────────────────────────────────────────────

    [TestMethod]
    public void DisabledByDefault_NoSegmentsNoEvents()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);

        collector.SampleTick("chrome", 0);
        collector.OnAppStart();
        collector.OnUnlock();
        collector.OnResume();

        Assert.IsFalse(collector.IsEnabled);
        Assert.AreEqual(0, collector.Segments.Count);
        Assert.AreEqual(0, collector.Events.Count);
        Assert.IsNull(collector.DrainPending());
    }

    [TestMethod]
    public void SampleTick_WhenEnabled_ProducesCommittedAndPendingSegment()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);      // baseline at mono 0
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("chrome", 5 * MsPerSec);   // commits [0,5s) to chrome
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("notepad", 10 * MsPerSec); // app switch: commits [5s,10s) to chrome

        // F-02: only the confirmed interval between two adjacent successful
        // ticks is committed — chrome gets [0,5s) and [5s,10s) as two
        // segments (total 10 s); the still-running notepad tick is only a
        // baseline, not committed.
        Assert.AreEqual(2, collector.Segments.Count);
        var total = 0.0;
        foreach (var seg in collector.Segments)
        {
            Assert.AreEqual("chrome", seg.AppId);
            Assert.AreEqual("2026-11-26", seg.Date);
            total += seg.DurationMs;
        }
        Assert.AreEqual(10 * MsPerSec, total, 0.01);

        var write = collector.DrainPending();
        Assert.IsNotNull(write);
        Assert.AreEqual(2, write!.Segments.Count);
        Assert.AreEqual("chrome", write.Segments[0].AppId);
        Assert.AreEqual(collector.Generation, write.Generation);
    }

    [TestMethod]
    public void SameAppId_CommitsOneSegmentPerTick_TotalDurationPreserved()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        for (int i = 0; i < 10; i++)
        {
            clock.NowMs = i * 5 * MsPerSec;
            collector.SampleTick("word", clock.NowMs);
        }
        // Disable drops the unverified tail only; the 9 confirmed
        // [i*5s,(i+1)*5s) spans are already committed.
        collector.Disable();

        Assert.AreEqual(9, collector.Segments.Count);
        var total = 0.0;
        foreach (var seg in collector.Segments)
        {
            Assert.AreEqual("word", seg.AppId);
            total += seg.DurationMs;
        }
        Assert.AreEqual((10 - 1) * 5 * MsPerSec, total, 0.01);
    }

    // ── enable / disable ───────────────────────────────────────────────

    [TestMethod]
    public void Disable_StopsNewCollection_KeepsOldData()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("chrome", 5 * MsPerSec);

        collector.Disable();
        Assert.IsFalse(collector.IsEnabled);

        // The verified pair committed one segment before disable; disable
        // only drops the unverified tail — data stays.
        Assert.AreEqual(1, collector.Segments.Count);

        // New ticks while disabled produce nothing.
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("chrome", 10 * MsPerSec);
        collector.OnUnlock();
        Assert.AreEqual(1, collector.Segments.Count);
        Assert.AreEqual(0, collector.Events.Count);
    }

    [TestMethod]
    public void PruneToRetention_RunsWhileDisabled()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("old", 0);
        collector.Disable();

        // Load stale committed data into a fresh collector (simulating a
        // days-old archive), then prune while disabled.
        var stale = new UsageStatsArchive();
        stale.Segments.Add(new UsageStatsSegment
        {
            AppId = "stale-app",
            Date = "2026-11-18", // > 7 days before 2026-11-26
            StartLocal = "2026-11-18T09:00:00.0000000+08:00",
            DurationMs = 1000
        });
        var fresh = NewCollector(clock);
        fresh.Restore(stale);
        Assert.IsFalse(fresh.IsEnabled);

        int removed = fresh.PruneToRetention();
        Assert.AreEqual(1, removed);
        Assert.AreEqual(0, fresh.Segments.Count);
    }

    // ── input idle / gaps ──────────────────────────────────────────────

    [TestMethod]
    public void InputIdle_BeyondThreshold_ClosesSegment_NoIdleAttribution()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        clock.NowMs = 0;
        collector.SampleTick("chrome", 0);
        // Input stays fresh for the first 120 s (5 s sampling ticks, no gaps).
        for (int t = 5; t <= 120; t += 5)
        {
            clock.NowMs = t * MsPerSec;
            collector.SampleTick("chrome", clock.NowMs);
        }
        // User input stops after 120 s but sampling continues every 5 s, so
        // the trace never breaks: input age grows past the 60 s threshold.
        for (int t = 125; t <= 180; t += 5)
        {
            clock.NowMs = t * MsPerSec;
            collector.SampleTick("chrome", 120 * MsPerSec);
        }
        // Age 65 s > 60 s → idle: the segment closes at the idle tick.
        clock.NowMs = 185 * MsPerSec;
        collector.SampleTick("chrome", 120 * MsPerSec);

        // The confirmed window is every adjacent-successful-tick span while
        // input age stays within the threshold (age = now − 120s; the last
        // valid tick is at 180 s where age = 60 s is not yet > 60 s). The
        // idle interval after it is not attributed.
        var total = 0.0;
        foreach (var seg in collector.Segments)
            total += seg.DurationMs;
        Assert.AreEqual(180 * MsPerSec, total, 0.01);
        Assert.AreEqual(36, collector.Segments.Count); // 24 + 12 five-second spans

        // Repeated idle ticks attribute nothing.
        clock.NowMs = 190 * MsPerSec;
        collector.SampleTick("chrome", 120 * MsPerSec);
        Assert.AreEqual(36, collector.Segments.Count);
    }

    [TestMethod]
    public void BrokenSampleGap_ClosesWithoutBackfill_RebuildsBaseline()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);
        // 60 s gap (beyond 15 s) with input still fresh → trace lost.
        // The 0→60s span is unverified and never counted.
        clock.NowMs = 60 * MsPerSec;
        collector.SampleTick("chrome", 60 * MsPerSec);
        Assert.AreEqual(0, collector.Segments.Count);

        // New baseline: the verified tick at 60 s opens a fresh baseline;
        // the adjacent verified pair 60s→65s and 65s→70s commits 2 spans.
        clock.NowMs = 65 * MsPerSec;
        collector.SampleTick("chrome", 65 * MsPerSec);
        clock.NowMs = 70 * MsPerSec;
        collector.SampleTick("chrome", 70 * MsPerSec);
        collector.Disable();

        Assert.AreEqual(2, collector.Segments.Count);
        var total = 0.0;
        foreach (var seg in collector.Segments)
            total += seg.DurationMs;
        // Only the verified spans are counted; the 0→60s gap was discarded,
        // never back-filled.
        Assert.AreEqual(10 * MsPerSec, total, 0.01);
    }

    [TestMethod]
    public void NoExternalApp_DropsUnverifiedTail_WithoutMismatch()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec;
        // Foreground is the pet itself / desktop: no external app. The
        // [0,5s) span was never confirmed by an adjacent successful pair,
        // so nothing is committed and no mismatch is counted.
        collector.SampleTick(null, 5 * MsPerSec);

        Assert.AreEqual(0, collector.Segments.Count);
        Assert.AreEqual(0UL, collector.CoverageMismatchCount);

        // A later verified pair still works normally.
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("notepad", 10 * MsPerSec);
        clock.NowMs = 15 * MsPerSec;
        collector.SampleTick("notepad", 15 * MsPerSec);
        Assert.AreEqual(1, collector.Segments.Count);
        Assert.AreEqual(5 * MsPerSec, collector.Segments[0].DurationMs, 0.01);
    }

    // ── lifecycle events ───────────────────────────────────────────────

    [TestMethod]
    public void LifecycleEvents_AreDistinct()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.OnAppStart();
        collector.OnUnlock();
        collector.OnResume();

        var write = collector.DrainPending();
        Assert.IsNotNull(write);
        Assert.AreEqual(3, write!.Events.Count);
        Assert.AreEqual(UsageStatsEventTypes.AppStart, write.Events[0].EventType);
        Assert.AreEqual(UsageStatsEventTypes.Unlock, write.Events[1].EventType);
        Assert.AreEqual(UsageStatsEventTypes.Resume, write.Events[2].EventType);
        Assert.AreEqual("2026-11-26", write.Events[0].Date);
    }

    [TestMethod]
    public void Lock_SuspendsSampling_UnlockRebuildsBaseline()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("chrome", 5 * MsPerSec);

        // Lock: suspend. The locked interval must not be attributed.
        collector.SetLocked(true);
        Assert.IsTrue(collector.IsSuspended);
        Assert.AreEqual(1, collector.Segments.Count);

        // Clock jumps: locked span (mono 5s → 10 min) is not back-filled.
        clock.NowMs = 10 * MsPerMin;
        collector.OnUnlock();
        Assert.IsFalse(collector.IsSuspended);
        Assert.AreEqual(1, collector.Segments.Count);
        Assert.AreEqual(1, collector.Events.Count);
        Assert.AreEqual(UsageStatsEventTypes.Unlock, collector.Events[0].EventType);

        // New baseline: unlock clears the reason; the next adjacent verified
        // pair re-opens attribution.
        collector.SampleTick("chrome", 10 * MsPerMin);
        clock.NowMs = 10 * MsPerMin + 5 * MsPerSec;
        collector.SampleTick("chrome", clock.NowMs);
        collector.Disable();

        Assert.AreEqual(2, collector.Segments.Count);
        Assert.AreEqual(5 * MsPerSec, collector.Segments[1].DurationMs, 0.01);
    }

    // ── identification failure: single policy ──────────────────────────

    [TestMethod]
    public void ResolutionFailed_DropsTail_CountsMismatch_KeepsVerified()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("editor", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // commits [0,5s)
        // F-02 (监理 R1 反例): a failed foreground interval must not extend
        // the known app, and already-verified history stays.
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick(null, 10 * MsPerSec, resolutionFailed: true);

        Assert.AreEqual(1, collector.Segments.Count);
        Assert.AreEqual(5 * MsPerSec, collector.Segments[0].DurationMs, 0.01);
        Assert.AreEqual("editor", collector.Segments[0].AppId);
        Assert.AreEqual(1UL, collector.CoverageMismatchCount);

        var write = collector.DrainPending();
        Assert.IsNotNull(write);
        Assert.IsTrue(write!.CoverageMismatchChanged);
        Assert.AreEqual(1UL, write.CoverageMismatch);
    }

    // ── cross-midnight ─────────────────────────────────────────────────

    [TestMethod]
    public void CrossMidnight_SplitsIntoOneSegmentPerLocalDay()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 23, 59, 55, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0); // 23:59:55 local, mono 0
        clock.NowMs = 10 * MsPerSec;       // 00:00:05 local, mono 10 s
        collector.SampleTick("chrome", 10 * MsPerSec);
        collector.Disable();               // close → split at midnight

        Assert.AreEqual(2, collector.Segments.Count);
        var first = collector.Segments[0];
        var second = collector.Segments[1];
        Assert.AreEqual("2026-11-26", first.Date);
        Assert.AreEqual(5 * MsPerSec, first.DurationMs, 0.01);
        Assert.AreEqual("2026-11-27", second.Date);
        Assert.AreEqual(5 * MsPerSec, second.DurationMs, 0.01);
        // Monotonic durations never re-used across days; total is preserved.
        Assert.AreEqual(10 * MsPerSec, first.DurationMs + second.DurationMs, 0.01);
    }

    // ── retention / restore / clear guarding ───────────────────────────

    [TestMethod]
    public void PruneToRetention_RemovesOldCommittedData()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));

        // Load an archive with data spanning beyond the raw 7-day window.
        var stale = new UsageStatsArchive();
        stale.Segments.Add(new UsageStatsSegment
        {
            AppId = "old-a",
            Date = "2026-11-18", // expired: older than today−6 days
            StartLocal = "2026-11-18T09:00:00.0000000+08:00",
            DurationMs = 1000
        });
        stale.Segments.Add(new UsageStatsSegment
        {
            AppId = "old-b",
            Date = "2026-11-25", // within retention: 1 day before today
            StartLocal = "2026-11-25T09:00:00.0000000+08:00",
            DurationMs = 1000
        });
        stale.Events.Add(new UsageStatsEventRecord
        {
            EventType = UsageStatsEventTypes.Unlock,
            Date = "2026-11-18", // expired
            OccurredLocal = "2026-11-18T09:00:00.0000000+08:00"
        });

        var fresh = NewCollector(clock);
        fresh.Restore(stale);

        int removed = fresh.PruneToRetention();
        Assert.AreEqual(2, removed); // old-a segment + 11-18 event
        Assert.AreEqual(1, fresh.Segments.Count);  // old-b kept
        Assert.AreEqual(0, fresh.Events.Count);    // event expired
    }

    [TestMethod]
    public void Restore_IsCommitted_NotWrittenBackOnDrain()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var archive = new UsageStatsArchive();
        archive.Segments.Add(new UsageStatsSegment
        {
            AppId = "kept",
            Date = "2026-11-25",
            StartLocal = "2026-11-25T09:00:00.0000000+08:00",
            DurationMs = 1000
        });
        archive.Events.Add(new UsageStatsEventRecord
        {
            EventType = UsageStatsEventTypes.AppStart,
            Date = "2026-11-25",
            OccurredLocal = "2026-11-25T09:00:00.0000000+08:00"
        });

        var collector = NewCollector(clock);
        collector.Restore(archive);

        Assert.AreEqual(1, collector.Segments.Count);
        Assert.AreEqual(1, collector.Events.Count);
        // Nothing new collected → nothing to persist (no rewrite of restored data).
        Assert.IsNull(collector.DrainPending());
    }

    [TestMethod]
    public void ClearAll_WipesDataAndPending_BumpsGeneration()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("chrome", 5 * MsPerSec);
        collector.SampleTick(null, 5 * MsPerSec, resolutionFailed: true);
        collector.OnUnlock();

        Assert.IsTrue(collector.Segments.Count > 0 || collector.Events.Count > 0);
        var genBefore = collector.Generation;

        collector.ClearAll();

        Assert.AreEqual(0, collector.Segments.Count);
        Assert.AreEqual(0, collector.Events.Count);
        Assert.AreEqual(0UL, collector.CoverageMismatchCount);
        Assert.IsNull(collector.DrainPending()); // pending wiped too
        Assert.IsTrue(collector.Generation > genBefore);
    }

    [TestMethod]
    public void WriteDrainedBeforeClear_CarriesOldGeneration_RejectedByGuard()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("chrome", 5 * MsPerSec);
        // App switch closes the chrome segment so a pending unit exists.
        collector.SampleTick("notepad", 5 * MsPerSec);

        var write = collector.DrainPending();
        Assert.IsNotNull(write);
        var genAtDrain = write!.Generation;
        Assert.AreEqual(collector.Generation, genAtDrain);

        // A clear happens before the App-side single write queue persists.
        collector.ClearAll();
        Assert.AreNotEqual(genAtDrain, collector.Generation);

        // Guard: an in-flight write from before the clear must be dropped.
        Assert.AreNotEqual(write.Generation, collector.Generation);
    }

    [TestMethod]
    public void DataAfterClear_IsNewData_NotResurrection()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("chrome", 5 * MsPerSec);
        collector.DrainPending();

        collector.ClearAll();
        Assert.AreEqual(0, collector.Segments.Count);

        // Post-clear sampling is a brand-new record.
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("notepad", 10 * MsPerSec);
        clock.NowMs = 15 * MsPerSec;
        collector.SampleTick("notepad", 15 * MsPerSec);
        collector.Disable();

        Assert.AreEqual(1, collector.Segments.Count);
        Assert.AreEqual("notepad", collector.Segments[0].AppId);
        Assert.AreEqual(5 * MsPerSec, collector.Segments[0].DurationMs, 0.01);
    }

    // ── F-02 监理 R1 反例等价：采样有效性 / 长缺口 / 挂起组合 ─────────

    [TestMethod]
    [DataRow(0.0, 0)]          // null
    [DataRow(1.0, 1)]          // NaN
    [DataRow(2.0, 2)]          // +Infinity
    [DataRow(100000.0, 3)]     // far-future timestamp
    public void InvalidInput_ZeroAccumulation_EveryBadTickResets(double badValue, int caseId)
    {
        _ = caseId;
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        double? missing = badValue switch
        {
            0.0 => null,
            1.0 => double.NaN,
            2.0 => double.PositiveInfinity,
            _ => badValue
        };

        // 监理 R1 反例: SameTick appId editor, then switch to browser.
        // Every bad reading resets the baseline; nothing is ever committed.
        clock.NowMs = 0;
        collector.SampleTick("editor", missing);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", missing);
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("browser", missing);

        Assert.AreEqual(0, collector.Segments.Count);
        Assert.IsNull(collector.DrainPending());
    }

    [TestMethod]
    public void LongGap_PreservesPreviouslyVerifiedDuration()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        // 监理 R1 反例: editor at 0/5k/10k (verified 10000), then a 60000
        // gap. The gap resets the baseline; the still-unverified tail after
        // the last successful sample is dropped — never back-filled.
        collector.SampleTick("editor", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec);
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("editor", 10 * MsPerSec);
        clock.NowMs = 60 * MsPerSec;
        collector.SampleTick("editor", 60 * MsPerSec); // gap > 15 s → baseline rebuilt
        collector.Disable();

        var total = 0.0;
        foreach (var seg in collector.Segments)
            total += seg.DurationMs;
        Assert.AreEqual(10 * MsPerSec, total, 0.01);
        Assert.AreEqual(2, collector.Segments.Count); // [0,5s)+[5s,10s)
    }

    [TestMethod]
    public void SuspendedTicks_NeverAccumulate_AnyReason()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        // Two verified active ticks → one committed span.
        collector.SampleTick("editor", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec);
        var before = SumDurations(collector);
        Assert.AreEqual(5 * MsPerSec, before, 0.01);

        // 监理 R1 反例: pause → two suspended ticks → Disable. The suspended
        // interval is never attributed; the total must not change.
        collector.SetPaused(true);
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("editor", 10 * MsPerSec);
        clock.NowMs = 15 * MsPerSec;
        collector.SampleTick("editor", 15 * MsPerSec);
        collector.Disable();
        Assert.AreEqual(before, SumDurations(collector), 0.01);
    }

    [TestMethod]
    public void PauseLockSleep_IndependentReasons_CombinedSuspension()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        var before = SumDurations(collector);

        // All three reasons active: suspends (also verifies the combined
        // predicate). No order dependency.
        collector.SetSleeping(true);
        Assert.IsTrue(collector.IsSuspended);
        collector.SetLocked(true);
        Assert.IsTrue(collector.IsSuspended);
        collector.SetPaused(true);
        Assert.IsTrue(collector.IsSuspended);

        // A tick while all reasons active accumulates nothing.
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec);
        Assert.AreEqual(before, SumDurations(collector), 0.01);

        // Clearing ONE reason must NOT resume while another remains.
        collector.SetPaused(false);
        Assert.IsTrue(collector.IsSuspended);
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("editor", 10 * MsPerSec);
        Assert.AreEqual(before, SumDurations(collector), 0.01);

        // Clearing all reasons resumes sampling.
        collector.SetLocked(false);
        Assert.IsTrue(collector.IsSuspended); // sleep still active
        collector.SetSleeping(false);
        Assert.IsFalse(collector.IsSuspended);

        // A verified pair after release commits a new span (baseline rebuilt).
        clock.NowMs = 15 * MsPerSec;
        collector.SampleTick("editor", 15 * MsPerSec);
        clock.NowMs = 20 * MsPerSec;
        collector.SampleTick("editor", 20 * MsPerSec);
        Assert.AreEqual(before + 5 * MsPerSec, SumDurations(collector), 0.01);
    }

    [TestMethod]
    public void UnlockOrResume_WhileOtherReasonsActive_StaysSuspended()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SetLocked(true);
        collector.SetSleeping(true);
        var before = SumDurations(collector);

        // Unlock only clears the lock reason; sleep keeps sampling gated.
        collector.OnUnlock();
        Assert.IsTrue(collector.IsSuspended);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec);
        Assert.AreEqual(before, SumDurations(collector), 0.01);
        Assert.AreEqual(1, collector.Events.Count); // unlock event still recorded

        // Resume only clears the sleep reason; now fully released.
        collector.OnResume();
        Assert.IsFalse(collector.IsSuspended);
        Assert.AreEqual(2, collector.Events.Count); // resume event recorded
    }

    [TestMethod]
    public void VerifiedInterval_Checkpointed_WhileTraceContinues()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        // 监理 R1 反例: three controlled ticks → the first two intervals
        // are already committed to Segments (a snapshot checkpoint exists
        // before any shutdown), not only held in a pending tail.
        collector.SampleTick("editor", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec);
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("editor", 10 * MsPerSec);

        Assert.IsTrue(collector.Segments.Count > 0);
        Assert.AreEqual(2, collector.Segments.Count);
        Assert.AreEqual(10 * MsPerSec, SumDurations(collector), 0.01);
    }

    // ── F-02 R2 收尾：单调回退防御 ─────────────────────────────────────

    [TestMethod]
    public void MonotonicRollback_DropsTail_NoReaccumulation_ResumeForward()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        // 监理 R2 反例序列：now 0 → 5000 → 1000 → 5000（时钟回退）。
        // 已提交区间 [0,5000) 保持；回退 tick 不得降低已验证时间边界，
        // 不得把 [1000,5000) 重复计入；回退后恢复的 tick 只重建基线。
        collector.SampleTick("editor", 0);          // 0 → 基线
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // 提交 [0,5000)
        clock.NowMs = 1 * MsPerSec;
        collector.SampleTick("editor", 1 * MsPerSec); // 回退 → ResetBaseline + 不计
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // 回退后同点 → 仅新基线，不重复计

        Assert.AreEqual(1, collector.Segments.Count, "回退不得再造出重复区间。");
        Assert.AreEqual(5 * MsPerSec, SumDurations(collector), 0.01,
            "已验证的 [0,5000) 必须保持，回退区间不得重复累计。");

        // 恢复正向推进：仅在相邻成功 tick 间提交新区间 [5000,10000)、
        // [10000,15000)。
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("editor", 10 * MsPerSec);
        clock.NowMs = 15 * MsPerSec;
        collector.SampleTick("editor", 15 * MsPerSec);
        collector.Disable();

        Assert.AreEqual(3, collector.Segments.Count, "恢复推进后只提交新确认区间。");
        Assert.AreEqual(15 * MsPerSec, SumDurations(collector), 0.01,
            "总时长 = 回退前 5s + 恢复后 10s，无重复累计、无吞损。");
        Assert.AreEqual(5 * MsPerSec, collector.Segments[1].DurationMs, 0.01,
            "恢复后第一段正是 [5000,10000)，回退点未重复计算。");
    }

    [TestMethod]
    public void MonotonicForward_NormalTicks_AccumulateExactly()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        // 正向序列对照：同样 0→5000→1000→5000 之外的正常推进，时长必须精确。
        collector.SampleTick("editor", 0);
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec);
        clock.NowMs = 10 * MsPerSec;
        collector.SampleTick("editor", 10 * MsPerSec);
        clock.NowMs = 15 * MsPerSec;
        collector.SampleTick("editor", 15 * MsPerSec);
        collector.Disable();

        Assert.AreEqual(3, collector.Segments.Count);
        Assert.AreEqual(15 * MsPerSec, SumDurations(collector), 0.01);
    }

    // ── F-02 R3 收尾：连续回退窗口不得重建可重复累计的基线 ─────────────

    [TestMethod]
    public void RollbackWindow_ConsecutiveBelowFloor_NeverRebuildsReaccumulatingBaseline()
    {
        // 监理 R3 反例序列：now 0 → 5000 → 1000 → 2000 → 5000。
        // 回退后 2000 不得被当作"新基线"（否则 5000 会把已计过的
        // [2000,5000) 重复累计成 8000）。已经计时上界（floor=5000）在
        // 回退后仍然保留：0/2000 连续低于上界的 tick 一律拒绝建基线。
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("editor", 0);          // 0 → 基线（floor=0）
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // 提交 [0,5000)，floor=5000
        clock.NowMs = 1 * MsPerSec;
        collector.SampleTick("editor", 1 * MsPerSec); // 回退 → 拒绝，floor 保留
        clock.NowMs = 2 * MsPerSec;
        collector.SampleTick("editor", 2 * MsPerSec); // 仍低于 floor → 拒绝，不重建基线
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // 回到上界 → 仅重建基线，不重复计
        collector.Disable();

        Assert.AreEqual(1, collector.Segments.Count,
            "连续低于上界的 tick 不得重建可重复累计的基线。");
        Assert.AreEqual(5 * MsPerSec, SumDurations(collector), 0.01,
            "R3 反例：已计 5000ms 保持，回退窗口不得重复累计成 8000ms。");
    }

    [TestMethod]
    public void RollbackWindow_PersistentlyBelowFloor_MultipleTicksStayRejected()
    {
        // 持续低于上界的多个 tick（1000/2000/3000/4000）全部拒绝；
        // 回到上界 5000 才重建基线；越过上界恢复推进只计未计过的区间。
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("editor", 0);            // floor=0
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // [0,5000)，floor=5000
        foreach (var below in new[] { 1.0, 2.0, 3.0, 4.0 })
        {
            clock.NowMs = below * MsPerSec;
            collector.SampleTick("editor", below * MsPerSec); // 连续拒绝
        }
        Assert.AreEqual(1, collector.Segments.Count,
            "持续回退期间不应有任何新区间被提交。");
        Assert.AreEqual(5 * MsPerSec, SumDurations(collector), 0.01);

        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // 回到上界 → 重建基线
        clock.NowMs = 6 * MsPerSec;
        collector.SampleTick("editor", 6 * MsPerSec); // 提交 [5000,6000)
        collector.Disable();

        Assert.AreEqual(2, collector.Segments.Count,
            "回到上界后下一次相邻成功采样才提交新区间。");
        Assert.AreEqual(6 * MsPerSec, SumDurations(collector), 0.01,
            "已计 5000ms + 恢复后未计过的 1000ms，无重复累计。");
        Assert.AreEqual(1 * MsPerSec, collector.Segments[1].DurationMs, 0.01,
            "恢复后第一段正是 [5000,6000)。");
    }

    [TestMethod]
    public void RollbackWindow_JumpAboveFloor_ResumesAsOrdinaryGapRebuild()
    {
        // 越过上界（不经过上界点）恢复：直接跳到 floor 之上的 6000，
        // 按既定缺口重建规则处理——新样本只作基线，后续相邻成功采样
        // 只累计未计过的可靠区间 [6000,7000)。
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("editor", 0);            // floor=0
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // [0,5000)，floor=5000
        clock.NowMs = 1 * MsPerSec;
        collector.SampleTick("editor", 1 * MsPerSec); // 回退拒绝
        clock.NowMs = 2 * MsPerSec;
        collector.SampleTick("editor", 2 * MsPerSec); // 回退拒绝
        clock.NowMs = 6 * MsPerSec;
        collector.SampleTick("editor", 6 * MsPerSec); // 越过上界 → 重建基线
        clock.NowMs = 7 * MsPerSec;
        collector.SampleTick("editor", 7 * MsPerSec); // 提交 [6000,7000)
        collector.Disable();

        Assert.AreEqual(2, collector.Segments.Count,
            "越过上界后首个样本仅为基线，第二个成功样本才提交新区间。");
        Assert.AreEqual(6 * MsPerSec, SumDurations(collector), 0.01,
            "5000ms 已计时 + 1000ms 未计过的新区间。");
    }

    [TestMethod]
    public void RollbackFloor_Lifecycle_EnableDisableKeeps_ClearResets()
    {
        // 生命周期：disable→enable 不重置已计时上界（同一单调时间域，
        // 已提交数据必须继续受防重计保护）；ClearAll 重置上界（清除后
        // 是新时间域，新记录从当前时间点正常建立，不受旧上界阻挡）。
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        collector.SampleTick("editor", 0);            // floor=0
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // [0,5000)，floor=5000
        collector.Disable();                          // 丢未确认尾部，floor 保留

        collector.Enable();
        clock.NowMs = 1 * MsPerSec;
        collector.SampleTick("editor", 1 * MsPerSec); // 回退到 disable 前的上界之下 → 拒绝
        Assert.AreEqual(1, collector.Segments.Count,
            "disable→enable 保留已计时上界：回退 tick 不得重建可重计基线。");
        Assert.AreEqual(5 * MsPerSec, SumDurations(collector), 0.01);

        collector.ClearAll();                         // 新时间域：floor 重置
        clock.NowMs = 0;
        collector.SampleTick("editor", 0);            // 新域基线
        clock.NowMs = 5 * MsPerSec;
        collector.SampleTick("editor", 5 * MsPerSec); // 新域提交 [0,5000)
        collector.Disable();

        Assert.AreEqual(1, collector.Segments.Count,
            "清除后新时间域从当前时间正常建立基线并累计。");
        Assert.AreEqual(5 * MsPerSec, SumDurations(collector), 0.01,
            "清除后的新记录不受旧已计时上界阻挡，也不是旧数据复生。");
    }

    private static double SumDurations(UsageStatsCollector collector)
    {
        double total = 0.0;
        foreach (var seg in collector.Segments)
            total += seg.DurationMs;
        return total;
    }

    // ── P6-002: 30-minute bucket aggregation ───────────────────────────

    /// <summary>Finds the single bucket for app id and (optionally) bucket start.</summary>
    private static UsageStatsBucket? FindBucket(
        UsageStatsCollector collector, string appId, string? bucketStartLocal = null)
    {
        foreach (var b in collector.Buckets)
        {
            if (b.AppId == appId &&
                (bucketStartLocal is null || b.BucketStartLocal == bucketStartLocal))
                return b;
        }
        return null;
    }

    [TestMethod]
    public void Aggregation_ManySamplesInOneBucket_OneSessionNotPerSample()
    {
        // 10:00 local, all samples inside the [10:00,10:30) bucket.
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        for (int t = 0; t <= 55; t += 5) // 12 samples → 11 committed 5 s spans
        {
            clock.NowMs = t * MsPerSec;
            collector.SampleTick("chrome", clock.NowMs);
        }
        collector.Disable();

        Assert.AreEqual(1, collector.Buckets.Count, "整段落在同一个 30 分钟桶内。");
        var bucket = collector.Buckets[0];
        Assert.AreEqual("chrome", bucket.AppId);
        Assert.AreEqual("2026-11-26", bucket.Date);
        Assert.AreEqual("2026-11-26T10:00:00.0000000+08:00", bucket.BucketStartLocal);
        Assert.AreEqual(55 * MsPerSec, bucket.ActiveMs, 0.01);
        Assert.AreEqual(1, bucket.SessionCount, "连续会话只计 1 次使用，不按 5 秒采样计次。");
    }

    [TestMethod]
    public void Aggregation_CrossBucket_OneSessionSplitDuration_SessionCountAtStart()
    {
        // Starts 10:28, crosses the 10:30 bucket boundary into 10:32.
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 28, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        for (int t = 0; t <= 240; t += 5)
        {
            clock.NowMs = t * MsPerSec;
            collector.SampleTick("chrome", clock.NowMs);
        }
        collector.Disable();

        var first = FindBucket(collector, "chrome", "2026-11-26T10:00:00.0000000+08:00");
        var second = FindBucket(collector, "chrome", "2026-11-26T10:30:00.0000000+08:00");
        Assert.IsNotNull(first, "10:28–10:30 应落在 10:00 桶。");
        Assert.IsNotNull(second, "10:30–10:32 应落在 10:30 桶。");
        Assert.AreEqual(120 * MsPerSec, first!.ActiveMs, 0.01);
        Assert.AreEqual(120 * MsPerSec, second!.ActiveMs, 0.01);
        Assert.AreEqual(1, first.SessionCount, "会话起始桶计 1 次使用。");
        Assert.AreEqual(0, second.SessionCount, "跨桶时长分裂但会话只计一次，不重复。");
        Assert.AreEqual(2, collector.Buckets.Count);
    }

    [TestMethod]
    public void Aggregation_CrossMidnight_SessionCountOnStartDayOnly()
    {
        // 23:58 → 00:02 across local midnight, one continuous session.
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 23, 58, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        for (int t = 0; t <= 240; t += 5)
        {
            clock.NowMs = t * MsPerSec;
            collector.SampleTick("chrome", clock.NowMs);
        }
        collector.Disable();

        var day1 = FindBucket(collector, "chrome", "2026-11-26T23:30:00.0000000+08:00");
        var day2 = FindBucket(collector, "chrome", "2026-11-27T00:00:00.0000000+08:00");
        Assert.IsNotNull(day1, "午夜前 23:58–24:00 应计入 11-26 的 23:30 桶。");
        Assert.IsNotNull(day2, "午夜后 00:00–00:02 应计入 11-27 的 00:00 桶。");
        Assert.AreEqual(120 * MsPerSec, day1!.ActiveMs, 0.01);
        Assert.AreEqual(120 * MsPerSec, day2!.ActiveMs, 0.01);
        Assert.AreEqual(1, day1.SessionCount, "会话起始日计 1 次使用。");
        Assert.AreEqual(0, day2.SessionCount, "跨午夜不重复计次。");
    }

    [TestMethod]
    public void Aggregation_AppSwitch_StartsNewSession()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        clock.NowMs = 0; collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec; collector.SampleTick("chrome", 5 * MsPerSec);
        clock.NowMs = 10 * MsPerSec; collector.SampleTick("chrome", 10 * MsPerSec);
        clock.NowMs = 15 * MsPerSec; collector.SampleTick("notepad", 15 * MsPerSec);
        clock.NowMs = 20 * MsPerSec; collector.SampleTick("notepad", 20 * MsPerSec);
        collector.Disable();

        Assert.AreEqual(1, FindBucket(collector, "chrome")!.SessionCount);
        Assert.AreEqual(1, FindBucket(collector, "notepad")!.SessionCount,
            "切换到另一应用后开始新会话。");
    }

    [TestMethod]
    public void Aggregation_IdleInterruption_StartsNewSession()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();

        // Active while age <= 60 s (last input pinned at 0), then idle past
        // the 60 s threshold, then a fresh active run.
        for (int t = 0; t <= 60; t += 5)
        {
            clock.NowMs = t * MsPerSec;
            collector.SampleTick("chrome", 0);
        }
        clock.NowMs = 65 * MsPerSec; collector.SampleTick("chrome", 0); // idle → reset
        clock.NowMs = 70 * MsPerSec; collector.SampleTick("chrome", 70 * MsPerSec); // baseline
        clock.NowMs = 75 * MsPerSec; collector.SampleTick("chrome", 75 * MsPerSec); // commit
        collector.Disable();

        var bucket = FindBucket(collector, "chrome", "2026-11-26T10:00:00.0000000+08:00");
        Assert.IsNotNull(bucket);
        Assert.AreEqual(2, bucket!.SessionCount, "空闲中断后重新活跃算新的使用会话。");
    }

    [TestMethod]
    public void Aggregation_ThirtyDayRetention_ByCountedDate_IndependentOfRaw7Days()
    {
        // "Today" = 2026-11-26. Raw cutoff = 11-20; aggregated cutoff = 10-28.
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var archive = new UsageStatsArchive();
        archive.Segments.Add(new UsageStatsSegment
        {
            AppId = "chrome", Date = "2026-11-10", // 16 days old: raw expired
            StartLocal = "2026-11-10T09:00:00.0000000+08:00", DurationMs = 1000
        });
        archive.Buckets.Add(new UsageStatsBucket
        {
            AppId = "chrome", Date = "2026-11-10", // 16 days old: within 30-day aggregate
            BucketStartLocal = "2026-11-10T09:00:00.0000000+08:00",
            ActiveMs = 1000, SessionCount = 1
        });
        archive.Buckets.Add(new UsageStatsBucket
        {
            AppId = "chrome", Date = "2026-10-20", // 37 days old: aggregate expired
            BucketStartLocal = "2026-10-20T09:00:00.0000000+08:00",
            ActiveMs = 2000, SessionCount = 1
        });

        var collector = NewCollector(clock);
        collector.Restore(archive);
        Assert.AreEqual(2, collector.Buckets.Count, "恢复后两桶均在。");

        var removed = collector.PruneToRetention();
        Assert.AreEqual(2, removed, "1 原始段 + 1 过期桶被清理。");
        Assert.AreEqual(0, collector.Segments.Count, "原始 7 天边界先过期。");
        Assert.AreEqual(1, collector.Buckets.Count,
            "16 天前的桶仍在 30 天聚合保留期内，不因原始过期而删。");
        Assert.AreEqual("2026-11-10", collector.Buckets[0].Date);
    }

    [TestMethod]
    public void ClearAll_ClearsBuckets_NoPendingResurrection()
    {
        var clock = NewClock(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(8)));
        var collector = NewCollector(clock);
        collector.Enable();
        clock.NowMs = 0; collector.SampleTick("chrome", 0);
        clock.NowMs = 5 * MsPerSec; collector.SampleTick("chrome", 5 * MsPerSec);
        Assert.AreEqual(1, collector.Buckets.Count);
        var gen = collector.Generation;
        var drained = collector.DrainPending();
        Assert.IsNotNull(drained);
        Assert.AreEqual(gen, drained!.Generation);

        collector.ClearAll();
        Assert.AreEqual(0, collector.Buckets.Count);
        Assert.IsTrue(collector.Generation > gen);
        Assert.IsNull(collector.DrainPending(), "清除后无待写，旧代次不会复生。");
    }
}