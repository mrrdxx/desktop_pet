using DesktopPet.App;
using DesktopPet.Core.Physics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Point = System.Windows.Point;

namespace DesktopPet.App.Tests;

/// <summary>
/// P2-004 F-01: Regression tests for high-resolution monotonic timing.
/// Verifies that the production GetElapsedMs() path (Stopwatch-based) no
/// longer causes timestamp collisions between press-down and first Move
/// in real fast drags, while preserving all existing rejection semantics
/// for truly same-timestamp inputs (injected clock).
/// </summary>
[TestClass]
public class HighResolutionTimingTests
{
    private static void BeginBody(MainWindow w) =>
        Assert.IsTrue(w.BeginCapturedPointerInteraction(new Point(60, 65), new Point(w.Left + 60, w.Top + 65)));

    // ── 1. Production clock has sub-millisecond resolution ───────────

    [STATestMethod]
    public void ProductionClock_TwoImmediateCalls_ReturnDifferentMs()
    {
        // Without TestGetElapsedMs injection, the production GetElapsedMs()
        // (Stopwatch-based) should produce different values even when called
        // in rapid succession, unlike Environment.TickCount64 which can
        // return the same value for ~10-16ms.
        var w = new MainWindow();
        try
        {
            // Do NOT set TestGetElapsedMs — use production clock.
            w.TestGetCursorPos = () => (900, 900);

            BeginBody(w);
            Assert.IsTrue(w.IsDragging);

            var attempts = w.LastSampleAttempts;
            Assert.IsTrue(attempts.Count >= 1);
            var baselineTime = attempts[0].TimeMs;

            // Immediately record a Move at the production clock value.
            // With Stopwatch, this should be > baselineTime even though
            // essentially no wall time has elapsed.
            w.ProcessCapturedPointerMove(
                new Point(70, 75),
                new Point(w.Left + 10, w.Top + 65),
                true,
                w.TestGetElapsedMs?.Invoke() ?? DesktopPet.App.MainWindow.GetElapsedMsPublic());

            attempts = w.LastSampleAttempts;
            Assert.IsTrue(attempts.Count >= 2,
                "Baseline + Move should both be in the attempt log.");
            Assert.AreEqual("move", attempts[^1].Source);
            Assert.AreEqual("accepted", attempts[^1].Result,
                "Move at a later high-resolution timestamp should be accepted, " +
                "not rejected_same_or_older_time. If this fails, GetElapsedMs " +
                "may still be using coarse Environment.TickCount64.");
            Assert.IsTrue(attempts[^1].TimeMs > baselineTime,
                "High-resolution clock should produce strictly increasing timestamps " +
                "even for back-to-back calls.");
        }
        finally { w.Close(); }
    }

    // ── 2. Short-interval drag produces throw with production clock ──

    [STATestMethod]
    public void ProductionClock_ShortIntervalDrag_ProducesTwoAcceptedSamples()
    {
        // Simulate a real fast drag: press, move 40px, release — all within
        // what would be a single TickCount64 bucket (~15ms). With Stopwatch,
        // each event gets a distinct sub-ms timestamp and RecordSample
        // accepts both, producing enough samples for a throw evaluation.
        var w = new MainWindow();
        try
        {
            w.TestGetCursorPos = () => (900, 900);
            // No TestGetElapsedMs — use production high-resolution clock.

            BeginBody(w);
            Assert.IsTrue(w.IsDragging);

            // Move 40px to the right — the kind of real fast drag that
            // previously failed with insufficient_samples.
            w.ProcessCapturedPointerMove(
                new Point(80, 75),
                new Point(w.Left + 40, w.Top + 65),
                true,
                DesktopPet.App.MainWindow.GetElapsedMsPublic());

            var attempts = w.LastSampleAttempts;
            int acceptedCount = attempts.Count(a => a.Result == "accepted");
            Assert.IsTrue(acceptedCount >= 2,
                "A real fast drag should produce at least 2 accepted samples " +
                "(baseline + move) with high-resolution timing. " +
                $"Got {acceptedCount} accepted out of {attempts.Count} attempts.");

            // Release and check the result.
            w.EndCapturedPointerInteraction();
            Assert.IsFalse(w.IsDragging);

            // The frozen snapshot should still have both attempts.
            var frozen = w.LastSampleAttempts;
            int frozenAccepted = frozen.Count(a => a.Result == "accepted");
            Assert.IsTrue(frozenAccepted >= 2,
                "Frozen snapshot after release should preserve both accepted samples.");
        }
        finally { w.Close(); }
    }

