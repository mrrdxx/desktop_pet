namespace DesktopPet.Core.Physics;

/// <summary>
/// Configuration for <see cref="PhysicsSimulator"/>.
/// All distance values are in physical pixels; all time values in milliseconds.
/// </summary>
public sealed class PhysicsConfig
{
    /// <summary>
    /// Fixed simulation step in milliseconds. Each update advances physics
    /// by this exact dt, regardless of real frame timing. Default 1000/60
    /// ≈ 16.67 ms (60 Hz). Must be positive and finite.
    /// </summary>
    public double FixedStepMs { get; set; } = 1000.0 / 60.0;

    /// <summary>
    /// Maximum number of fixed steps to process in a single update call.
    /// If the accumulated time exceeds this many steps, the excess is
    /// discarded (prevents spiral-of-death after long stalls). Default 5.
    /// Must be at least 1.
    /// </summary>
    public int MaxStepsPerUpdate { get; set; } = 5;

    /// <summary>
    /// Gravitational acceleration in px/ms² (downward = positive Y).
    /// Default 0.002 px/ms² ≈ 2000 px/s² at 100% DPI. Must be non-negative
    /// and finite.
    /// </summary>
    public double GravityPxPerMs2 { get; set; } = 0.002;

    /// <summary>
    /// Velocity damping factor applied on each bounce (0 = no bounce,
    /// 1 = perfect bounce). The reflected velocity is multiplied by this
    /// factor. Default 0.4 — loses 60% of speed per bounce.
    /// Must be in [0, 1].
    /// </summary>
    public double BounceRestitution { get; set; } = 0.4;

    /// <summary>
    /// Speed (px/ms) at or below which the pet is considered stopped.
    /// When speed falls below this after a floor bounce, physics halts and
    /// the pet rests on the floor. Default 0.02 px/ms ≈ 20 px/s.
    /// Must be non-negative and finite.
    /// </summary>
    public double StopSpeedPxPerMs { get; set; } = 0.02;

    /// <summary>
    /// Maximum number of bounces before forcing a stop, regardless of
    /// residual speed. Prevents infinite micro-bouncing. Default 8.
    /// Must be at least 1.
    /// </summary>
    public int MaxBounces { get; set; } = 8;

    /// <summary>
    /// Returns true if all configuration values are within valid ranges.
    /// </summary>
    public bool IsValid =>
        double.IsFinite(FixedStepMs) && FixedStepMs > 0 &&
        MaxStepsPerUpdate >= 1 &&
        double.IsFinite(GravityPxPerMs2) && GravityPxPerMs2 >= 0 &&
        double.IsFinite(BounceRestitution) && BounceRestitution is >= 0 and <= 1 &&
        double.IsFinite(StopSpeedPxPerMs) && StopSpeedPxPerMs >= 0 &&
        MaxBounces >= 1;
}

/// <summary>
/// A rectangular work area in physical pixels. The pet moves within this
/// rectangle. Y increases downward (screen coordinates). The pet's anchor
/// (foot-center) is constrained so it cannot leave this rectangle.
/// A valid work area requires Left &lt;= Right, Top &lt;= Bottom, and all
/// edges finite.
/// </summary>
public readonly record struct WorkArea(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;

    /// <summary>
    /// Returns true if the work area is a valid rectangle with finite edges
    /// and non-negative dimensions.
    /// </summary>
    public bool IsValid =>
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Right) && double.IsFinite(Bottom) &&
        Left <= Right && Top <= Bottom;
}

/// <summary>
/// A single physics state snapshot: position and velocity of the pet's
/// foot-center in physical pixels.
/// </summary>
public readonly record struct PhysicsState(double X, double Y, double Vx, double Vy)
{
    public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);

    /// <summary>
    /// Returns true if all components are finite.
    /// </summary>
    public bool IsFinite =>
        double.IsFinite(X) && double.IsFinite(Y) &&
        double.IsFinite(Vx) && double.IsFinite(Vy);
}

