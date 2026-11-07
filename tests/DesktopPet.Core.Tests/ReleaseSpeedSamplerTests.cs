using DesktopPet.Core.Physics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Tests for ReleaseSpeedSampler — release velocity estimation and
/// gentle-drop / throw classification (P2-001, R-04).
///
/// Coverage:
/// - Gentle release (slow / stationary) does not start physics.
/// - Fast throw produces direction-correct, clamped velocity.
/// - Stop-before-release does not carry old velocity.
/// - Direction reversal: release direction follows recent motion, not net displacement.
/// - Zero-time-delta and out-of-order samples are rejected without corrupting history.
/// - Sampling gap does not produce anomalous speed.
/// - DPI / coordinate-context change via ClearSampleContext: no throw from coordinate jump.
/// - Invalid coordinates and release time are safely rejected.
/// - Insufficient samples → gentle release.
/// </summary>
[TestClass]
public sealed class ReleaseSpeedSamplerTests
{
    private static ReleaseSpeedConfig TestConfig() => new()
    {
        SampleWindowMs = 100,
        ThrowThresholdPxPerMs = 0.15,
        MaxSpeedPxPerMs = 2.0,
        MinSamplesForThrow = 2,
        MinTimeSpanMs = 5,
        StopSpeedPxPerMs = 0.02,
    };

    // ── Gentle release ──

    [TestMethod]
    public void GentleRelease_Stationary_NoThrow()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(100, 200, 50);
        sampler.RecordSample(100, 200, 100);
        sampler.RecordSample(100, 200, 150);
        sampler.RecordSample(100, 200, 200);

        var result = sampler.EvaluateRelease(200);

