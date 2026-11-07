using DesktopPet.App;
using DesktopPet.Core.Physics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Point = System.Windows.Point;

namespace DesktopPet.App.Tests;

/// <summary>
/// P2-004: Tests for the RecordSample attempt log snapshot lifecycle.
/// Verifies that LastSampleAttempts is live during drag, frozen after release,
/// and replaced on the next drag — without modifying any judgment logic or thresholds.
/// </summary>
[TestClass]
public class SampleAttemptSnapshotTests
{
    private static void BeginBody(MainWindow w) =>
        Assert.IsTrue(w.BeginCapturedPointerInteraction(new Point(60, 65), new Point(w.Left + 60, w.Top + 65)));

    // ── 1. Live during drag ──────────────────────────────────────────

    [STATestMethod]
    public void AttemptLog_LiveDuringDrag_ContainsBaselineAndMove()
    {
        var w = new MainWindow(); double now = 1000;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900, 900);
            BeginBody(w);
            Assert.IsTrue(w.IsDragging);

            // StartDragging records a baseline sample at press time (now=1000).
            var live = w.LastSampleAttempts;
            Assert.IsTrue(live.Count >= 1, "Baseline attempt should be logged at drag start.");
            Assert.AreEqual("baseline", live[0].Source);
            Assert.AreEqual("accepted", live[0].Result);

            // Simulate a Move at a later timestamp.
            now = 1020;
            w.ProcessCapturedPointerMove(new Point(75, 75), new Point(82, 82), true, now);

