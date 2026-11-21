using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P4-001 R4 F-01 counterexamples: verify that the first valid sample
/// after a missing measurement preserves its active/idle state, so the
/// next new input does not over-accumulate.
/// </summary>
[TestClass]
public class ReviewP4001R4
{
    /// <summary>
    /// R4 F-01: After a missing sample, the first valid sample is IDLE.
    /// The _pendingRebase branch must preserve the idle state
    /// (_idleStartMs = lastInputMs) so the next active tick takes the
    /// idle→active transition path (no accumulation of the gap).
    ///
    /// Scenario:
    ///   t=0      active, input=0      → baseline
    ///   t=1000   active, input=1000   → accumulate 1000ms (total 1000)
    ///   t=61000  active, input=61000  → accumulate 60000ms (total 61000)
    ///   t=62000  MISSING (null)        → rebase, _pendingRebase=true
    ///   t=63000  valid but IDLE (input=1000, age=62s > 60s active window)
    ///            → _pendingRebase: preserve idle state, no accumulate
    ///   t=64000  active, input=64000  → idle→active transition: no
    ///            accumulate the gap, just set baseline
    ///   t=65000  active, input=65000  → accumulate 1000ms (total 62000)
    ///
    /// BUG (before R4 fix): _pendingRebase cleared _idleStartMs, so
    /// t=64000 saw wasIdle=false (continuous active) and accumulated
    /// the full 63000→64000 gap, making total 62000 at t=64000 instead
    /// of staying at 61000. Then t=65000 added another 1000 → 63000.
    ///
    /// FIX (R4): _pendingRebase preserves idle state, so t=64000 takes
    /// the idle→active path (no accumulation), and t=65000 accumulates
    /// 1000ms → total 62000 (correct: only 1000ms of real active time
    /// after the break).
    /// </summary>
    [TestMethod]
    public void AfterMissing_FirstValidSampleIdle_NextActiveDoesNotOverAccumulate()
    {
        var session = new ActivitySession();

        // Build up accumulated active time with continuous 1s ticks.
        // 61 ticks of 1000ms = 61000ms accumulated.
        session.Tick(0, 0);
        for (var t = 1000; t <= 61000; t += 1000)
            session.Tick(t, t);
        Assert.AreEqual(61000d, session.AccumulatedActiveMs,
            "Should have 61000ms accumulated after continuous active period.");

        // Missing sample at t=62000 → _pendingRebase = true.
        session.Tick(62000, null);
        Assert.AreEqual(61000d, session.AccumulatedActiveMs,
            "Missing sample should not accumulate.");

        // First valid sample at t=63000, but user is IDLE (last input
        // was at 1000ms, age = 62000ms = 62s > 60s active window).
        // _pendingRebase should preserve idle state, not clear it.
        session.Tick(63000, 1000);
        Assert.AreEqual(61000d, session.AccumulatedActiveMs,
            "First valid sample after missing (idle) should not accumulate.");

        // New active input at t=64000 (input=64000, age=0).
        // With R4 fix: _idleStartMs was preserved, so wasIdle=true →
        // idle→active transition, no accumulation of the 63000→64000 gap.
        session.Tick(64000, 64000);
        Assert.AreEqual(61000d, session.AccumulatedActiveMs,
            "First active tick after idle rebase should not accumulate the gap " +
            "(idle→active transition). BUG: would be 62000 if _idleStartMs was cleared.");

        // Next active tick at t=65000 → accumulate 1000ms.
        session.Tick(65000, 65000);
        Assert.AreEqual(62000d, session.AccumulatedActiveMs,
            "Should accumulate 1000ms from the new baseline (64000→65000).");
    }

    /// <summary>
    /// R4 F-01: After a missing sample, the first valid sample is ACTIVE.
    /// The _pendingRebase branch should clear _idleStartMs (active state),
    /// and the next tick accumulates normally from the new baseline.
    ///
    /// Scenario:
    ///   t=0      active, input=0      → baseline
    ///   t=1000   active, input=1000   → accumulate 1000ms (total 1000)
    ///   t=2000   MISSING (null)        → rebase, _pendingRebase=true
    ///   t=3000   active, input=3000   → _pendingRebase: active → clear
    ///            idle, set baseline, no accumulate (total 1000)
    ///   t=4000   active, input=4000   → accumulate 1000ms (total 2000)
    /// </summary>
    [TestMethod]
    public void AfterMissing_FirstValidSampleActive_NextTickAccumulatesFromBaseline()
    {
        var session = new ActivitySession();

        session.Tick(0, 0);
        session.Tick(1000, 1000);
        Assert.AreEqual(1000d, session.AccumulatedActiveMs);

        // Missing sample.
        session.Tick(2000, null);
        Assert.AreEqual(1000d, session.AccumulatedActiveMs);

        // First valid sample is ACTIVE (input=3000, age=0).
        session.Tick(3000, 3000);
        Assert.AreEqual(1000d, session.AccumulatedActiveMs,
            "First valid sample after missing (active) should not accumulate.");

        // Next tick accumulates from the new baseline (3000→4000).
        session.Tick(4000, 4000);
        Assert.AreEqual(2000d, session.AccumulatedActiveMs,
            "Should accumulate 1000ms from the new baseline.");
    }
}