        Assert.IsFalse(result.ShouldThrow);
        Assert.AreEqual(0, result.VelocityX, 1e-9);
        Assert.AreEqual(0, result.VelocityY, 1e-9);
    }

    [TestMethod]
    public void GentleRelease_SlowMove_NoThrow()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        // Move 5px over 100ms = 0.05 px/ms, below 0.15 threshold.
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(103, 200, 50);
        sampler.RecordSample(105, 200, 100);

        var result = sampler.EvaluateRelease(100);

        Assert.IsFalse(result.ShouldThrow);
        Assert.IsTrue(result.RawSpeed < 0.15);
    }

    // ── Throw: direction and clamping ──

    [TestMethod]
    public void FastThrow_Right_ProducesRightwardVelocity()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        // Move 200px right over 50ms = 4.0 px/ms, above max 2.0.
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 25);
        sampler.RecordSample(300, 200, 50);

        var result = sampler.EvaluateRelease(50);

        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityX > 0, "Velocity should be rightward (positive X).");
        Assert.AreEqual(0, result.VelocityY, 1e-9, "No vertical movement.");
        var speed = Math.Sqrt(result.VelocityX * result.VelocityX + result.VelocityY * result.VelocityY);
        Assert.IsTrue(speed <= 2.0 + 1e-9, $"Speed {speed} should be clamped to <= 2.0.");
        Assert.IsTrue(result.RawSpeed > 2.0, "Raw speed should exceed max before clamping.");
    }

    [TestMethod]
    public void FastThrow_Diagonal_PreservesDirection()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        // Move 300px right and 300px down over 50ms.
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(250, 350, 25);
        sampler.RecordSample(400, 500, 50);

        var result = sampler.EvaluateRelease(50);

        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityX > 0);
        Assert.IsTrue(result.VelocityY > 0);
        Assert.AreEqual(result.VelocityX, result.VelocityY, 1e-6,
            "Diagonal with equal displacement should have equal velocity components.");
        var speed = Math.Sqrt(result.VelocityX * result.VelocityX + result.VelocityY * result.VelocityY);
        Assert.IsTrue(speed <= 2.0 + 1e-9);
    }

    [TestMethod]
    public void FastThrow_Leftward_NegativeVelocity()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        // Move 200px left over 50ms.
        sampler.RecordSample(300, 200, 0);
        sampler.RecordSample(200, 200, 25);
        sampler.RecordSample(100, 200, 50);

        var result = sampler.EvaluateRelease(50);

        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityX < 0, "Velocity should be leftward (negative X).");
        Assert.AreEqual(0, result.VelocityY, 1e-9);
    }

    // ── F-01: Direction reversal — recent motion, not net displacement ──

    [TestMethod]
    public void DirectionReversal_FastRightThenFastLeft_ThrowsLeftward()
    {
        // F-01反例：(100,0)@0→(400,0)@50→(300,0)@100
        // 末段正在向左拉，应输出向左速度，不是净位移向右。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(400, 200, 50);
        sampler.RecordSample(300, 200, 100);

        var result = sampler.EvaluateRelease(100);

        Assert.IsTrue(result.ShouldThrow, "Recent motion is fast enough to throw.");
        Assert.IsTrue(result.VelocityX < 0,
            "Last motion segment is leftward; velocity must be negative X, not net-displacement rightward.");
    }

    [TestMethod]
    public void DirectionReversal_FastLeftThenFastRight_ThrowsRightward()
    {
        // 反向对照：先向左再向右，应向右。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(400, 200, 0);
        sampler.RecordSample(100, 200, 50);
        sampler.RecordSample(200, 200, 100);

        var result = sampler.EvaluateRelease(100);

        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityX > 0,
            "Last motion segment is rightward; velocity must be positive X.");
    }

    [TestMethod]
    public void DirectionReversal_DiagonalThenUp_ThrowsUpward()
    {
        // 对角对照：先右下再向上，末段向上。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 400, 50);
        sampler.RecordSample(300, 100, 100);

        var result = sampler.EvaluateRelease(100);

        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityY < 0,
            "Last motion segment is upward (negative Y); velocity must reflect recent direction.");
    }

    [TestMethod]
    public void RoundTripDrag_ReturnToStart_ThrowsInLastDirection()
    {
        // 往返拖动回到起点，末段向左运动 → 向左甩出。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 30);
        sampler.RecordSample(300, 200, 60);
        sampler.RecordSample(200, 200, 80);
        sampler.RecordSample(100, 200, 100);

        var result = sampler.EvaluateRelease(100);

        // 末段 (200→100)/20ms = 5px/ms 向左，应向左甩出。
        Assert.IsTrue(result.ShouldThrow, "Last segment is fast leftward motion.");
        Assert.IsTrue(result.VelocityX < 0, "Last segment direction is leftward.");
    }

    // ── F-01: Stop before release — continuous 33ms sampling ──

    [TestMethod]
    public void StopBeforeRelease_Continuous33ms_Then2StationaryFrames_Gentle()
    {
        // F-01反例：33ms连续采样移动后，静止2帧(66ms)松手。
        // (0,0)@0→(100,0)@33→(200,0)@66→同点@99→同点@132
        // 末段静止66ms，应轻放。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 33);
        sampler.RecordSample(300, 200, 66);
        sampler.RecordSample(300, 200, 99);
        sampler.RecordSample(300, 200, 132);

        var result = sampler.EvaluateRelease(132);

        Assert.IsFalse(result.ShouldThrow,
            "Pet stopped for 66ms (2 frames at 33ms) before release; must not carry old velocity.");
        Assert.AreEqual(0, result.VelocityX, 1e-9);
        Assert.AreEqual(0, result.VelocityY, 1e-9);
    }

    [TestMethod]
    public void StopBeforeRelease_LongStationary_Gentle()
    {
        // 快速移动后长时间静止。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 25);
        sampler.RecordSample(500, 200, 50);
        sampler.RecordSample(500, 200, 100);
        sampler.RecordSample(500, 200, 150);
        sampler.RecordSample(500, 200, 200);

        var result = sampler.EvaluateRelease(200);

        Assert.IsFalse(result.ShouldThrow,
            "Pet stopped 150ms before release; old velocity must not be carried forward.");
        Assert.AreEqual(0, result.VelocityX, 1e-9);
        Assert.AreEqual(0, result.VelocityY, 1e-9);
    }

    [TestMethod]
    public void StopBeforeRelease_ShortPause_StillGentle()
    {
        // 快速移动后短暂静止（仍在窗口内）。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(400, 200, 50);
        sampler.RecordSample(400, 200, 60);
        sampler.RecordSample(400, 200, 70);
        sampler.RecordSample(400, 200, 80);
        sampler.RecordSample(400, 200, 90);
        sampler.RecordSample(400, 200, 100);
        sampler.RecordSample(400, 200, 110);

        var result = sampler.EvaluateRelease(110);

        Assert.IsFalse(result.ShouldThrow,
            "Pet stopped at 50ms and held stationary; recent segment is not moving.");
    }

    [TestMethod]
    public void ContinuousFastDrag_StillThrows()
    {
        // 持续快拖不停止，应能甩出。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 25);
        sampler.RecordSample(300, 200, 50);
        sampler.RecordSample(400, 200, 75);
        sampler.RecordSample(500, 200, 100);

        var result = sampler.EvaluateRelease(100);

        Assert.IsTrue(result.ShouldThrow, "Continuous fast drag should produce a throw.");
        Assert.IsTrue(result.VelocityX > 0);
    }

    // ── F-03: Zero time delta and out-of-order samples ──

    [TestMethod]
    public void ZeroTimeDelta_SameTimestamp_SecondSkipped()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        // Same timestamp — should be skipped.
        sampler.RecordSample(500, 200, 0);

        Assert.AreEqual(1, sampler.SampleCount, "Same-timestamp sample should be rejected.");
    }

    [TestMethod]
    public void OutOfOrderTime_EarlierTimestamp_Rejected()
    {
        // F-03反例：0→50→25ms，25ms应被拒绝，不破坏FIFO。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 50);
        // Out-of-order — should be rejected.
        sampler.RecordSample(600, 200, 25);

        Assert.AreEqual(2, sampler.SampleCount, "Out-of-order sample should be rejected.");

        // Valid history is intact: (100,200)@0 and (200,200)@50.
        var result = sampler.EvaluateRelease(50);
        Assert.IsTrue(result.ShouldThrow, "Valid samples should still produce correct result.");
        Assert.IsTrue(result.VelocityX > 0);
    }

    [TestMethod]
    public void OutOfOrderTime_NegativeTimestamp_Rejected()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        // Negative / earlier — should be rejected.
        sampler.RecordSample(600, 200, -1);

        Assert.AreEqual(1, sampler.SampleCount);
    }

    [TestMethod]
    public void AllSameTimestamp_OnlyFirstKept()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 50);
        sampler.RecordSample(500, 200, 50);
        sampler.RecordSample(900, 200, 50);

        var result = sampler.EvaluateRelease(50);

        Assert.IsFalse(result.ShouldThrow);
        Assert.AreEqual(1, sampler.SampleCount);
    }

    // ── F-03: Invalid coordinates ──

    [TestMethod]
    public void NaNCoordinates_Rejected()
    {
        // F-03反例：NaN坐标不应输出NaN速度。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(double.NaN, 200, 50);

        Assert.AreEqual(1, sampler.SampleCount, "NaN X coordinate should be rejected.");

        // Valid sample remains; with only 1 sample → gentle.
        var result = sampler.EvaluateRelease(50);
        Assert.IsFalse(result.ShouldThrow);
        Assert.IsFalse(double.IsNaN(result.VelocityX));
        Assert.IsFalse(double.IsNaN(result.VelocityY));
    }

    [TestMethod]
    public void NaN_Y_Coordinate_Rejected()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, double.NaN, 50);

        Assert.AreEqual(1, sampler.SampleCount, "NaN Y coordinate should be rejected.");
    }

    [TestMethod]
    public void InfinityCoordinates_Rejected()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(double.PositiveInfinity, 200, 50);

        Assert.AreEqual(1, sampler.SampleCount, "Infinity X coordinate should be rejected.");
    }

    [TestMethod]
    public void NaNCoordinates_ThenValidSample_StillWorks()
    {
        // 非法坐标不污染历史，后续有效调用仍正常。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(double.NaN, 200, 25);  // rejected
        sampler.RecordSample(300, 200, 50);          // valid

        Assert.AreEqual(2, sampler.SampleCount);
        var result = sampler.EvaluateRelease(50);
        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityX > 0);
    }

    // ── F-03: Invalid release time ──

    [TestMethod]
    public void NaNReleaseTime_GentleRelease()
    {
        // F-03反例：NaN释放时间不应返回有效抛掷。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 50);

        var result = sampler.EvaluateRelease(double.NaN);

        Assert.IsFalse(result.ShouldThrow, "NaN release time must not produce a throw.");
        Assert.AreEqual(0, result.VelocityX, "Velocity must be zero, not NaN.");
        Assert.AreEqual(0, result.VelocityY);
        Assert.IsFalse(double.IsNaN(result.RawSpeed));
    }

    [TestMethod]
    public void InfinityReleaseTime_GentleRelease()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 50);

        var result = sampler.EvaluateRelease(double.PositiveInfinity);

        Assert.IsFalse(result.ShouldThrow, "Infinity release time must not produce a throw.");
    }

    [TestMethod]
    public void ReleaseTimeBeforeNewestSample_GentleRelease()
    {
        // F-03：释放时间早于最新样本，不能用未来样本算速度。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 50);
        sampler.RecordSample(500, 200, 100);

        var result = sampler.EvaluateRelease(60);

        // Release at 60ms is before newest sample at 100ms.
        // Should not use the 100ms sample to compute velocity.
        Assert.IsFalse(result.ShouldThrow,
            "Release time before newest sample should not use future samples.");
    }

    [TestMethod]
    public void AllReturnedVelocities_AreFinite()
    {
        // 所有返回速度必须有限。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(double.NaN, 200, 25);       // rejected
        sampler.RecordSample(300, double.PositiveInfinity, 50); // rejected

        var result = sampler.EvaluateRelease(50);

        Assert.IsFalse(double.IsNaN(result.VelocityX));
        Assert.IsFalse(double.IsNaN(result.VelocityY));
        Assert.IsFalse(double.IsInfinity(result.VelocityX));
        Assert.IsFalse(double.IsInfinity(result.VelocityY));
    }

    // ── F-02: DPI / coordinate context change ──

    [TestMethod]
    public void ClearSampleContext_ThenImmediateRelease_Gentle()
    {
        // F-02反例：仅上下文变化立即松手不抛。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 50);

        // DPI change — caller signals context switch.
        sampler.ClearSampleContext();

        // Immediate release after context change — no samples → gentle.
        var result = sampler.EvaluateRelease(50);

        Assert.IsFalse(result.ShouldThrow,
            "After ClearSampleContext with no new samples, release must be gentle.");
    }

    [TestMethod]
    public void ClearSampleContext_CoordinateJumpNotTreatedAsThrow()
    {
        // F-02反例：DPI跳变 (100,200)@0→(150,300)@50 不应抛掷。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);

        // DPI change — caller clears context before recording in new coordinates.
        sampler.ClearSampleContext();
        sampler.RecordSample(150, 300, 50);

        // Only 1 sample after context clear → gentle.
        var result = sampler.EvaluateRelease(50);

        Assert.IsFalse(result.ShouldThrow,
            "Coordinate jump after ClearSampleContext must not produce a throw.");
    }

    [TestMethod]
    public void ClearSampleContext_ThenNewTrajectory_ThrowsCorrectly()
    {
        // 上下文变化后产生足够真实轨迹，正常估计。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 50);

        sampler.ClearSampleContext();

        // New trajectory in new coordinate system.
        sampler.RecordSample(500, 500, 60);
        sampler.RecordSample(700, 500, 85);
        sampler.RecordSample(900, 500, 110);

        var result = sampler.EvaluateRelease(110);

        // 3 samples, fast rightward motion → throw rightward.
        Assert.IsTrue(result.ShouldThrow, "New trajectory after context clear should throw.");
        Assert.IsTrue(result.VelocityX > 0, "Motion is rightward in new coordinates.");
    }

    [TestMethod]
    public void ClearSampleContext_ThenStationary_Gentle()
    {
        // 上下文变化后静止 → 轻放。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.ClearSampleContext();
        sampler.RecordSample(150, 300, 20);
        sampler.RecordSample(150, 300, 60);
        sampler.RecordSample(150, 300, 100);
        sampler.RecordSample(150, 300, 150);

        var result = sampler.EvaluateRelease(150);

        Assert.IsFalse(result.ShouldThrow,
            "After context clear and stationary hold, release should be gentle.");
    }

    // ── Sampling gap ──

    [TestMethod]
    public void SamplingGap_OldSamplesPruned_GentleIfWindowEmpty()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 25);
        sampler.RecordSample(500, 200, 50);

        var result = sampler.EvaluateRelease(550);

        Assert.IsFalse(result.ShouldThrow,
            "After a long gap with no samples in the window, release should be gentle.");
    }

    [TestMethod]
    public void SamplingGap_RecentSamplesStillUsed()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(500, 200, 50);
        // Gap, then new slow movement.
        sampler.RecordSample(500, 200, 250);
        sampler.RecordSample(510, 200, 300);

        var result = sampler.EvaluateRelease(300);

        // 10/50 = 0.2 px/ms → above threshold → throw.
        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityX > 0);
    }

    // ── Edge cases ──

    [TestMethod]
    public void NoSamples_GentleRelease()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());

        var result = sampler.EvaluateRelease(100);

        Assert.IsFalse(result.ShouldThrow);
        Assert.AreEqual(0, result.VelocityX);
        Assert.AreEqual(0, result.VelocityY);
    }

    [TestMethod]
    public void SingleSample_GentleRelease()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);

        var result = sampler.EvaluateRelease(50);

        Assert.IsFalse(result.ShouldThrow);
    }

    [TestMethod]
    public void Reset_ClearsSamples()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 50);

        sampler.Reset();

        Assert.IsFalse(sampler.HasSamples);
        Assert.AreEqual(0, sampler.SampleCount);
    }

    [TestMethod]
    public void Reset_AllowsReuseForNewDrag()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(500, 200, 50);
        var first = sampler.EvaluateRelease(50);
        Assert.IsTrue(first.ShouldThrow);

        sampler.Reset();

        sampler.RecordSample(200, 200, 0);
        sampler.RecordSample(203, 200, 50);
        var second = sampler.EvaluateRelease(50);
        Assert.IsFalse(second.ShouldThrow);
    }

    [TestMethod]
    public void JustAboveThreshold_Throws()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        // 8px over 50ms = 0.16 px/ms, just above 0.15 threshold.
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(108, 200, 50);

        var result = sampler.EvaluateRelease(50);

        Assert.IsTrue(result.ShouldThrow, "Speed just above threshold should throw.");
        Assert.IsTrue(result.RawSpeed > 0.15);
        Assert.IsTrue(result.RawSpeed <= 2.0);
    }

    [TestMethod]
    public void JustBelowThreshold_Gentle()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        // 7px over 50ms = 0.14 px/ms, just below 0.15 threshold.
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(107, 200, 50);

        var result = sampler.EvaluateRelease(50);

        Assert.IsFalse(result.ShouldThrow, "Speed just below threshold should be gentle.");
    }

    [TestMethod]
    public void NaNTimestamp_Rejected()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, double.NaN);

        Assert.AreEqual(1, sampler.SampleCount, "NaN timestamp sample should be rejected.");
    }

    [TestMethod]
    public void InfinityTimestamp_Rejected()
    {
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, double.PositiveInfinity);

        Assert.AreEqual(1, sampler.SampleCount, "Infinity timestamp sample should be rejected.");
    }

    // ── F-01: Continuous sequence tests (33ms sampling) ──

    [TestMethod]
    public void ContinuousSequence_FastRightThenStop_2Frames_Gentle()
    {
        // 33ms连续快拖后静止2帧。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 33);
        sampler.RecordSample(300, 200, 66);
        // Stop: 2 stationary frames.
        sampler.RecordSample(300, 200, 99);
        sampler.RecordSample(300, 200, 132);

        var result = sampler.EvaluateRelease(132);

        Assert.IsFalse(result.ShouldThrow,
            "2 stationary frames after continuous motion → gentle.");
    }

    [TestMethod]
    public void ContinuousSequence_FastRightThenStop_1Frame_StillGentle()
    {
        // 33ms连续快拖后仅1帧静止。
        // 末段 (300→300)/33ms = 0 → 低于停止速度 → 轻放。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 33);
        sampler.RecordSample(300, 200, 66);
        sampler.RecordSample(300, 200, 99);

        var result = sampler.EvaluateRelease(99);

        // Last pair (66→99) is stationary → no motion segment → gentle.
        Assert.IsFalse(result.ShouldThrow,
            "1 stationary frame after motion: last pair is stationary → gentle.");
    }

    [TestMethod]
    public void ContinuousSequence_FastRight_NoStop_Throws()
    {
        // 33ms连续快拖不停止 → 甩出。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 33);
        sampler.RecordSample(300, 200, 66);
        sampler.RecordSample(400, 200, 99);

        var result = sampler.EvaluateRelease(99);

        Assert.IsTrue(result.ShouldThrow, "Continuous fast motion with no stop → throw.");
        Assert.IsTrue(result.VelocityX > 0);
    }

    [TestMethod]
    public void ContinuousSequence_RightThenLeft_ThrowsLeftward()
    {
        // 连续33ms采样：先向右3帧再向左2帧。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(200, 200, 33);
        sampler.RecordSample(300, 200, 66);
        sampler.RecordSample(200, 200, 99);
        sampler.RecordSample(100, 200, 132);

        var result = sampler.EvaluateRelease(132);

        // 末段向左运动 → 向左甩出。
        Assert.IsTrue(result.ShouldThrow);
        Assert.IsTrue(result.VelocityX < 0, "Last motion segment is leftward.");
    }

    [TestMethod]
    public void ContinuousSequence_RightThenLeftThenStop_Gentle()
    {
        // 先向右再向左然后停住。
        var sampler = new ReleaseSpeedSampler(TestConfig());
        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(250, 200, 33);
        sampler.RecordSample(400, 200, 66);
        sampler.RecordSample(250, 200, 99);
        sampler.RecordSample(100, 200, 132);
        // Stop.
        sampler.RecordSample(100, 200, 165);
        sampler.RecordSample(100, 200, 198);

        var result = sampler.EvaluateRelease(198);

        Assert.IsFalse(result.ShouldThrow,
            "Pet stopped after direction reversal → gentle.");
    }
}