            live = w.LastSampleAttempts;
            Assert.IsTrue(live.Count >= 2, "Baseline + Move should both be in the attempt log.");
            Assert.AreEqual("move", live[^1].Source);
            Assert.AreEqual("accepted", live[^1].Result);
        }
        finally { w.Close(); }
    }

    // ── 2. Frozen after release ──────────────────────────────────────

    [STATestMethod]
    public void AttemptLog_FrozenAfterRelease_SurvivesReset()
    {
        var w = new MainWindow(); double now = 1000;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900, 900);
            BeginBody(w);

            // Record a move with a real time delta so it is accepted.
            now = 1020;
            w.ProcessCapturedPointerMove(new Point(75, 75), new Point(82, 82), true, now);

            var beforeRelease = w.LastSampleAttempts;
            var countBefore = beforeRelease.Count;
            Assert.IsTrue(countBefore >= 2, "Need at least baseline + move before release.");

            // Release — EndDrag should freeze the snapshot before Reset.
            now = 1030;
            w.EndCapturedPointerInteraction();
            Assert.IsFalse(w.IsDragging);

            var frozen = w.LastSampleAttempts;
            Assert.AreEqual(countBefore, frozen.Count,
                "Frozen snapshot after release should have the same count as before release.");
            Assert.AreEqual("baseline", frozen[0].Source);
            Assert.AreEqual("accepted", frozen[0].Result);

            // The frozen snapshot must remain stable across subsequent ticks.
            now = 1100;
            var stillFrozen = w.LastSampleAttempts;
            Assert.AreEqual(countBefore, stillFrozen.Count,
                "Frozen snapshot must not change across ticks after release.");
        }
        finally { w.Close(); }
    }

    // ── 3. Replaced on next drag ─────────────────────────────────────

    [STATestMethod]
    public void AttemptLog_ReplacedOnNextDrag_OldSnapshotGone()
    {
        var w = new MainWindow(); double now = 1000;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900, 900);

            // First drag: baseline + move + release.
            BeginBody(w);
            now = 1020;
            w.ProcessCapturedPointerMove(new Point(75, 75), new Point(82, 82), true, now);
            now = 1030;
            w.EndCapturedPointerInteraction();
            Assert.IsFalse(w.IsDragging);

            var firstFrozen = w.LastSampleAttempts;
            var firstCount = firstFrozen.Count;
            Assert.IsTrue(firstCount >= 2, "First drag should have baseline + move.");

            // Second drag: new baseline at a different time.
            now = 2000;
            BeginBody(w);
            Assert.IsTrue(w.IsDragging);

            var secondLive = w.LastSampleAttempts;
            Assert.IsTrue(secondLive.Count >= 1, "Second drag should start with a new baseline.");
            Assert.AreEqual("baseline", secondLive[0].Source);
            Assert.AreEqual(2000, secondLive[0].TimeMs,
                "Second drag baseline should be at now=2000 (press time), not the old drag's time.");

            // Release the second drag.
            now = 2020;
            w.ProcessCapturedPointerMove(new Point(75, 75), new Point(90, 90), true, now);
            now = 2030;
            w.EndCapturedPointerInteraction();

            var secondFrozen = w.LastSampleAttempts;
            Assert.IsTrue(secondFrozen.Count >= 2, "Second drag frozen snapshot should have baseline + move.");
            Assert.AreNotEqual(firstCount, 0);
            // The second frozen snapshot's baseline time should differ from the first.
            Assert.AreEqual(2000, secondFrozen[0].TimeMs,
                "Second drag frozen snapshot should reflect the second drag, not the first.");
        }
        finally { w.Close(); }
    }

    // ── 4. Same-timestamp rejection is captured ──────────────────────

    [STATestMethod]
    public void AttemptLog_SameTimestampMove_RejectionRecorded()
    {
        var w = new MainWindow(); double now = 1000;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900, 900);
            BeginBody(w);

            // Baseline is recorded at now=1000 (the press time).
            var live = w.LastSampleAttempts;
            Assert.AreEqual(1, live.Count);
            Assert.AreEqual("baseline", live[0].Source);
            Assert.AreEqual("accepted", live[0].Result);
            var baselineTime = live[0].TimeMs;

            // Move at the SAME timestamp — should be rejected.
            w.ProcessCapturedPointerMove(new Point(75, 75), new Point(82, 82), true, baselineTime);

            live = w.LastSampleAttempts;
            Assert.AreEqual(2, live.Count,
                "Both baseline (accepted) and move (rejected) should be in the attempt log.");
            Assert.AreEqual("move", live[1].Source);
            Assert.AreEqual("rejected_same_or_older_time", live[1].Result);
            Assert.AreEqual(baselineTime, live[1].TimeMs,
                "Rejected move should have the same timeMs as the baseline.");
            Assert.AreEqual(baselineTime, live[1].NewestBeforeTimeMs,
                "NewestBeforeTimeMs should be the baseline's time.");
            Assert.AreEqual(1, live[1].CountBefore,
                "Buffer had 1 sample (baseline) before the rejected move.");
            Assert.AreEqual(1, live[1].CountAfter,
                "Buffer still has 1 sample after the rejected move.");

            // Release — frozen snapshot should preserve the rejection.
            now = 1010;
            w.EndCapturedPointerInteraction();

            var frozen = w.LastSampleAttempts;
            Assert.AreEqual(2, frozen.Count);
            Assert.AreEqual("rejected_same_or_older_time", frozen[1].Result);
        }
        finally { w.Close(); }
    }

    // ── 5. Cancellation also freezes ─────────────────────────────────

    [STATestMethod]
    public void AttemptLog_CancellationPath_AlsoFreezesSnapshot()
    {
        var w = new MainWindow(); double now = 1000;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900, 900);
            BeginBody(w);
            now = 1020;
            w.ProcessCapturedPointerMove(new Point(75, 75), new Point(82, 82), true, now);

            var beforeCancel = w.LastSampleAttempts.Count;
            Assert.IsTrue(beforeCancel >= 2);

            // Cancel (e.g. capture loss) — should also freeze before Reset.
            w.EndCapturedPointerInteraction(isCancellation: true);
            Assert.IsFalse(w.IsDragging);

            var frozen = w.LastSampleAttempts;
            Assert.AreEqual(beforeCancel, frozen.Count,
                "Cancellation should also freeze the attempt log snapshot.");
        }
        finally { w.Close(); }
    }

    // ── 6. Empty before first drag ───────────────────────────────────

    [STATestMethod]
    public void AttemptLog_EmptyBeforeFirstDrag()
    {
        var w = new MainWindow();
        try
        {
            Assert.IsFalse(w.IsDragging);
            var empty = w.LastSampleAttempts;
            Assert.AreEqual(0, empty.Count,
                "LastSampleAttempts should be empty before any drag has occurred.");
        }
        finally { w.Close(); }
    }
}