/// <summary>
/// Result of a physics simulation update.
/// </summary>
public readonly record struct PhysicsUpdateResult
{
    /// <summary>
    /// Final state after all fixed steps in this update.
    /// </summary>
    public required PhysicsState State { get; init; }

    /// <summary>
    /// Whether the pet has come to rest (speed below stop threshold and
    /// resting on the floor). No further updates are needed.
    /// </summary>
    public required bool IsResting { get; init; }

    /// <summary>
    /// Number of bounces that occurred during this update.
    /// </summary>
    public required int BounceCount { get; init; }

    /// <summary>
    /// Number of fixed steps actually executed. May be less than
    /// requested if the pet came to rest mid-update.
    /// </summary>
    public required int StepsExecuted { get; init; }

    /// <summary>
    /// Whether the update was rejected (invalid input) and no simulation
    /// advanced. When true, the simulator state is unchanged.
    /// </summary>
    public required bool Rejected { get; init; }
}

/// <summary>
/// Pure Core logic for simulating bounded projectile physics after a
/// throw release. Implements fixed-step gravity integration, work-area
/// boundary collision with damped reflection, and final stop detection.
///
/// Design (R-04 / D-016 / ARCHITECTURE §5):
/// <list type="bullet">
/// <item><b>Fixed-step with carry</b>: sub-step time remainder is carried
///   across Update calls so that high-frequency small dt (e.g. 6×10ms)
///   produces the same steps as one larger call (e.g. 1×60ms). Only time
///   exceeding MaxStepsPerUpdate × FixedStepMs is discarded.</item>
/// <item><b>Gravity</b>: constant downward acceleration (positive Y) via
///   semi-implicit Euler: v += g*dt; x += v*dt.</item>
/// <item><b>Boundary collision</b>: reflects velocity with BounceRestitution
///   damping. Position is clamped to stay inside the work area. Floor
///   bounces also damp horizontal velocity (friction).</item>
/// <item><b>Natural stop (floor only)</b>: after a floor bounce, if speed
///   drops below StopSpeedPxPerMs, the pet stops and IsResting=true.
///   Side-wall and ceiling bounces never cause resting — the pet continues
///   to fall under gravity until it reaches the floor.</item>
/// <item><b>Bounce budget</b>: MaxBounces caps total bounces to prevent
///   infinite micro-bouncing. When the cap is reached, horizontal velocity
///   is zeroed but gravity continues — the pet falls to the floor and
///   then rests naturally.</item>
/// <item><b>ForceStop</b>: explicit immediate stop at any position (user
///   re-grab). Distinct from natural floor rest.</item>
/// <item><b>Pause/resume</b>: the caller calls ClearTimeAccumulation() on
///   resume to discard accumulated time without losing motion state.
///   Launch/Reset also clear the accumulator.</item>
/// <item><b>Input validation</b>: Launch rejects NaN/Infinity coordinates
///   and velocities. Update rejects invalid dt, invalid work area, and
///   invalid config — rejected calls do not advance simulation or pollute
///   existing valid state.</item>
/// <item><b>Gentle release</b>: ShouldThrow=false from ReleaseSpeedSampler
///   means the caller never calls Launch; the pet stays at its release
///   position.</item>
/// </list>
///
/// This class does NOT own pet position (the caller reads State.X/Y),
/// does NOT call Win32, and does NOT render anything.
/// </summary>
public sealed class PhysicsSimulator
{
    private readonly PhysicsConfig _config;
    private PhysicsState _state;
    private bool _isResting;
    private bool _launched;
    private int _totalBounces;
    private double _timeCarryMs;

    public PhysicsSimulator(PhysicsConfig? config = null)
    {
        _config = config ?? new PhysicsConfig();
    }

    public PhysicsConfig Config => _config;

    /// <summary>
    /// Current physics state (position + velocity).
    /// </summary>
    public PhysicsState State => _state;

    /// <summary>
    /// Whether the pet has come to rest on the floor and no further
    /// updates are needed.
    /// </summary>
    public bool IsResting => _isResting;

    /// <summary>
    /// Total bounces since the simulation started.
    /// </summary>
    public int TotalBounces => _totalBounces;

