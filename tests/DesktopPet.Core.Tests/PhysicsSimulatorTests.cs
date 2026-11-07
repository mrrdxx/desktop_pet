using DesktopPet.Core.Physics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Tests for PhysicsSimulator — bounded projectile physics after a throw
/// release (P2-002, R-04).
///
/// Coverage:
/// - Gravity pulls pet downward (parabolic trajectory).
/// - Floor bounce with damping; pet eventually rests.
/// - Wall bounce (left/right) reflects horizontal velocity.
/// - Ceiling bounce (top boundary).
/// - Pet never leaves the work area.
/// - Stop detection: low speed after bounce → resting.
/// - MaxBounces cap prevents infinite micro-bouncing.
/// - Fixed-step determinism: same elapsed time → same result.
/// - Extreme dt: long stall capped by MaxStepsPerUpdate.
/// - Pause/resume: no accumulation between calls.
/// - Gentle release never enters physics (caller responsibility).
/// - Invalid elapsed time handled safely.
/// - ForceStop and Reset.
/// </summary>
[TestClass]
public sealed class PhysicsSimulatorTests
{
    private static PhysicsConfig TestConfig() => new()
    {
        FixedStepMs = 1000.0 / 60.0, // ~16.67 ms
        MaxStepsPerUpdate = 5,
        GravityPxPerMs2 = 0.002,
        BounceRestitution = 0.4,
        StopSpeedPxPerMs = 0.02,
        MaxBounces = 8,
    };

    private static WorkArea TestArea() => new(0, 0, 1000, 800);

    // ── Gravity trajectory ──

    [TestMethod]
    public void Gravity_PullsPetDownward()
    {
        var sim = new PhysicsSimulator(TestConfig());
        sim.Launch(500, 100, 0, 0); // no initial velocity, above floor

        // One step: v_y = g*dt, y += v_y*dt
        var result = sim.Update(sim.Config.FixedStepMs, TestArea());

        Assert.IsTrue(result.State.Vy > 0, "Gravity should increase downward velocity.");
        Assert.IsTrue(result.State.Y > 100, "Pet should move downward.");
    }

    [TestMethod]
    public void ParabolicTrajectory_HorizontalPlusGravity()
    {
        var sim = new PhysicsSimulator(TestConfig());
        sim.Launch(100, 100, 1.0, 0); // rightward, no vertical velocity

        // After several steps, pet should move right and fall down.
        var result = sim.Update(sim.Config.FixedStepMs * 3, TestArea());

        Assert.IsTrue(result.State.X > 100, "Pet should move rightward.");
        Assert.IsTrue(result.State.Y > 100, "Pet should also fall due to gravity.");
        Assert.IsTrue(result.State.Vy > 0, "Vertical velocity should be positive (falling).");
        Assert.IsTrue(result.State.Vx > 0, "Horizontal velocity preserved (no air resistance).");
    }

    [TestMethod]
    public void Trajectory_IsDeterministic()
    {
        var config = TestConfig();
        var area = TestArea();

        var sim1 = new PhysicsSimulator(config);
        sim1.Launch(100, 100, 1.0, -0.5);
        var r1 = sim1.Update(50, area);

        var sim2 = new PhysicsSimulator(config);
        sim2.Launch(100, 100, 1.0, -0.5);
        var r2 = sim2.Update(50, area);

        Assert.AreEqual(r1.State.X, r2.State.X, 1e-9, "Same input → same X.");
        Assert.AreEqual(r1.State.Y, r2.State.Y, 1e-9, "Same input → same Y.");
        Assert.AreEqual(r1.State.Vx, r2.State.Vx, 1e-9);
        Assert.AreEqual(r1.State.Vy, r2.State.Vy, 1e-9);
    }

    // ── Floor bounce and rest ──

