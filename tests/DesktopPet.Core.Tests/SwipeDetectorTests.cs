using DesktopPet.Core.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Tests for SwipeDetector: speed+distance trigger, cooldown,
/// "fast swipe from far then slowly approach" false positive prevention,
/// and interrupt reset.
///
/// Uses a controlled clock (explicit timestamps) and fixed input
/// sequences — no real mouse or timer dependencies.
/// </summary>
[TestClass]
public class SwipeDetectorTests
{
    private static MouseSample S(double x, double y, double t) => new(x, y, t);

    // Pet at (500,500) size 120x120 → covers [500,620] x [500,620]
    private static readonly PetRectPx _petRect = new(500, 500, 120, 120);

    /// <summary>
    /// Helper: feed a sequence of samples through MouseSampler and return
    /// the last result, then feed it to SwipeDetector.
    /// </summary>
    private static (MouseSampleResult result, SwipeTrigger trigger) FeedSequence(
        SwipeDetector detector, MouseSampler sampler,
        double[][] points, PetRectPx rect)
    {
        MouseSampleResult result = MouseSampleResult.Invalid;
        SwipeTrigger trigger = SwipeTrigger.None;

        foreach (var p in points)
        {
            result = sampler.Sample(S(p[0], p[1], p[2]));
            if (result.IsValid)
                trigger = detector.Update(result, rect, p[2]);
        }

        return (result, trigger);
    }

    // ── Basic swipe trigger ──

