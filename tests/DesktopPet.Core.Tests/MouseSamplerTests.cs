using DesktopPet.Core.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Tests for MouseSampler: speed calculation, sliding window,
/// sampling interruption filter, teleportation filter, and
/// cross-screen DPI equivalent speed.
/// 
/// Uses a controlled clock (explicit timestamps) and fixed input
/// sequences — no real mouse or timer dependencies.
/// </summary>
[TestClass]
public class MouseSamplerTests
{
    private static MouseSample S(double x, double y, double t) => new(x, y, t);

    // ── Basic speed calculation ──

    [TestMethod]
    public void FirstSample_ReturnsInvalid()
    {
        var sampler = new MouseSampler();
        var result = sampler.Sample(S(100, 200, 0));
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0, result.EquivalentSpeedPxPerSec, 0.001);
        Assert.AreEqual(0, result.EquivalentRecentSegmentSpeedPxPerSec, 0.001);
    }

    [TestMethod]
    public void TwoSamples_ComputesCorrectSpeed_100Percent()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.0 });
        sampler.Sample(S(0, 0, 0));
        // Move 100 px in 100 ms = 1000 px/s
        var result = sampler.Sample(S(100, 0, 100));
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(1000, result.EquivalentSpeedPxPerSec, 0.1);
        Assert.AreEqual(1000, result.SpeedPxPerSec, 0.1);
    }

    [TestMethod]
    public void TwoSamples_DiagonalMove_ComputesEuclideanSpeed()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.0 });
        sampler.Sample(S(0, 0, 0));
        // Move (60, 80) in 100 ms → distance 100 px → 1000 px/s
        var result = sampler.Sample(S(60, 80, 100));
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(1000, result.EquivalentSpeedPxPerSec, 0.1);
    }

    // ── Sliding window ──

    [TestMethod]
    public void SlidingWindow_OldSamplesEvicted_SpeedReflectsRecentOnly()
    {
        var config = new MouseSamplerConfig { WindowSize = 3, DpiScale = 1.0 };
        var sampler = new MouseSampler(config);

        // t=0:   (0, 0)
        // t=100: (100, 0)  → speed 1000 px/s over [0,100]
        // t=200: (200, 0)  → speed 1000 px/s over [0,200] (3 samples)
        // t=300: (210, 0)  → window evicts t=0, speed over [100,300] = 110/0.2 = 550 px/s
        sampler.Sample(S(0, 0, 0));
        sampler.Sample(S(100, 0, 100));
        sampler.Sample(S(200, 0, 200));
        var result = sampler.Sample(S(210, 0, 300));

        Assert.IsTrue(result.IsValid);
        // Distance from (100,0) to (210,0) = 110 px over 200 ms = 550 px/s
        Assert.AreEqual(550, result.EquivalentSpeedPxPerSec, 0.1);
        Assert.AreEqual(3, sampler.WindowCount);
    }

    [TestMethod]
    public void WindowSize1_CannotComputeSpeed_AlwaysInvalid()
    {
        var config = new MouseSamplerConfig { WindowSize = 1, DpiScale = 1.0 };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        var result = sampler.Sample(S(100, 0, 100));

        Assert.IsFalse(result.IsValid);
    }

    // ── Sampling interruption filter ──

    [TestMethod]
    public void SampleGap_ExceedingMaxGap_ClearsWindow()
    {
        var config = new MouseSamplerConfig
        {
            MaxSampleGapMs = 200,
            DpiScale = 1.0,
        };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        sampler.Sample(S(100, 0, 100));
        // Gap of 500 ms exceeds MaxSampleGapMs=200
        var result = sampler.Sample(S(200, 0, 600));

        Assert.IsFalse(result.IsValid);
        // Window should be cleared and restarted with the new sample
        Assert.AreEqual(1, sampler.WindowCount);
    }

    [TestMethod]
    public void SampleGap_ExactlyAtMaxGap_Kept()
    {
        var config = new MouseSamplerConfig
        {
            MaxSampleGapMs = 200,
            DpiScale = 1.0,
        };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        // Gap exactly 200 ms = MaxSampleGapMs, should be accepted (not >)
        // Distance 100 px over 200 ms = 500 px/s
        var result = sampler.Sample(S(100, 0, 200));

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(500, result.EquivalentSpeedPxPerSec, 0.1);
    }

    // ── Teleportation filter ──

    [TestMethod]
    public void Teleport_ExceedingMaxDisplacement_ClearsWindow()
    {
        var config = new MouseSamplerConfig
        {
            MaxDisplacementPx = 3000,
            DpiScale = 1.0,
        };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        // Displacement of 4000 px exceeds MaxDisplacementPx=3000
        var result = sampler.Sample(S(4000, 0, 100));

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(1, sampler.WindowCount);
    }

    [TestMethod]
    public void Teleport_AtMaxDisplacement_Kept()
    {
        var config = new MouseSamplerConfig
        {
            MaxDisplacementPx = 3000,
            DpiScale = 1.0,
        };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        // Displacement exactly 3000 = MaxDisplacementPx, should be accepted (not >)
        var result = sampler.Sample(S(3000, 0, 100));

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(30000, result.EquivalentSpeedPxPerSec, 0.1);
    }

    [TestMethod]
    public void Teleport_FilterUsesEquivalentPixels_At150Percent()
    {
        // At 150% DPI, physical 4500 px = 3000 equivalent px.
        // MaxDisplacementPx=3000 in equivalent pixels.
        // 4500 physical / 1.5 = 3000 equivalent → exactly at limit, accepted.
        var config = new MouseSamplerConfig
        {
            MaxDisplacementPx = 3000,
            DpiScale = 1.5,
        };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        var result = sampler.Sample(S(4500, 0, 100));

        Assert.IsTrue(result.IsValid);
    }

    // ── Cross-screen DPI equivalent speed ──

    [TestMethod]
    public void SameVisualDistance_DifferentDPI_SameEquivalentSpeed()
    {
        // At 100%: move 100 physical px in 100 ms
        var sampler100 = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.0 });
        sampler100.Sample(S(0, 0, 0));
        var result100 = sampler100.Sample(S(100, 0, 100));

        // At 150%: move 150 physical px in 100 ms (same visual distance)
        var sampler150 = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.5 });
        sampler150.Sample(S(0, 0, 0));
        var result150 = sampler150.Sample(S(150, 0, 100));

        Assert.IsTrue(result100.IsValid);
        Assert.IsTrue(result150.IsValid);
        // Both should have the same equivalent speed (1000 px/s)
        Assert.AreEqual(result100.EquivalentSpeedPxPerSec, result150.EquivalentSpeedPxPerSec, 0.1);
        // Physical speeds differ: 1000 vs 1500
        Assert.AreEqual(1000, result100.SpeedPxPerSec, 0.1);
        Assert.AreEqual(1500, result150.SpeedPxPerSec, 0.1);
    }

    // ── Clear / reset ──

    [TestMethod]
    public void Clear_ResetsWindowAndLastSample()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.0 });
        sampler.Sample(S(0, 0, 0));
        sampler.Sample(S(100, 0, 100));

        Assert.AreEqual(2, sampler.WindowCount);

        sampler.Clear();

        Assert.AreEqual(0, sampler.WindowCount);
        // After clear, next sample is treated as first (invalid)
        var result = sampler.Sample(S(200, 0, 200));
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(1, sampler.WindowCount);
    }

    // ── Configurable parameters ──

    [TestMethod]
    public void CustomWindowSize_Respected()
    {
        var config = new MouseSamplerConfig { WindowSize = 5, DpiScale = 1.0 };
        var sampler = new MouseSampler(config);

        for (int i = 0; i <= 10; i++)
            sampler.Sample(S(i * 10, 0, i * 33));

        Assert.AreEqual(5, sampler.WindowCount);
    }

    [TestMethod]
    public void DpiScaleChanged_AffectsSubsequentSamples()
    {
        var config = new MouseSamplerConfig { DpiScale = 1.0 };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        var result100 = sampler.Sample(S(100, 0, 100));
        Assert.AreEqual(1000, result100.EquivalentSpeedPxPerSec, 0.1);

        // Change DPI to 150% and clear for fresh measurement
        config.DpiScale = 1.5;
        sampler.Clear();
        sampler.Sample(S(0, 0, 0));
        var result150 = sampler.Sample(S(150, 0, 100));
        Assert.AreEqual(1000, result150.EquivalentSpeedPxPerSec, 0.1);
    }

    // ── Edge cases ──

    [TestMethod]
    public void ZeroTimeDelta_ReturnsInvalid()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.0 });
        sampler.Sample(S(0, 0, 100));
        // Same timestamp → dt=0, cannot compute speed
        var result = sampler.Sample(S(100, 0, 100));
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void StationaryMouse_SpeedIsZero()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.0 });
        sampler.Sample(S(500, 500, 0));
        var result = sampler.Sample(S(500, 500, 100));
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(0, result.EquivalentSpeedPxPerSec, 0.001);
    }

    [TestMethod]
    public void LatestSample_InResult_MatchesLastFed()
    {
        var sampler = new MouseSampler(new MouseSamplerConfig { DpiScale = 1.0 });
        sampler.Sample(S(0, 0, 0));
        var last = S(123, 456, 200);
        var result = sampler.Sample(last);
        Assert.AreEqual(last, result.Latest);
    }

    // ── F-01: Recent segment speed for flick/swipe detection ──

    [TestMethod]
    public void FastRoundTrip_NetSpeedCancels_RecentSegmentSpeedPreserved()
    {
        // F-01 core case: (0,0)→(200,0)→(0,0), each segment 2000 px/s.
        // Net displacement speed should be 0 (returns to origin).
        // Recent segment speed should capture the fast movement.
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });

        sampler.Sample(S(0, 0, 0));
        var mid = sampler.Sample(S(200, 0, 100));
        var end = sampler.Sample(S(0, 0, 200));

        // Mid-point: net speed = 2000, recent segment = 2000
        Assert.IsTrue(mid.IsValid);
        Assert.AreEqual(2000, mid.EquivalentSpeedPxPerSec, 0.1);
        Assert.AreEqual(2000, mid.EquivalentRecentSegmentSpeedPxPerSec, 0.1);

        // End-point: net speed = 0 (returned to origin), but recent segment
        // should still show the fast return segment.
        Assert.IsTrue(end.IsValid);
        Assert.AreEqual(0, end.EquivalentSpeedPxPerSec, 0.1);
        Assert.AreEqual(2000, end.EquivalentRecentSegmentSpeedPxPerSec, 0.1);
    }

    [TestMethod]
    public void StationaryThenFastFlick_RecentSegmentNotDiluted()
    {
        // F-01 second case: 9 stationary samples, then a fast flick.
        // The flick segment is ~3030 px/s but net window speed is ~336.
        // Recent segment speed should capture the fast flick.
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });

        // 9 stationary samples at 33ms intervals (t=0..264ms)
        for (int i = 0; i < 9; i++)
            sampler.Sample(S(500, 500, i * 33));

        // Fast flick: 100px in 33ms ≈ 3030 px/s
        var result = sampler.Sample(S(600, 500, 297));

        Assert.IsTrue(result.IsValid);
        // Net window speed is diluted by stationary samples
        Assert.IsTrue(result.EquivalentSpeedPxPerSec < 500,
            $"Net speed should be diluted, got {result.EquivalentSpeedPxPerSec}");
        // Recent segment speed should capture the fast flick
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec > 2500,
            $"Recent segment speed should capture flick, got {result.EquivalentRecentSegmentSpeedPxPerSec}");
    }

    [TestMethod]
    public void SlowJitter_RecentSegmentBelowThreshold()
    {
        // Slow jitter: small movements that should NOT trigger swipe.
        // Each segment: 5px in 33ms ≈ 151 px/s — well below 1200.
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });

        MouseSampleResult result = default;
        for (int i = 0; i < 10; i++)
        {
            // Alternate small jitter: ±5px
            var x = i % 2 == 0 ? 500 : 505;
            result = sampler.Sample(S(x, 500, i * 33));
        }

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec < 200,
            $"Slow jitter should be below threshold, got {result.EquivalentRecentSegmentSpeedPxPerSec}");
        Assert.IsTrue(result.EquivalentSpeedPxPerSec < 50,
            $"Net speed for jitter should be near zero, got {result.EquivalentSpeedPxPerSec}");
    }

    [TestMethod]
    public void SlowRoundTrip_RecentSegmentBelowThreshold()
    {
        // Slow round trip: 20px in 150ms each way = 133 px/s per segment.
        // Should NOT trigger swipe threshold (1200).
        var sampler = new MouseSampler(new MouseSamplerConfig
        {
            WindowSize = 10,
            MaxSampleGapMs = 200,
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });

        sampler.Sample(S(0, 0, 0));
        sampler.Sample(S(20, 0, 150));
        var result = sampler.Sample(S(0, 0, 300));

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.EquivalentRecentSegmentSpeedPxPerSec < 200,
            $"Slow round trip should be below threshold, got {result.EquivalentRecentSegmentSpeedPxPerSec}");
    }

    [TestMethod]
    public void RecentSegmentCount1_OnlyLastSegment()
    {
        // With RecentSegmentCount=1, only the very last segment matters.
        var config = new MouseSamplerConfig
        {
            WindowSize = 10,
            DpiScale = 1.0,
            RecentSegmentCount = 1,
        };
        var sampler = new MouseSampler(config);

        sampler.Sample(S(0, 0, 0));
        sampler.Sample(S(200, 0, 100));  // segment: 2000 px/s
        var result = sampler.Sample(S(0, 0, 200));   // segment: 2000 px/s

        Assert.IsTrue(result.IsValid);
        // Only last segment (return) is considered: 200px / 0.1s = 2000
        Assert.AreEqual(2000, result.EquivalentRecentSegmentSpeedPxPerSec, 0.1);
    }

    [TestMethod]
    public void RecentSegmentSpeed_DPI_EquivalentAt150Percent()
    {
        // Same visual flick at 150% should give same equivalent recent segment speed.
        var sampler100 = new MouseSampler(new MouseSamplerConfig
        {
            DpiScale = 1.0,
            RecentSegmentCount = 3,
        });
        sampler100.Sample(S(0, 0, 0));
        var r100 = sampler100.Sample(S(120, 0, 100));

        var sampler150 = new MouseSampler(new MouseSamplerConfig
        {
            DpiScale = 1.5,
            RecentSegmentCount = 3,
        });
        sampler150.Sample(S(0, 0, 0));
        var r150 = sampler150.Sample(S(180, 0, 100));  // 180 physical = 120 equivalent

        Assert.AreEqual(r100.EquivalentRecentSegmentSpeedPxPerSec,
                        r150.EquivalentRecentSegmentSpeedPxPerSec, 0.1);
    }
}