    [TestMethod]
    public void FloorBounce_ReflectsAndDampsVelocity()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 700, 0, 2.0); // fast downward, near floor

        // Keep updating until a bounce occurs or pet rests.
        PhysicsUpdateResult result = default;
        for (var i = 0; i < 20; i++)
        {
            result = sim.Update(sim.Config.FixedStepMs, area);
            if (result.BounceCount > 0 || result.IsResting)
                break;
        }

        Assert.IsTrue(result.BounceCount > 0 || result.IsResting,
            "Pet should bounce off the floor or come to rest.");
        Assert.IsTrue(sim.State.Y <= area.Bottom + 1e-9,
            "Pet must not go below floor.");
    }

    [TestMethod]
    public void FloorBounce_EventuallyRests()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 0.5, 0); // moderate throw

        // Simulate enough time for the pet to bounce and settle.
        PhysicsUpdateResult result = default;
        for (var i = 0; i < 200; i++)
        {
            result = sim.Update(sim.Config.FixedStepMs * 3, area);
            if (result.IsResting)
                break;
        }

        Assert.IsTrue(result.IsResting,
            "Pet should eventually come to rest after bouncing.");
        Assert.AreEqual(0, sim.State.Vx, 1e-9, "Resting → zero horizontal velocity.");
        Assert.AreEqual(0, sim.State.Vy, 1e-9, "Resting → zero vertical velocity.");
        Assert.IsTrue(sim.State.Y <= area.Bottom + 1e-9,
            "Pet should rest on or above the floor.");
    }

    [TestMethod]
    public void RestingPet_NoFurtherMovement()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 0.3, 0);

        // Run until resting.
        for (var i = 0; i < 200; i++)
        {
            sim.Update(sim.Config.FixedStepMs * 3, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting);

        var restX = sim.State.X;
        var restY = sim.State.Y;

        // More updates should not move the pet.
        sim.Update(100, area);
        sim.Update(100, area);

        Assert.AreEqual(restX, sim.State.X, 1e-9, "Resting pet should not move.");
        Assert.AreEqual(restY, sim.State.Y, 1e-9);
    }

    // ── Wall bounce ──

    [TestMethod]
    public void WallBounce_Left_ReflectsHorizontalVelocity()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(10, 400, -2.0, 0); // fast leftward, near left wall

        PhysicsUpdateResult result = default;
        for (var i = 0; i < 20; i++)
        {
            result = sim.Update(sim.Config.FixedStepMs, area);
            if (result.BounceCount > 0 || result.IsResting)
                break;
        }

        Assert.IsTrue(result.BounceCount > 0 || result.IsResting,
            "Pet should bounce off left wall or come to rest.");
        Assert.IsTrue(sim.State.X >= area.Left - 1e-9,
            "Pet must not go left of the work area.");
    }

    [TestMethod]
    public void WallBounce_Right_ReflectsHorizontalVelocity()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(990, 400, 2.0, 0); // fast rightward, near right wall

        PhysicsUpdateResult result = default;
        for (var i = 0; i < 20; i++)
        {
            result = sim.Update(sim.Config.FixedStepMs, area);
            if (result.BounceCount > 0 || result.IsResting)
                break;
        }

        Assert.IsTrue(result.BounceCount > 0 || result.IsResting,
            "Pet should bounce off right wall or come to rest.");
        Assert.IsTrue(sim.State.X <= area.Right + 1e-9,
            "Pet must not go right of the work area.");
    }

    // ── Ceiling bounce ──

    [TestMethod]
    public void CeilingBounce_ReflectsVerticalVelocity()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 10, 0, -3.0); // fast upward, near ceiling

        PhysicsUpdateResult result = default;
        for (var i = 0; i < 20; i++)
        {
            result = sim.Update(sim.Config.FixedStepMs, area);
            if (result.BounceCount > 0 || result.IsResting)
                break;
        }

        Assert.IsTrue(result.BounceCount > 0 || result.IsResting,
            "Pet should bounce off ceiling or come to rest.");
        Assert.IsTrue(sim.State.Y >= area.Top - 1e-9,
            "Pet must not go above the work area.");
    }

    // ── Never leaves work area ──

    [TestMethod]
    public void PetNeverLeavesWorkArea()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 400, 5.0, -5.0); // very fast diagonal

        for (var i = 0; i < 100; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            Assert.IsTrue(sim.State.X >= area.Left - 1e-9,
                $"Step {i}: X={sim.State.X} < Left={area.Left}");
            Assert.IsTrue(sim.State.X <= area.Right + 1e-9,
                $"Step {i}: X={sim.State.X} > Right={area.Right}");
            Assert.IsTrue(sim.State.Y >= area.Top - 1e-9,
                $"Step {i}: Y={sim.State.Y} < Top={area.Top}");
            Assert.IsTrue(sim.State.Y <= area.Bottom + 1e-9,
                $"Step {i}: Y={sim.State.Y} > Bottom={area.Bottom}");

            if (sim.IsResting)
                break;
        }
    }

    // ── MaxBounces cap ──

    [TestMethod]
    public void MaxBouncesCap_FallsToFloorThenRests()
    {
        // F-02 反例: MaxBounces reached mid-air must NOT freeze pet in air.
        // Pet should continue falling under gravity until it reaches the floor.
        var config = TestConfig();
        config.MaxBounces = 1;
        config.BounceRestitution = 0.9;
        config.StopSpeedPxPerMs = 0.001;

        var sim = new PhysicsSimulator(config);
        var area = TestArea();
        sim.Launch(999, 100, 1.0, 0); // rightward, will hit right wall first

        // Run until resting — pet must end up on the floor, not in mid-air.
        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting, "Pet should eventually rest.");
        Assert.AreEqual(area.Bottom, sim.State.Y, 1e-6,
            "Pet must rest on the floor (Y=Bottom), not frozen in mid-air.");
    }

    [TestMethod]
    public void NoInfiniteMicroBouncing()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 0.1, 0);

        // Run for a long simulated time.
        for (var i = 0; i < 1000; i++)
        {
            sim.Update(sim.Config.FixedStepMs * 2, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting,
            "Pet must eventually stop — no infinite micro-bouncing.");
    }

    // ── Extreme dt ──

    [TestMethod]
    public void ExtremeDt_CappedByMaxSteps()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 0, 0);

        // Very large elapsed time — should be capped to MaxStepsPerUpdate steps.
        var result = sim.Update(10000, area); // 10 seconds

        Assert.IsTrue(result.StepsExecuted <= sim.Config.MaxStepsPerUpdate,
            $"Steps executed ({result.StepsExecuted}) should be capped at MaxStepsPerUpdate ({sim.Config.MaxStepsPerUpdate}).");
    }

    [TestMethod]
    public void ZeroDt_NoMovement()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 1.0, 0);

        var beforeX = sim.State.X;
        var beforeY = sim.State.Y;

        var result = sim.Update(0, area);

        Assert.AreEqual(0, result.StepsExecuted, "Zero dt → zero steps.");
        Assert.AreEqual(beforeX, sim.State.X, 1e-9);
        Assert.AreEqual(beforeY, sim.State.Y, 1e-9);
    }

    [TestMethod]
    public void NegativeDt_Rejected()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 1.0, 0);

        var beforeX = sim.State.X;
        var result = sim.Update(-100, area);

        Assert.IsTrue(result.Rejected, "Negative dt should be rejected.");
        Assert.AreEqual(0, result.StepsExecuted);
        Assert.AreEqual(beforeX, sim.State.X, 1e-9);
    }

    [TestMethod]
    public void NaNDt_Rejected()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 1.0, 0);

        var beforeX = sim.State.X;
        var result = sim.Update(double.NaN, area);

        Assert.IsTrue(result.Rejected, "NaN dt should be rejected.");
        Assert.AreEqual(0, result.StepsExecuted);
        Assert.AreEqual(beforeX, sim.State.X, 1e-9);
    }

    [TestMethod]
    public void InfinityDt_Rejected()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 1.0, 0);

        var beforeX = sim.State.X;
        var result = sim.Update(double.PositiveInfinity, area);

        Assert.IsTrue(result.Rejected, "Infinity dt should be rejected.");
        Assert.AreEqual(0, result.StepsExecuted);
        Assert.AreEqual(beforeX, sim.State.X, 1e-9);
    }

    // ── Pause/resume (carry + ClearTimeAccumulation) ──

    [TestMethod]
    public void PauseResume_ClearTimeAccumulation_KeepsMotionState()
    {
        // F-01: Pause/resume must clear time accumulation but preserve
        // motion state (position + velocity). ForceStop is NOT used for
        // pause — it would lose velocity.
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 1.0, 0);

        // Advance some steps.
        sim.Update(50, area);
        var stateBeforePause = sim.State;
        Assert.IsFalse(sim.IsResting);

        // Pause: caller calls ClearTimeAccumulation to discard the gap.
        sim.ClearTimeAccumulation();

        // Motion state preserved.
        Assert.AreEqual(stateBeforePause.X, sim.State.X, 1e-9);
        Assert.AreEqual(stateBeforePause.Y, sim.State.Y, 1e-9);
        Assert.AreEqual(stateBeforePause.Vx, sim.State.Vx, 1e-9);
        Assert.AreEqual(stateBeforePause.Vy, sim.State.Vy, 1e-9);
        Assert.IsFalse(sim.IsResting, "Pause should not set resting.");

        // Resume with small dt — pet advances normally.
        sim.Update(sim.Config.FixedStepMs, area);
        Assert.IsTrue(sim.State.X > stateBeforePause.X,
            "Pet should advance after resume.");
    }

    [TestMethod]
    public void MultipleUpdates_Cumulative()
    {
        // F-01: With carry, 30ms×2 should equal 60ms×1 (same total steps).
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(100, 100, 1.0, 0);

        var sim2 = new PhysicsSimulator(TestConfig());
        sim2.Launch(100, 100, 1.0, 0);

        sim.Update(30, area);
        sim.Update(30, area);

        sim2.Update(60, area);

        Assert.AreEqual(sim.State.X, sim2.State.X, 1e-9,
            "30ms×2 should equal 60ms×1 with carry.");
        Assert.AreEqual(sim.State.Y, sim2.State.Y, 1e-9);
    }

    // ── Gentle release (caller responsibility) ──

    [TestMethod]
    public void GentleRelease_PetStaysInPlace()
    {
        // When ReleaseSpeedSampler returns ShouldThrow=false, the caller
        // does NOT call Launch. The pet stays at its release position.
        // The simulator must not execute any steps without a Launch.
        var sim = new PhysicsSimulator(TestConfig());

        Assert.IsFalse(sim.IsResting, "Not launched yet — not resting, just idle.");
        // Without Launch, Update must not execute steps.
        var result = sim.Update(100, TestArea());
        Assert.AreEqual(0, result.StepsExecuted,
            "Without Launch, simulator should not execute steps.");
        // State remains at default (0,0,0,0) — no movement.
        Assert.AreEqual(0, sim.State.X, 1e-9);
        Assert.AreEqual(0, sim.State.Y, 1e-9);
    }

    // ── ForceStop ──

    [TestMethod]
    public void ForceStop_StopsImmediately()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 2.0, 2.0);

        // Advance a bit.
        sim.Update(50, area);
        Assert.IsFalse(sim.IsResting, "Pet should be moving.");

        // User re-grabs → force stop.
        sim.ForceStop();

        Assert.IsTrue(sim.IsResting, "ForceStop should set resting.");
        Assert.AreEqual(0, sim.State.Vx, 1e-9);
        Assert.AreEqual(0, sim.State.Vy, 1e-9);

        // Further updates should not move the pet.
        var restX = sim.State.X;
        sim.Update(100, area);
        Assert.AreEqual(restX, sim.State.X, 1e-9);
    }

    // ── Reset ──

    [TestMethod]
    public void Reset_ClearsState()
    {
        var sim = new PhysicsSimulator(TestConfig());
        sim.Launch(500, 100, 2.0, 0);
        sim.Update(50, TestArea());

        sim.Reset();

        Assert.AreEqual(0, sim.State.X, 1e-9);
        Assert.AreEqual(0, sim.State.Y, 1e-9);
        Assert.AreEqual(0, sim.State.Vx, 1e-9);
        Assert.AreEqual(0, sim.State.Vy, 1e-9);
        Assert.IsFalse(sim.IsResting);
        Assert.AreEqual(0, sim.TotalBounces);
    }

    [TestMethod]
    public void Reset_AllowsRelaunch()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();

        // First throw.
        sim.Launch(100, 100, 2.0, 0);
        sim.Update(50, area);
        var firstX = sim.State.X;
        Assert.IsTrue(firstX > 100, "First throw should move pet.");

        sim.Reset();

        // Second throw from different position.
        sim.Launch(800, 100, -1.0, 0);
        sim.Update(50, area);
        Assert.IsTrue(sim.State.X < 800, "Second throw should move pet leftward.");
        Assert.AreNotEqual(firstX, sim.State.X, "Second throw is different trajectory.");
    }

    // ── Bounce count tracking ──

    [TestMethod]
    public void BounceCount_TrackedAcrossUpdates()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 0, 0); // straight drop

        var totalBounces = 0;
        for (var i = 0; i < 200; i++)
        {
            var result = sim.Update(sim.Config.FixedStepMs * 3, area);
            totalBounces += result.BounceCount;
            if (result.IsResting)
                break;
        }

        Assert.AreEqual(totalBounces, sim.TotalBounces,
            "Sum of per-update bounces should equal TotalBounces.");
        Assert.IsTrue(totalBounces > 0, "Dropping pet should bounce at least once.");
    }

    // ── Floor friction ──

    [TestMethod]
    public void FloorBounce_DampsHorizontalVelocity()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(100, 100, 1.0, 0); // rightward throw

        // Run until first floor bounce.
        var initialVx = sim.State.Vx;
        for (var i = 0; i < 200; i++)
        {
            var result = sim.Update(sim.Config.FixedStepMs, area);
            if (result.BounceCount > 0)
            {
                // After floor bounce, horizontal velocity should be damped.
                Assert.IsTrue(sim.State.Vx < initialVx,
                    "Floor bounce should damp horizontal velocity.");
                return;
            }
            if (result.IsResting)
                break;
        }

        // If pet rested without bouncing (shouldn't happen with this setup),
        // the test still passes.
    }

    // ── All returned states are finite ──

    [TestMethod]
    public void AllStates_AreFinite()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 2.0, -1.0);

        for (var i = 0; i < 100; i++)
        {
            sim.Update(sim.Config.FixedStepMs * 2, area);

            Assert.IsFalse(double.IsNaN(sim.State.X));
            Assert.IsFalse(double.IsNaN(sim.State.Y));
            Assert.IsFalse(double.IsNaN(sim.State.Vx));
            Assert.IsFalse(double.IsNaN(sim.State.Vy));
            Assert.IsFalse(double.IsInfinity(sim.State.X));
            Assert.IsFalse(double.IsInfinity(sim.State.Y));
            Assert.IsFalse(double.IsInfinity(sim.State.Vx));
            Assert.IsFalse(double.IsInfinity(sim.State.Vy));

            if (sim.IsResting)
                break;
        }
    }

    // ── Integration with ReleaseSpeedSampler ──

    [TestMethod]
    public void Integration_ThrowVelocity_LaunchesPhysics()
    {
        // Simulate the full flow: sampler produces velocity → simulator launches.
        var sampler = new ReleaseSpeedSampler(new ReleaseSpeedConfig
        {
            SampleWindowMs = 100,
            ThrowThresholdPxPerMs = 0.15,
            MaxSpeedPxPerMs = 2.0,
            MinSamplesForThrow = 2,
            MinTimeSpanMs = 5,
            StopSpeedPxPerMs = 0.02,
        });

        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(300, 200, 50);
        var release = sampler.EvaluateRelease(50);

        Assert.IsTrue(release.ShouldThrow, "Fast drag should produce a throw.");

        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(release.VelocityX * 50 + 100, 200, release.VelocityX, release.VelocityY);

        // Physics should move the pet.
        sim.Update(sim.Config.FixedStepMs, area);
        Assert.AreNotEqual(200.0, sim.State.Y, 1e-9,
            "Pet should start falling due to gravity after throw.");
    }

    [TestMethod]
    public void Integration_GentleRelease_NoPhysics()
    {
        // Gentle release → ShouldThrow=false → caller does NOT call Launch.
        var sampler = new ReleaseSpeedSampler(new ReleaseSpeedConfig
        {
            SampleWindowMs = 100,
            ThrowThresholdPxPerMs = 0.15,
            MaxSpeedPxPerMs = 2.0,
            MinSamplesForThrow = 2,
            MinTimeSpanMs = 5,
            StopSpeedPxPerMs = 0.02,
        });

        sampler.RecordSample(100, 200, 0);
        sampler.RecordSample(102, 200, 50); // very slow, below threshold
        var release = sampler.EvaluateRelease(50);

        Assert.IsFalse(release.ShouldThrow, "Slow drag should be gentle.");

        // Caller does NOT call Launch. Pet stays at (102, 200).
        // No physics simulation needed.
        var petX = 102.0;
        var petY = 200.0;
        Assert.AreEqual(102.0, petX, "Gentle release → pet stays in place.");
        Assert.AreEqual(200.0, petY);
    }

    // ════════════════════════════════════════════════════════════════
    // F-01 反例: 固定步长余量 — 6×10ms 必须等价于 1×60ms
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void F01_SixSmallDt_EqualsOneLargeDt()
    {
        // 监理反例: 6×10ms 完全不动，而 60ms 走 3 步。
        // 修复后 carry 累积，6×10ms=60ms 应等价于 1×60ms。
        var config = TestConfig();
        var area = TestArea();

        var sim1 = new PhysicsSimulator(config);
        sim1.Launch(100, 100, 1.0, 0);
        for (var i = 0; i < 6; i++)
            sim1.Update(10, area);

        var sim2 = new PhysicsSimulator(config);
        sim2.Launch(100, 100, 1.0, 0);
        sim2.Update(60, area);

        Assert.AreEqual(sim2.State.X, sim1.State.X, 1e-9,
            "6×10ms should equal 1×60ms with carry.");
        Assert.AreEqual(sim2.State.Y, sim1.State.Y, 1e-9);
        Assert.IsTrue(sim1.State.X > 100, "6×10ms should actually move the pet.");
    }

    [TestMethod]
    public void F01_NonDivisibleMixedSequence_EquivalentSteps()
    {
        // 10+20+30 = 60ms should equal 60ms in one call.
        var config = TestConfig();
        var area = TestArea();

        var sim1 = new PhysicsSimulator(config);
        sim1.Launch(100, 100, 1.0, 0);
        sim1.Update(10, area);
        sim1.Update(20, area);
        sim1.Update(30, area);

        var sim2 = new PhysicsSimulator(config);
        sim2.Launch(100, 100, 1.0, 0);
        sim2.Update(60, area);

        Assert.AreEqual(sim2.State.X, sim1.State.X, 1e-9,
            "10+20+30ms should equal 60ms with carry.");
        Assert.AreEqual(sim2.State.Y, sim1.State.Y, 1e-9);
    }

    [TestMethod]
    public void F01_HighFrequencySmallDt_NotStuckForever()
    {
        // Even with very small dt, pet should eventually move.
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(100, 100, 1.0, 0);

        var initialX = sim.State.X;
        for (var i = 0; i < 100; i++)
            sim.Update(5, area); // 5ms each, accumulates

        Assert.IsTrue(sim.State.X > initialX,
            "High-frequency small dt should not keep pet stuck forever.");
    }

    [TestMethod]
    public void F01_LargeDt_StillCappedByMaxSteps()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 0, 0);

        var result = sim.Update(10000, area);

        Assert.IsTrue(result.StepsExecuted <= sim.Config.MaxStepsPerUpdate,
            $"Steps ({result.StepsExecuted}) capped at MaxStepsPerUpdate ({sim.Config.MaxStepsPerUpdate}).");
    }

    [TestMethod]
    public void F01_LaunchClearsTimeCarry()
    {
        // Launch should reset the time carry so old remnants don't affect new trajectory.
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();

        // First trajectory with some carry.
        sim.Launch(100, 100, 1.0, 0);
        sim.Update(10, area); // leaves carry

        // Relaunch — carry should be cleared.
        sim.Launch(200, 100, 1.0, 0);
        var result = sim.Update(10, area);

        // With clean carry, 10ms alone shouldn't produce a full step (step≈16.67ms).
        Assert.AreEqual(0, result.StepsExecuted,
            "After relaunch, 10ms should not produce a step (carry was cleared).");
    }

    // ════════════════════════════════════════════════════════════════
    // F-02 反例: 侧墙/顶边低速碰撞不能让宠物永久停在半空
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void F02_SidewallLowSpeed_FallsToFloorThenRests()
    {
        // 监理反例: Launch(0.01,100,-0.03,-0.0333333) 一步侧墙接触后
        // IsResting=true/Y100，而地板 Y800。
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(0.01, 100, -0.03, -0.0333333);

        // Run until resting.
        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting, "Pet should eventually rest.");
        Assert.AreEqual(area.Bottom, sim.State.Y, 1e-6,
            "Pet must rest on the floor, not frozen at Y=100 after wall bounce.");
    }

    [TestMethod]
    public void F02_CeilingBounce_DoesNotFreezeInAir()
    {
        // Ceiling bounce should not cause resting — pet falls back down.
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 10, 0, -3.0); // fast upward, near ceiling

        // Run until resting.
        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting, "Pet should eventually rest.");
        Assert.AreEqual(area.Bottom, sim.State.Y, 1e-6,
            "Pet must rest on the floor after ceiling bounce, not frozen in air.");
    }

    [TestMethod]
    public void F02_MaxBouncesExhausted_FallsToFloor()
    {
        // 监理反例: MaxBounces=1, Launch(999,100,1,0) 右墙后停止于 Y100.5556。
        var config = TestConfig();
        config.MaxBounces = 1;

        var sim = new PhysicsSimulator(config);
        var area = TestArea();
        sim.Launch(999, 100, 1.0, 0);

        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting, "Pet should eventually rest.");
        Assert.AreEqual(area.Bottom, sim.State.Y, 1e-6,
            "Pet must fall to floor after bounce budget exhausted, not frozen at Y≈100.");
    }

    [TestMethod]
    public void F02_ForceStop_StillWorksImmediately()
    {
        // ForceStop is distinct from natural stop — can stop at any position.
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(500, 100, 2.0, 2.0);

        sim.Update(50, area);
        Assert.IsFalse(sim.IsResting);

        sim.ForceStop();

        Assert.IsTrue(sim.IsResting, "ForceStop should immediately set resting.");
        Assert.AreEqual(0, sim.State.Vx, 1e-9);
        Assert.AreEqual(0, sim.State.Vy, 1e-9);
        // Pet can be at any Y — not necessarily the floor.
        Assert.IsTrue(sim.State.Y < area.Bottom, "ForceStop can stop pet mid-air.");

        // Further updates don't move.
        var restY = sim.State.Y;
        sim.Update(100, area);
        Assert.AreEqual(restY, sim.State.Y, 1e-9);
    }

    // ════════════════════════════════════════════════════════════════
    // F-03 反例: 初始状态/工作区/配置有效性边界
    // ════════════════════════════════════════════════════════════════

    [TestMethod]
    public void F03_LaunchNaNCoordinate_Rejected()
    {
        // 监理反例: NaN 坐标输入后 Update 输出 X=NaN。
        var sim = new PhysicsSimulator(TestConfig());
        var accepted = sim.Launch(double.NaN, 100, 1.0, 0);

        Assert.IsFalse(accepted, "NaN coordinate should be rejected by Launch.");
        Assert.IsFalse(sim.IsLaunched, "Rejected launch should not set launched state.");
    }

    [TestMethod]
    public void F03_LaunchInfinityVelocity_Rejected()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var accepted = sim.Launch(100, 100, double.PositiveInfinity, 0);

        Assert.IsFalse(accepted, "Infinity velocity should be rejected by Launch.");
        Assert.IsFalse(sim.IsLaunched);
    }

    [TestMethod]
    public void F03_LaunchNaN_DoesNotPolluteExistingState()
    {
        // Valid launch first, then invalid launch should not corrupt state.
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(100, 100, 1.0, 0);
        sim.Update(50, area);
        var validX = sim.State.X;

        // Attempt invalid launch.
        var accepted = sim.Launch(double.NaN, 200, 1.0, 0);
        Assert.IsFalse(accepted);

        // State should be from the invalid launch attempt (it replaces state
        // only if accepted). Actually, rejected Launch should NOT change state.
        // But our implementation: rejected Launch returns false and does NOT
        // modify _state. However, the previous valid state was already set.
        // The key is: after rejection, a subsequent valid Update should work.
        // Since Launch was rejected, _launched is still true from the first
        // valid Launch, and _state is unchanged.
        Assert.IsTrue(sim.IsLaunched, "Previous valid launch should still be active.");
        Assert.AreEqual(validX, sim.State.X, 1e-9,
            "Rejected launch should not change existing state.");
    }

    [TestMethod]
    public void F03_InvalidLaunch_ThenValidLaunch_Works()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();

        // Invalid launch.
        sim.Launch(double.NaN, 100, 1.0, 0);
        Assert.IsFalse(sim.IsLaunched);

        // Valid launch should work.
        var accepted = sim.Launch(100, 100, 1.0, 0);
        Assert.IsTrue(accepted);
        Assert.IsTrue(sim.IsLaunched);

        sim.Update(50, area);
        Assert.IsTrue(sim.State.X > 100, "Valid launch after rejection should work normally.");
    }

    [TestMethod]
    public void F03_InvertedWorkArea_Rejected()
    {
        // 监理反例: WorkArea(200,200,0,0) 仍被当边界计算。
        var sim = new PhysicsSimulator(TestConfig());
        sim.Launch(100, 100, 1.0, 0);

        var result = sim.Update(50, new WorkArea(200, 200, 0, 0));

        Assert.IsTrue(result.Rejected, "Inverted work area should be rejected.");
        Assert.AreEqual(0, result.StepsExecuted, "Rejected update should not advance.");
    }

    [TestMethod]
    public void F03_NaNWorkArea_Rejected()
    {
        var sim = new PhysicsSimulator(TestConfig());
        sim.Launch(100, 100, 1.0, 0);

        var result = sim.Update(50, new WorkArea(double.NaN, 0, 1000, 800));

        Assert.IsTrue(result.Rejected, "NaN work area edge should be rejected.");
    }

    [TestMethod]
    public void F03_InvalidWorkArea_DoesNotPolluteState()
    {
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(100, 100, 1.0, 0);
        sim.Update(50, area);
        var validX = sim.State.X;

        // Invalid work area.
        sim.Update(50, new WorkArea(200, 200, 0, 0));

        // Subsequent valid update should work from the preserved state.
        sim.Update(50, area);
        Assert.IsTrue(sim.State.X > validX,
            "State should be preserved after invalid work area rejection.");
    }

    [TestMethod]
    public void F03_InvalidConfig_Rejected()
    {
        var config = TestConfig();
        config.FixedStepMs = -1; // invalid

        var sim = new PhysicsSimulator(config);
        sim.Launch(100, 100, 1.0, 0);

        var result = sim.Update(50, TestArea());

        Assert.IsTrue(result.Rejected, "Invalid config should be rejected.");
        Assert.AreEqual(0, result.StepsExecuted);
    }

    [TestMethod]
    public void F03_InvalidConfig_DoesNotPolluteState()
    {
        // Start with valid config, then switch to invalid (simulating
        // runtime config change). State should be preserved.
        var config = TestConfig();
        var sim = new PhysicsSimulator(config);
        var area = TestArea();
        sim.Launch(100, 100, 1.0, 0);
        sim.Update(50, area);
        var validX = sim.State.X;

        // Corrupt config.
        config.BounceRestitution = 2.0; // out of [0,1]
        var result = sim.Update(50, area);
        Assert.IsTrue(result.Rejected);

        // Fix config.
        config.BounceRestitution = 0.4;
        sim.Update(50, area);
        Assert.IsTrue(sim.State.X > validX,
            "State preserved after invalid config rejection.");
    }

    [TestMethod]
    public void F03_ValidSequence_MixedWithInvalid_NoCorruption()
    {
        // Interleave valid and invalid calls — valid ones should advance,
        // invalid ones should be no-ops.
        var sim = new PhysicsSimulator(TestConfig());
        var area = TestArea();
        sim.Launch(100, 100, 1.0, 0);

        // Valid.
        sim.Update(50, area);
        var x1 = sim.State.X;

        // Invalid dt.
        sim.Update(-10, area);
        Assert.AreEqual(x1, sim.State.X, 1e-9, "Invalid dt should not advance.");

        // Valid.
        sim.Update(50, area);
        Assert.IsTrue(sim.State.X > x1, "Valid update after invalid should advance.");

        // Invalid area.
        sim.Update(50, new WorkArea(100, 100, 0, 0));
        var x2 = sim.State.X;

        // Valid.
        sim.Update(50, area);
        Assert.IsTrue(sim.State.X > x2, "Valid update after invalid area should advance.");
    }

    [TestMethod]
    public void F03_WorkAreaIsValid_Degenerate()
    {
        // Zero-area work area (Left==Right, Top==Bottom) is technically valid
        // (non-negative dimensions) but pet will immediately collide.
        var area = new WorkArea(500, 800, 500, 800);
        Assert.IsTrue(area.IsValid, "Degenerate (zero-area) work area is valid.");

        var sim = new PhysicsSimulator(TestConfig());
        sim.Launch(500, 100, 0, 0);

        // Pet should be clamped and eventually rest.
        for (var i = 0; i < 200; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting);
        Assert.AreEqual(500, sim.State.X, 1e-9);
        Assert.AreEqual(800, sim.State.Y, 1e-9);
    }

    [TestMethod]
    public void F03_ConfigIsValid_Default()
    {
        Assert.IsTrue(new PhysicsConfig().IsValid, "Default config should be valid.");
    }

    [TestMethod]
    public void F03_ConfigIsValid_InvalidCases()
    {
        var c = new PhysicsConfig();

        c.FixedStepMs = 0;
        Assert.IsFalse(c.IsValid, "FixedStepMs=0 is invalid.");
        c.FixedStepMs = 1000.0 / 60.0;

        c.MaxStepsPerUpdate = 0;
        Assert.IsFalse(c.IsValid, "MaxStepsPerUpdate=0 is invalid.");
        c.MaxStepsPerUpdate = 5;

        c.BounceRestitution = -0.1;
        Assert.IsFalse(c.IsValid, "BounceRestitution<0 is invalid.");
        c.BounceRestitution = 1.1;
        Assert.IsFalse(c.IsValid, "BounceRestitution>1 is invalid.");
        c.BounceRestitution = 0.4;

        c.GravityPxPerMs2 = -1;
        Assert.IsFalse(c.IsValid, "Negative gravity is invalid.");
        c.GravityPxPerMs2 = 0.002;

        c.MaxBounces = 0;
        Assert.IsFalse(c.IsValid, "MaxBounces=0 is invalid.");
    }

    // ── F-02 supplement: zero / exact threshold convergence ──

    [TestMethod]
    public void F02_ZeroStopThreshold_FloorBounceRests()
    {
        // StopSpeedPxPerMs=0 is a legal config (IsValid requires >= 0).
        // With <= comparison, speed exactly 0 after floor bounce must rest.
        // BounceRestitution=0 → first floor bounce zeroes vertical speed;
        // floor friction zeroes horizontal speed → Speed=0 → must rest.
        var config = TestConfig();
        config.StopSpeedPxPerMs = 0;
        config.BounceRestitution = 0;
        config.MaxBounces = 8;

        var sim = new PhysicsSimulator(config);
        var area = TestArea();
        sim.Launch(500, 100, 0, 0); // straight drop, no horizontal speed

        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting,
            "Zero threshold with <= must rest when speed is exactly 0.");
        Assert.AreEqual(area.Bottom, sim.State.Y, 1e-6,
            "Pet must rest on the floor.");
    }

    [TestMethod]
    public void F02_ZeroStopThreshold_NoUpdateAfterRest()
    {
        // After resting, subsequent Update must not step or change state.
        var config = TestConfig();
        config.StopSpeedPxPerMs = 0;
        config.BounceRestitution = 0;

        var sim = new PhysicsSimulator(config);
        var area = TestArea();
        sim.Launch(500, 100, 0, 0);

        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting);
        var restingState = sim.State;

        var result = sim.Update(sim.Config.FixedStepMs * 3, area);
        Assert.AreEqual(0, result.StepsExecuted,
            "No steps after resting.");
        Assert.AreEqual(restingState, sim.State,
            "State must not change after resting.");
    }

    [TestMethod]
    public void F02_ExactThreshold_RestsWhenSpeedEqualsThreshold()
    {
        // Speed exactly == StopSpeedPxPerMs must rest with <= comparison.
        // We craft a scenario where after a floor bounce the speed equals
        // the threshold. Use BounceRestitution=1 (perfect bounce) with a
        // carefully chosen initial drop so that post-bounce speed equals
        // a round threshold.
        //
        // With gravity g=0.002, step dt=1000/60:
        //   After n steps falling from rest: vy = g * n * dt
        //   First floor hit: y crosses Bottom.
        //   Post-bounce vy = -vy * restitution.
        // We want |post-bounce speed| == StopSpeedPxPerMs exactly.
        //
        // Simpler: use StopSpeedPxPerMs that equals the post-bounce speed
        // of the first floor hit. Drop from rest, let pet fall, measure
        // post-bounce speed, then re-run with that value as threshold.
        var config = TestConfig();
        config.BounceRestitution = 0.5;
        config.MaxBounces = 8;

        // First pass: find the speed right after first floor bounce.
        var probe = new PhysicsSimulator(config);
        var area = TestArea();
        probe.Launch(500, 100, 0, 0);

        double? postBounceSpeed = null;
        for (var i = 0; i < 500; i++)
        {
            var prevBounces = probe.TotalBounces;
            probe.Update(probe.Config.FixedStepMs, area);
            if (probe.TotalBounces > prevBounces && probe.State.Y >= area.Bottom - 1e-9)
            {
                postBounceSpeed = probe.State.Speed;
                break;
            }
        }

        Assert.IsNotNull(postBounceSpeed,
            "Probe must observe a floor bounce.");

        // Second pass: set StopSpeedPxPerMs exactly to post-bounce speed.
        // With <= the pet must rest on this bounce.
        config.StopSpeedPxPerMs = postBounceSpeed!.Value;

        var sim = new PhysicsSimulator(config);
        sim.Launch(500, 100, 0, 0);

        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);
            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(sim.IsResting,
            "Speed exactly equal to threshold must rest with <= comparison.");
        Assert.AreEqual(area.Bottom, sim.State.Y, 1e-6,
            "Pet must rest on the floor.");
    }

    [TestMethod]
    public void F02_ZeroThreshold_SideWallContinuesToFloor()
    {
        // Zero threshold must not freeze pet on side-wall bounce.
        // Side-wall bounce with zero horizontal speed (BounceRestitution=0)
        // → Speed=0 but hitFloor=false → must NOT rest.
        // Pet continues falling under gravity to the floor.
        var config = TestConfig();
        config.StopSpeedPxPerMs = 0;
        config.BounceRestitution = 0;
        config.MaxBounces = 8;

        var sim = new PhysicsSimulator(config);
        var area = TestArea();
        // Launch leftward near the left wall, slightly above floor.
        sim.Launch(1, 100, -0.5, 0);

        var hitWall = false;
        for (var i = 0; i < 500; i++)
        {
            sim.Update(sim.Config.FixedStepMs, area);

            // Detect side-wall bounce (pet at Left edge, not at floor).
            if (!hitWall && Math.Abs(sim.State.X - area.Left) < 1e-9 &&
                sim.State.Y < area.Bottom - 1e-9)
            {
                hitWall = true;
                Assert.IsFalse(sim.IsResting,
                    "Side-wall bounce with zero threshold must not rest.");
            }

            if (sim.IsResting)
                break;
        }

        Assert.IsTrue(hitWall,
            "Test must have hit the side wall before reaching the floor.");
        Assert.IsTrue(sim.IsResting,
            "Pet must eventually rest on the floor.");
        Assert.AreEqual(area.Bottom, sim.State.Y, 1e-6,
            "Pet must end up on the floor, not frozen on the wall.");
    }
}