    [TestMethod]
    public void Swipe_FastAndClose_Triggers()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            SpeedThreshold = 1200,
            EdgeDistanceThreshold = 80,
            CooldownMs = 3000,
        });

        // Start far away, then fast swipe that ends near the pet.
        // t=0: (100, 560) — far left
        // t=33: (490, 560) — fast segment: 390px/33ms ≈ 11818 px/s, ends 10px from left edge
        // t=66: (495, 560) — slow segment: 5px/33ms ≈ 152 px/s, still near edge
        // PeakSegmentEnd = (490, 560), 10px from edge < 80 → Triggered
        sampler.Sample(S(100, 560, 0));
        sampler.Sample(S(490, 560, 33));
        var result = sampler.Sample(S(495, 560, 66));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec > 1200,
            $"Speed should exceed 1200, got {result.EquivalentRecentSegmentSpeedPxPerSec}");

        var trigger = det.Update(result, _petRect, 66);
        Assert.AreEqual(SwipeTrigger.Triggered, trigger);
    }

    // ── Slow movement near pet does not trigger ──

    [TestMethod]
    public void Swipe_SlowNearPet_NoTrigger()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector();

        // Slow movement near the pet: 10px per 33ms ≈ 303 px/s, well below 1200.
        sampler.Sample(S(480, 560, 0));
        sampler.Sample(S(485, 560, 33));
        var result = sampler.Sample(S(490, 560, 66));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec < 1200);

        var trigger = det.Update(result, _petRect, 66);
        Assert.AreEqual(SwipeTrigger.None, trigger);
    }

    // ── Fast movement far from pet does not trigger ──

    [TestMethod]
    public void Swipe_FastFarFromPet_NoTrigger()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector();

        // Fast movement but far from pet (300px from edge).
        sampler.Sample(S(0, 560, 0));
        sampler.Sample(S(100, 560, 33));
        var result = sampler.Sample(S(200, 560, 66));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec > 1200);

        var trigger = det.Update(result, _petRect, 66);
        Assert.AreEqual(SwipeTrigger.None, trigger);
    }

    // ── Cooldown prevents re-trigger ──

    [TestMethod]
    public void Swipe_CooldownPreventsRetrigger()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            SpeedThreshold = 1200,
            EdgeDistanceThreshold = 80,
            CooldownMs = 3000,
        });

        // First swipe: fast segment ends near pet → trigger at t=66.
        sampler.Sample(S(100, 560, 0));
        sampler.Sample(S(490, 560, 33));
        var r1 = sampler.Sample(S(495, 560, 66));
        var t1 = det.Update(r1, _petRect, 66);
        Assert.AreEqual(SwipeTrigger.Triggered, t1);

        // Second fast swipe at t=100 (within 3000ms cooldown).
        sampler.Sample(S(100, 560, 70));
        sampler.Sample(S(490, 560, 85));
        var r2 = sampler.Sample(S(495, 560, 100));
        var t2 = det.Update(r2, _petRect, 100);
        Assert.AreEqual(SwipeTrigger.None, t2);

        // Third fast swipe at t=3200 (cooldown expired).
        sampler.Clear();
        sampler.Sample(S(100, 560, 3100));
        sampler.Sample(S(490, 560, 3133));
        var r3 = sampler.Sample(S(495, 560, 3166));
        var t3 = det.Update(r3, _petRect, 3166);
        Assert.AreEqual(SwipeTrigger.Triggered, t3);
    }

    // ── CRITICAL: Fast swipe from far, then slowly approach — no false trigger ──

    [TestMethod]
    public void Swipe_FastFromFarThenSlowApproach_NoFalseTrigger()
    {
        // This is the key test case from the user's requirement:
        // "远处快划后慢慢靠近不误触"
        //
        // Scenario: The mouse makes a fast swipe far from the pet, then
        // slowly approaches the pet. The old speed peak should NOT be
        // combined with the new close distance to produce a false trigger.
        //
        // The SwipeDetector evaluates speed and distance from the SAME
        // sample result in the same Update call. By the time the cursor
        // is close to the pet, the recent segment speed has dropped
        // below threshold (because the recent segments are slow).

        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            SpeedThreshold = 1200,
            EdgeDistanceThreshold = 80,
            CooldownMs = 3000,
        });

        // Phase 1: Fast swipe far from the pet (300px from left edge).
        // t=0: (0, 560) — far left
        // t=33: (100, 560) — fast segment: 100px/33ms ≈ 3030 px/s
        // t=66: (200, 560) — still fast, still far (300px from edge)
        sampler.Sample(S(0, 560, 0));
        sampler.Sample(S(100, 560, 33));
        var r1 = sampler.Sample(S(200, 560, 66));

        // At this point: speed is high (>1200) but distance is far (>80).
        // No trigger.
        Assert.IsTrue(r1.EquivalentRecentSegmentSpeedPxPerSec > 1200);
        var t1 = det.Update(r1, _petRect, 66);
        Assert.AreEqual(SwipeTrigger.None, t1);

        // Phase 2: Slowly approach the pet.
        // Each step: 20px per 33ms ≈ 606 px/s (below 1200 threshold).
        // After a few slow steps, the recent segment speed drops below 1200
        // because RecentSegmentCount=3 and the last 3 segments are all slow.
        //
        // t=99: (220, 560) — 20px in 33ms
        // t=132: (240, 560) — 20px in 33ms
        // t=165: (260, 560) — 20px in 33ms, now 240px from edge
        // t=198: (280, 560) — 20px in 33ms
        // t=231: (300, 560) — 20px in 33ms, 200px from edge
        // t=264: (320, 560) — 20px in 33ms
        // t=297: (340, 560) — 20px in 33ms, 160px from edge
        // t=330: (360, 560) — 20px in 33ms
        // t=363: (380, 560) — 20px in 33ms, 120px from edge
        // t=396: (400, 560) — 20px in 33ms
        // t=429: (420, 560) — 20px in 33ms, 80px from edge (at threshold)
        // t=462: (440, 560) — 20px in 33ms, 60px from edge (within threshold!)
        // t=495: (460, 560) — 20px in 33ms, 40px from edge
        // t=528: (480, 560) — 20px in 33ms, 20px from edge

        SwipeTrigger lastTrigger = SwipeTrigger.None;
        for (int t = 99; t <= 528; t += 33)
        {
            var x = 200 + ((t - 66) / 33) * 20;
            var r = sampler.Sample(S(x, 560, t));
            if (r.IsValid)
            {
                var tr = det.Update(r, _petRect, t);
                if (tr == SwipeTrigger.Triggered)
                    lastTrigger = tr;
            }
        }

        // By the time the cursor is close to the pet, the recent segment
        // speed should have dropped below 1200 (all recent segments are
        // 20px/33ms ≈ 606 px/s). No false trigger should occur.
        Assert.AreEqual(SwipeTrigger.None, lastTrigger,
            "Fast swipe from far followed by slow approach should NOT trigger");
    }

    // ── CRITICAL variant: Fast swipe from far, then approach with one fast segment near ──

    [TestMethod]
    public void Swipe_FastFromFarThenOneFastSegmentNear_Triggers()
    {
        // Contrast with the previous test: if the user actually makes a
        // fast movement near the pet (not just slowly approaching), it
        // SHOULD trigger. This verifies the detector isn't over-suppressing.

        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            SpeedThreshold = 1200,
            EdgeDistanceThreshold = 80,
            CooldownMs = 3000,
        });

        // Phase 1: Fast swipe far away.
        sampler.Sample(S(0, 560, 0));
        sampler.Sample(S(100, 560, 33));
        var r1 = sampler.Sample(S(200, 560, 66));
        Assert.AreEqual(SwipeTrigger.None, det.Update(r1, _petRect, 66));

        // Phase 2: Slowly approach.
        for (int t = 99; t <= 330; t += 33)
        {
            var x = 200 + ((t - 66) / 33) * 20;
            var r = sampler.Sample(S(x, 560, t));
            if (r.IsValid)
                det.Update(r, _petRect, t);
        }

        // Phase 3: Fast segment near the pet (within 80px of edge).
        // Current position at t=330 is (360, 560), 140px from edge.
        // Fast swipe: 360→460 in 33ms = 100px/33ms ≈ 3030 px/s.
        // 460 is 40px from left edge (500) — within 80px threshold.
        var r2 = sampler.Sample(S(460, 560, 363));
        Assert.IsTrue(r2.IsValid);
        Assert.IsTrue(r2.EquivalentRecentSegmentSpeedPxPerSec > 1200,
            $"Speed should exceed 1200, got {r2.EquivalentRecentSegmentSpeedPxPerSec}");
        var trigger = det.Update(r2, _petRect, 363);
        Assert.AreEqual(SwipeTrigger.Triggered, trigger,
            "Fast segment near the pet should trigger even after slow approach");
    }

    // ── Reset clears cooldown ──

    [TestMethod]
    public void Swipe_Reset_ClearsCooldown()
    {
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            SpeedThreshold = 1200,
            EdgeDistanceThreshold = 80,
            CooldownMs = 3000,
        });

        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });

        // Trigger a swipe: fast segment ends near pet.
        sampler.Sample(S(100, 560, 0));
        sampler.Sample(S(490, 560, 33));
        var r1 = sampler.Sample(S(495, 560, 66));
        Assert.AreEqual(SwipeTrigger.Triggered, det.Update(r1, _petRect, 66));

        // Reset (simulating drag/pause).
        det.Reset();
        Assert.IsFalse(det.IsInCooldown);

        // Immediately after reset, a new fast swipe should trigger.
        sampler.Clear();
        sampler.Sample(S(100, 560, 100));
        sampler.Sample(S(490, 560, 133));
        var r2 = sampler.Sample(S(495, 560, 166));
        Assert.AreEqual(SwipeTrigger.Triggered, det.Update(r2, _petRect, 166));
    }

    // ── Invalid sample does not trigger ──

    [TestMethod]
    public void Swipe_InvalidSample_NoTrigger()
    {
        var det = new SwipeDetector();
        var invalid = MouseSampleResult.Invalid;
        var trigger = det.Update(invalid, _petRect, 0);
        Assert.AreEqual(SwipeTrigger.None, trigger);
    }

    // ── DPI: same visual speed at 150% triggers ──

    [TestMethod]
    public void Swipe_At150PercentDPI_SameVisualSpeed_Triggers()
    {
        // At 150% DPI, physical pixels are 1.5x for the same visual distance.
        // The equivalent speed should be the same, so the same visual swipe
        // should trigger regardless of DPI.

        var sampler150 = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.5,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            DpiScale = 1.5,
        });
        var petRect150 = new PetRectPx(750, 750, 180, 180);

        // Fast swipe near the pet in physical pixels:
        // 150→640 = 490 physical px per 33ms ≈ 14848 physical px/s
        // Equivalent: 14848 / 1.5 = 9899 eq px/s > 1200 ✓
        // PeakSegmentEnd = (640, 840), 110 physical px from left edge (750)
        // Equivalent distance: 110 / 1.5 ≈ 73px < 80 ✓
        sampler150.Sample(S(150, 840, 0));
        sampler150.Sample(S(640, 840, 33));
        var result = sampler150.Sample(S(645, 840, 66));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec > 1200);

        var trigger = det.Update(result, petRect150, 66);
        Assert.AreEqual(SwipeTrigger.Triggered, trigger);
    }

    // ── F-03: 监理反例 — 快段远处结束 + 慢段近处不误触 ──

    [TestMethod]
    public void Swipe_FastSegmentFarEnd_SlowSegmentNear_NoFalseTrigger()
    {
        // Reproduces the监理 F-03 反例:
        // Pet rect=(500,500,120,120), mouse:
        //   (300,560,0ms) → (410,560,33ms) → (430,560,66ms)
        //
        // Segment 1: 300→410 = 110px/33ms ≈ 3333 px/s (fast, ends 90px from edge)
        // Segment 2: 410→430 = 20px/33ms ≈ 606 px/s (slow, ends 70px from edge)
        //
        // Old bug: EquivalentRecentSegmentSpeedPxPerSec = max(3333, 606) = 3333 > 1200,
        // and distance checked against sample.Latest (430,560) which is 70px < 80 → Triggered.
        // Expected: None, because the fast segment ended 90px from the pet (outside threshold).
        //
        // Fix: distance is checked against PeakSegmentEnd (410,560) — the endpoint
        // of the segment that produced the 3333 px/s peak. 410 is 90px from edge > 80 → None.

        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            SpeedThreshold = 1200,
            EdgeDistanceThreshold = 80,
            CooldownMs = 3000,
        });

        sampler.Sample(S(300, 560, 0));
        sampler.Sample(S(410, 560, 33));
        var result = sampler.Sample(S(430, 560, 66));

        Assert.IsTrue(result.IsValid);
        // The peak speed should be from segment 1 (3333 px/s).
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec > 1200,
            $"Peak speed should exceed 1200, got {result.EquivalentRecentSegmentSpeedPxPerSec}");
        // The peak segment endpoint should be (410, 560), not (430, 560).
        Assert.AreEqual(410, result.PeakSegmentEnd.X, 0.001,
            "PeakSegmentEnd should be the endpoint of the fastest segment");
        Assert.AreEqual(560, result.PeakSegmentEnd.Y, 0.001);

        var trigger = det.Update(result, _petRect, 66);
        Assert.AreEqual(SwipeTrigger.None, trigger,
            "Fast segment ending far + slow segment near should NOT trigger");
    }

    // ── F-03 正对照: 近处确实有快段时仍可触发 ──

    [TestMethod]
    public void Swipe_FastSegmentNearEnd_Triggers()
    {
        // Contrast with the F-03 反例: if the fast segment actually ends
        // near the pet, it SHOULD trigger. This verifies the fix doesn't
        // over-suppress legitimate swipes.
        //
        // Pet rect=(500,500,120,120), mouse:
        //   (300,560,0ms) → (490,560,33ms) → (500,560,66ms)
        //
        // Segment 1: 300→490 = 190px/33ms ≈ 5758 px/s (fast, ends 10px from edge)
        // Segment 2: 490→500 = 10px/33ms ≈ 303 px/s (slow, at edge)
        //
        // PeakSegmentEnd = (490, 560), 10px from edge < 80 → Triggered ✓

        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        var det = new SwipeDetector(new SwipeDetectorConfig
        {
            SpeedThreshold = 1200,
            EdgeDistanceThreshold = 80,
            CooldownMs = 3000,
        });

        sampler.Sample(S(300, 560, 0));
        sampler.Sample(S(490, 560, 33));
        var result = sampler.Sample(S(500, 560, 66));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec > 1200);
        // Peak segment endpoint should be (490, 560).
        Assert.AreEqual(490, result.PeakSegmentEnd.X, 0.001);

        var trigger = det.Update(result, _petRect, 66);
        Assert.AreEqual(SwipeTrigger.Triggered, trigger,
            "Fast segment ending near the pet should trigger");
    }
}