    /// <summary>
    /// Whether a trajectory has been launched (and not Reset).
    /// </summary>
    public bool IsLaunched => _launched;

    /// <summary>
    /// Initialize a new throw trajectory. Sets the starting position and
    /// velocity. Resets bounce count, resting state, and time accumulator.
    /// Rejects NaN/Infinity coordinates or velocities — if rejected, the
    /// existing state is unchanged and the simulator remains unlaunched.
    /// </summary>
    /// <returns>True if the launch was accepted; false if rejected due to
    /// invalid (NaN/Infinity) coordinates or velocities.</returns>
    public bool Launch(double x, double y, double vx, double vy)
    {
        var candidate = new PhysicsState(x, y, vx, vy);
        if (!candidate.IsFinite)
            return false;

        _state = candidate;
        _isResting = false;
        _launched = true;
        _totalBounces = 0;
        _timeCarryMs = 0;
        return true;
    }

    /// <summary>
    /// Advance the simulation by the given elapsed time. Processes fixed
    /// steps of FixedStepMs, carrying sub-step remainder across calls.
    /// If the pet comes to rest mid-update, remaining steps are skipped.
    ///
    /// Rejects invalid dt (NaN/Infinity/negative), invalid work area, or
    /// invalid config — rejected calls return Rejected=true without
    /// advancing simulation or polluting existing state.
    /// </summary>
    /// <param name="elapsedMs">Elapsed real time since the last update.
    /// NaN/Infinity/negative are rejected (no advancement).</param>
    /// <param name="area">Work area boundary in physical pixels.</param>
    public PhysicsUpdateResult Update(double elapsedMs, WorkArea area)
    {
        if (_isResting || !_launched)
        {
            return new PhysicsUpdateResult
            {
                State = _state,
                IsResting = _isResting,
                BounceCount = 0,
                StepsExecuted = 0,
                Rejected = false,
            };
        }

        // Reject invalid elapsed time.
        if (double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs) || elapsedMs < 0)
        {
            return new PhysicsUpdateResult
            {
                State = _state,
                IsResting = _isResting,
                BounceCount = 0,
                StepsExecuted = 0,
                Rejected = true,
            };
        }

        // Reject invalid work area.
        if (!area.IsValid)
        {
            return new PhysicsUpdateResult
            {
                State = _state,
                IsResting = _isResting,
                BounceCount = 0,
                StepsExecuted = 0,
                Rejected = true,
            };
        }

        // Reject invalid config.
        if (!_config.IsValid)
        {
            return new PhysicsUpdateResult
            {
                State = _state,
                IsResting = _isResting,
                BounceCount = 0,
                StepsExecuted = 0,
                Rejected = true,
            };
        }

        var stepMs = _config.FixedStepMs;

        // Accumulate time with carry from previous call.
        var availableMs = _timeCarryMs + elapsedMs;
        var stepsToRun = (int)(availableMs / stepMs);
        var remainder = availableMs - stepsToRun * stepMs;

        // Cap steps to prevent spiral-of-death after long stalls.
        if (stepsToRun > _config.MaxStepsPerUpdate)
        {
            stepsToRun = _config.MaxStepsPerUpdate;
            // Discard excess beyond the catch-up budget, but keep the
            // sub-step remainder from the last executed step.
            remainder = 0;
        }

        // Store carry for next call.
        _timeCarryMs = remainder;

        var bouncesThisUpdate = 0;
        var stepsExecuted = 0;