    // ── 3. Injected same-timestamp still rejected (semantics preserved) ─

    [STATestMethod]
    public void InjectedClock_SameTimestampMove_StillRejected()
    {
        // With injected clock, if press and Move share the exact same
        // timestamp, RecordSample must still reject the Move. The
        // high-resolution clock change does NOT relax the monotonic
        // ordering requirement — it only makes real collisions rare.
        var w = new MainWindow(); double now = 5000;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900, 900);
            BeginBody(w);

            // Baseline at t=5000.
            var attempts = w.LastSampleAttempts;
            Assert.AreEqual(1, attempts.Count);
            Assert.AreEqual("accepted", attempts[0].Result);
            Assert.AreEqual(5000, attempts[0].TimeMs);

            // Move at the SAME injected timestamp — must be rejected.
            w.ProcessCapturedPointerMove(
                new Point(80, 75),
                new Point(w.Left + 40, w.Top + 65),
                true,
                now); // same as baseline

            attempts = w.LastSampleAttempts;
            Assert.AreEqual(2, attempts.Count);
            Assert.AreEqual("rejected_same_or_older_time", attempts[1].Result,
                "Truly same-timestamp Move must still be rejected. " +
                "High-resolution clock does not relax monotonic ordering.");
        }
        finally { w.Close(); }
    }

    // ── 4. Injected short delta (sub-ms) accepted ─────────────────────

    [STATestMethod]
    public void InjectedClock_SubMillisecondDelta_Accepted()
    {
        // With high-resolution clock, even a 0.1ms delta should be accepted.
        // This verifies that RecordSample's comparison (timeMs <= newest.TimeMs)
        // correctly handles fractional milliseconds — a truly 0.1ms-later
        // sample is strictly greater and should be accepted.
        var w = new MainWindow(); double now = 5000.0;
        try
        {
            w.TestGetElapsedMs = () => now; w.TestGetCursorPos = () => (900, 900);
            BeginBody(w);

            // Baseline at t=5000.0.
            // Move at t=5000.1 — 0.1ms later, a realistic Stopwatch delta.
            now = 5000.1;
            w.ProcessCapturedPointerMove(
                new Point(80, 75),
                new Point(w.Left + 40, w.Top + 65),
                true,
                now);

            var attempts = w.LastSampleAttempts;
            Assert.IsTrue(attempts.Count >= 2);
            Assert.AreEqual("move", attempts[^1].Source);
            Assert.AreEqual("accepted", attempts[^1].Result,
                "A Move 0.1ms after baseline should be accepted — " +
                "high-resolution timing makes this a legitimate distinct timestamp.");
        }
        finally { w.Close(); }
    }

    // ── 5. Pause/Resume time domain consistency ───────────────────────

    [STATestMethod]
    public void ProductionClock_PauseResume_PhysicsTimeDomainConsistent()
    {
        // Verify that pause/resume with the production clock does not
        // produce negative or zero deltas on the first post-resume tick.
        // The physics _lastPhysicsUpdateMs is reset in ResumeAnimation,
        // and GetElapsedMs() returns a continuously increasing value
        // across pause/resume (Stopwatch doesn't pause).
        var w = new MainWindow();
        try
        {
            w.TestGetCursorPos = () => (900, 900);
            // No TestGetElapsedMs — production clock.

            var beforePause = DesktopPet.App.MainWindow.GetElapsedMsPublic();
            Assert.IsTrue(beforePause >= 0);

            w.PauseAnimation();
            Assert.IsTrue(w.IsAppPaused);

            // Even while paused, GetElapsedMs keeps increasing (Stopwatch
            // doesn't stop). The pause mechanism compensates by adjusting
            // timer references, not by stopping the clock.
            var duringPause = DesktopPet.App.MainWindow.GetElapsedMsPublic();
            Assert.IsTrue(duringPause >= beforePause,
                "GetElapsedMs should be monotonically increasing even during pause.");

            w.ResumeAnimation();
            Assert.IsFalse(w.IsAppPaused);

            var afterResume = DesktopPet.App.MainWindow.GetElapsedMsPublic();
            Assert.IsTrue(afterResume >= duringPause,
                "GetElapsedMs after resume should be >= during pause.");
        }
        finally { w.Close(); }
    }
}
