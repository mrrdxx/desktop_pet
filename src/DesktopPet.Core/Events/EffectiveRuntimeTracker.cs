namespace DesktopPet.Core.Events;

/// <summary>
/// Tracks accumulated effective runtime minutes, excluding paused intervals.
/// 
/// F-02 / G-01 time contract:
///   - Effective runtime is measured by a MONOTONIC time source, not UTC wall clock.
///     The caller supplies a monotonic timestamp (e.g. Stopwatch.GetTimestamp() or
///     Environment.TickCount64) converted to a double "monotonic minutes" value.
///     UTC is only used for date determination (which local day), never for
///     measuring how long the pet has been actively running.
///   - Pause: when paused, time does not accumulate. On resume, the paused
///     interval is simply not added (caller stops calling UpdateTo during pause
///     and calls StartActivePeriod with the new monotonic reference on resume).
///   - Clock adjustment: because the input is monotonic, system UTC/local clock
///     changes (NTP corrections, manual adjustments, timezone changes) do not
///     affect accumulated runtime. A monotonic clock only moves forward.
///   - Offline: time spent offline (app closed, sleeping, screensaver) does NOT
///     count as effective runtime. On restart, the caller must NOT restore the
///     old monotonic reference — instead, call StartActivePeriod with the current
///     monotonic value and only accumulate from the restart point forward.
///     The Restore method is for saving/restoring the ACCUMULATED total only;
///     the caller must call StartActivePeriod after Restore to begin a fresh
///     active interval (see G-01 contract in tests).
///   - Per-day vs session: the accumulated minutes represent total effective
///     runtime across the entire session/lifecycle. The scheduler's daily plan
///     uses the DIFFERENCE between current accumulated and the plan's baseline
///     (accumulated at plan creation time) to check if the candidate delay
///     (20–120 min) has been reached. This ensures each day's delay is relative
///     to the start of that day, not the entire session. On a new day,
///     EnsurePlanForDate creates a new plan with its own delay and baseline,
///     and the accumulated minutes continue from where they were.
///     This matches R-03: "20–120 minutes of effective runtime" since the pet
///     started running that day, not since the session began.
/// </summary>
public sealed class EffectiveRuntimeTracker
{
    private double _accumulatedMinutes;
    private double? _lastActiveMonotonicMinutes;

    /// <summary>Current accumulated effective runtime in minutes.</summary>
    public double AccumulatedMinutes => _accumulatedMinutes;

    /// <summary>
    /// Marks the start of an active (non-paused) period.
    /// Call this when the pet becomes active (initial start or resume from pause).
    /// 
    /// G-01: After Restore, the caller MUST call StartActivePeriod with the
    /// current monotonic time before calling UpdateTo. This ensures offline
    /// time is not counted as effective runtime.
    /// </summary>
    /// <param name="monotonicMinutes">Current monotonic time in minutes from a monotonic source. Must be a finite, non-negative value.</param>
    public void StartActivePeriod(double monotonicMinutes)
    {
        // F-03: Reject invalid inputs without modifying existing state.
        if (double.IsNaN(monotonicMinutes) || double.IsInfinity(monotonicMinutes) || monotonicMinutes < 0)
            return;

        _lastActiveMonotonicMinutes = monotonicMinutes;
    }

    /// <summary>
    /// Marks the end of an active period (pause begins).
    /// Adds the elapsed time since the last StartActivePeriod to the accumulator.
    /// </summary>
    /// <param name="monotonicMinutes">Current monotonic time in minutes. Must be a finite, non-negative value.</param>
    public void EndActivePeriod(double monotonicMinutes)
    {
        if (_lastActiveMonotonicMinutes is null)
            return;

        // F-03: Reject invalid inputs without modifying existing state.
        if (double.IsNaN(monotonicMinutes) || double.IsInfinity(monotonicMinutes) || monotonicMinutes < 0)
            return;

        var elapsed = monotonicMinutes - _lastActiveMonotonicMinutes.Value;
        if (elapsed > 0)
        {
            _accumulatedMinutes += elapsed;
        }

        _lastActiveMonotonicMinutes = null;
    }

    /// <summary>
    /// Updates the accumulator to the current monotonic time while active.
    /// Call this on each tick during active (non-paused) operation.
    /// Returns the total accumulated minutes including the current active interval.
    /// 
    /// F-02: Input must be a finite, non-negative monotonic value.
    /// F-03: NaN and Infinity are rejected without modifying state.
    /// </summary>
    /// <param name="monotonicMinutes">Current monotonic time in minutes from a monotonic source.</param>
    /// <returns>Total effective runtime in minutes.</returns>
    public double UpdateTo(double monotonicMinutes)
    {
        if (_lastActiveMonotonicMinutes is null)
            return _accumulatedMinutes;

        // F-03: Reject invalid inputs.
        if (double.IsNaN(monotonicMinutes) || double.IsInfinity(monotonicMinutes))
            return _accumulatedMinutes;

        var elapsed = monotonicMinutes - _lastActiveMonotonicMinutes.Value;
        if (elapsed > 0)
        {
            _accumulatedMinutes += elapsed;
            _lastActiveMonotonicMinutes = monotonicMinutes;
        }
        // Monotonic clock should not go backward. If it does (e.g. counter
        // reset), treat as zero elapsed and do not move the reference backward.

        return _accumulatedMinutes;
    }

    /// <summary>
    /// Restores accumulated minutes from a saved snapshot.
    /// 
    /// G-01: This method restores ONLY the accumulated total. It does NOT
    /// restore the active interval reference — the caller must call
    /// StartActivePeriod with the current monotonic time to begin accumulating
    /// again. This ensures offline time (app closed, sleeping) is not counted
    /// as effective runtime.
    /// 
    /// F-03: Invalid accumulatedMinutes (NaN, Infinity, negative) are clamped
    /// to 0 and the restore is accepted with the corrected value.
    /// </summary>
    /// <param name="accumulatedMinutes">Previously accumulated effective runtime. Invalid values are clamped to 0.</param>
    public void Restore(double accumulatedMinutes)
    {
        if (double.IsNaN(accumulatedMinutes) || double.IsInfinity(accumulatedMinutes) || accumulatedMinutes < 0)
            _accumulatedMinutes = 0;
        else
            _accumulatedMinutes = accumulatedMinutes;

        // Do NOT restore _lastActiveMonotonicMinutes — caller must call
        // StartActivePeriod to begin a fresh active interval.
        _lastActiveMonotonicMinutes = null;
    }

    /// <summary>The monotonic timestamp (in minutes) when the current active period started, or null if paused.</summary>
    public double? LastActiveMonotonicMinutes => _lastActiveMonotonicMinutes;
}