        for (var i = 0; i < stepsToRun; i++)
        {
            stepsExecuted++;

            // Gravity integration (semi-implicit Euler):
            // v += g*dt;  x += v*dt
            var dt = stepMs;
            _state = _state with
            {
                Vy = _state.Vy + _config.GravityPxPerMs2 * dt,
            };
            _state = _state with
            {
                X = _state.X + _state.Vx * dt,
                Y = _state.Y + _state.Vy * dt,
            };

            // Boundary collision.
            var (bounced, hitFloor) = ResolveCollisions(area);
            if (bounced)
            {
                _totalBounces++;
                bouncesThisUpdate++;

                // Natural stop: only on floor bounce with low speed.
                // Use <= so that zero threshold (StopSpeedPxPerMs=0) still
                // stops when speed is exactly 0, and speed exactly equal to
                // a positive threshold also stops.
                if (hitFloor && _state.Speed <= _config.StopSpeedPxPerMs)
                {
                    _state = _state with { Vx = 0, Vy = 0 };
                    _isResting = true;
                    break;
                }

                // Bounce budget exhausted: zero horizontal velocity and
                // cap upward velocity, but let gravity continue so the
                // pet falls to the floor.
                if (_totalBounces >= _config.MaxBounces)
                {
                    _state = _state with
                    {
                        Vx = 0,
                        Vy = _state.Vy < 0 ? 0 : _state.Vy,
                    };
                    // Don't set IsResting — pet still needs to fall.
                }
            }
        }

        // After all steps, if bounce budget is exhausted and pet is on
        // the floor with low speed, let it rest.
        // Use <= for consistency with the in-loop check (zero threshold
        // and exact-threshold convergence).
        if (!_isResting && _totalBounces >= _config.MaxBounces &&
            Math.Abs(_state.Y - area.Bottom) < 1e-9 &&
            _state.Speed <= _config.StopSpeedPxPerMs)
        {
            _state = _state with { Vx = 0, Vy = 0 };
            _isResting = true;
        }

        return new PhysicsUpdateResult
        {
            State = _state,
            IsResting = _isResting,
            BounceCount = bouncesThisUpdate,
            StepsExecuted = stepsExecuted,
            Rejected = false,
        };
    }

    /// <summary>
    /// Resolve collisions with the work area boundaries. Reflects velocity
    /// and clamps position. Returns (bounced, hitFloor).
    /// </summary>
    private (bool bounced, bool hitFloor) ResolveCollisions(WorkArea area)
    {
        var bounced = false;
        var hitFloor = false;
        var vx = _state.Vx;
        var vy = _state.Vy;
        var x = _state.X;
        var y = _state.Y;

        // Left boundary.
        if (x < area.Left)
        {
            x = area.Left;
            vx = -vx * _config.BounceRestitution;
            bounced = true;
        }
        // Right boundary.
        else if (x > area.Right)
        {
            x = area.Right;
            vx = -vx * _config.BounceRestitution;
            bounced = true;
        }

        // Top boundary.
        if (y < area.Top)
        {
            y = area.Top;
            vy = -vy * _config.BounceRestitution;
            bounced = true;
        }
        // Bottom boundary (floor — primary bounce surface).
        else if (y > area.Bottom)
        {
            y = area.Bottom;
            vy = -vy * _config.BounceRestitution;
            // Floor friction: damp horizontal velocity on floor bounce.
            vx *= _config.BounceRestitution;
            bounced = true;
            hitFloor = true;
        }

        if (bounced)
        {
            _state = _state with { X = x, Y = y, Vx = vx, Vy = vy };
        }

        return (bounced, hitFloor);
    }

    /// <summary>
    /// Force the pet to rest immediately at its current position.
    /// Used when the user re-grabs the pet (explicit interrupt).
    /// This is distinct from natural floor rest — the pet can be stopped
    /// at any position, including mid-air.
    /// </summary>
    public void ForceStop()
    {
        _state = _state with { Vx = 0, Vy = 0 };
        _isResting = true;
        _timeCarryMs = 0;
    }

    /// <summary>
    /// Clear accumulated time carry without changing motion state.
    /// Called by the caller on pause/resume to discard the time gap
    /// without losing the pet's current position and velocity.
    /// </summary>
    public void ClearTimeAccumulation()
    {
        _timeCarryMs = 0;
    }

    /// <summary>
    /// Reset the simulator to a clean state for reuse.
    /// </summary>
    public void Reset()
    {
        _state = default;
        _isResting = false;
        _launched = false;
        _totalBounces = 0;
        _timeCarryMs = 0;
    }
}
