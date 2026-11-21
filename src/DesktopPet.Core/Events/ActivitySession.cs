namespace DesktopPet.Core.Events;

/// <summary>
/// P4-001: Global active-session tracker for rest-reminder timing (R-06).
/// <para>
/// This is independent of <see cref="EffectiveRuntimeTracker"/> (rare-event
/// timing). The activity session measures user-active time, not just
/// process-uptime: it only accumulates when the user has had recent input
/// within the active window, and resets after a continuous idle period.
/// </para>
/// <para>
/// Core responsibilities (timer + threshold):
/// <list type="bullet">
/// <item>Accumulate active minutes when the user is "active" (last input
/// within <see cref="ActiveWindowSeconds"/>).</item>
/// <item>Stop accumulating when idle beyond the active window but before
/// the idle-reset threshold — a brief reading pause.</item>
/// <item>Reset the session (accumulated → 0, reminder fired → false) when
/// idle continuously for <see cref="IdleResetSeconds"/> (5 minutes).</item>
/// <item>Fire exactly one rest-reminder intent when accumulated active
/// minutes reach <see cref="ThresholdMinutes"/> (120 minutes). The same
/// session does not re-fire.</item>
/// <item>After a reset (idle 5 min) or a manual <see cref="Reset"/>, a new
/// session can fire again once the threshold is reached.</item>
/// <item>Lock/screen-sleep/pause: caller stops calling <see cref="Tick"/>
/// and calls <see cref="Suspend"/>. On resume, caller calls
/// <see cref="Resume"/> with the new monotonic baseline. The suspended
/// interval is discarded — no backfill, no reminder replay.</item>
/// </list>
/// </para>
/// <para>
/// All time inputs are monotonic milliseconds from a single monotonic
/// source (same domain as <see cref="EffectiveRuntimeTracker"/>). The
/// caller (App) is responsible for sampling input idle and lifecycle
/// state, then feeding concise parameters to this Core class.
/// </para>
/// </summary>
public sealed class ActivitySession
{
    /// <summary>
    /// Default active window in seconds: if the last user input was within
    /// this many seconds, the user is considered "active". R-06 initial
    /// value: 60 seconds (subject to experience calibration).
    /// </summary>
    public const double ActiveWindowSeconds = 60.0;

    /// <summary>
    /// Default idle-reset threshold in seconds: if the user remains idle
    /// (no input) continuously for this long, the session resets.
    /// R-06: 5 minutes = 300 seconds.
    /// </summary>
    public const double IdleResetSeconds = 300.0;

    /// <summary>
    /// Default accumulated-active threshold in minutes: when the session's
    /// accumulated active time reaches this value, a rest-reminder intent
    /// fires exactly once. R-06: 2 hours = 120 minutes.
    /// </summary>
    public const double ThresholdMinutes = 120.0;

    /// <summary>
    /// F-04 R4 M-11: Configurable threshold in minutes for the rest-reminder
    /// intent. Defaults to <see cref="ThresholdMinutes"/> (120 minutes).
    /// Isolation probes can inject a shorter threshold (e.g. 1 minute) to
    /// observe reminder behaviour without waiting 2 hours. Production code
    /// always uses the default.
    /// </summary>
    public double EffectiveThresholdMinutes { get; } = ThresholdMinutes;

    /// <summary>
    /// F-01 R2: Maximum allowed gap between two consecutive ticks before
    /// the interval is considered unreliable and discarded. If the gap
    /// between the previous tick and the current tick exceeds this value,
    /// no time is accumulated for that interval — the session rebases
    /// to the current tick instead. This prevents backfilling long
    /// unobserved intervals (e.g. process was busy, sampling paused).
    /// Set to 5 seconds (5000 ms), which is 5× the normal 1-second
    /// sampling interval — enough tolerance for timer jitter and brief
    /// scheduling delays, but far below the 5-minute idle-reset and
    /// 2-hour threshold. R1 used 10 minutes which allowed a 9-minute
    /// unobserved gap to be backfilled; R2 reduces this to a sampling-
    /// rhythm-appropriate tolerance.
    /// </summary>
    public const double MaxSampleGapMs = 5.0 * 1000.0;

    private double _accumulatedActiveMs;
    private double? _lastTickMs;
    private double? _lastInputMs;
    private double? _idleStartMs;
    private bool _reminderFired;
    private bool _suspended;
    // F-01 R3: When true, the next valid tick only re-establishes the
    // baseline (_lastTickMs = nowMs) without accumulating. Set after a
    // missing sample, long gap, or backward tick so the first reliable
    // sample after the break does not backfill the unreliable interval.
    private bool _pendingRebase;

    /// <summary>
    /// Creates a session with the default 120-minute reminder threshold.
    /// </summary>
    public ActivitySession() { }

    /// <summary>
    /// F-04 R4 M-11: Creates a session with a custom reminder threshold.
    /// Used by isolation probes to observe reminder behaviour with a
    /// shorter threshold. Production code uses the parameterless
    /// constructor (120 minutes).
    /// </summary>
    /// <param name="thresholdMinutes">Accumulated-active threshold in
    /// minutes at which the rest-reminder intent fires once.</param>
    public ActivitySession(double thresholdMinutes)
    {
        EffectiveThresholdMinutes = thresholdMinutes;
    }

    /// <summary>
    /// Total accumulated active milliseconds in the current session.
    /// Resets to 0 on idle-reset or explicit <see cref="Reset"/>.
    /// </summary>
    public double AccumulatedActiveMs => _accumulatedActiveMs;

    /// <summary>
    /// Accumulated active time in minutes (convenience accessor).
    /// </summary>
    public double AccumulatedActiveMinutes => _accumulatedActiveMs / 60000.0;

    /// <summary>
    /// Whether the rest-reminder intent has already fired in this session.
    /// Becomes false again on reset.
    /// </summary>
    public bool ReminderFired => _reminderFired;

    /// <summary>
    /// Whether the session is currently suspended (lock/sleep/pause).
    /// While suspended, <see cref="Tick"/> is a no-op.
    /// </summary>
    public bool IsSuspended => _suspended;

    /// <summary>
    /// The monotonic timestamp (ms) of the last successful tick, or null
    /// if no tick has occurred yet.
    /// </summary>
    public double? LastTickMs => _lastTickMs;

    /// <summary>
    /// Advances the session by one tick.
    /// <para>
    /// The caller supplies the current monotonic time and the monotonic
    /// timestamp of the last user input (both in milliseconds from the
    /// same monotonic source). The method determines whether the user is
    /// active (input within the active window), accumulates active time,
    /// checks for idle-reset, and fires the reminder intent if the
    /// threshold is reached.
    /// </para>
    /// <para>
    /// When suspended (after <see cref="Suspend"/>), this method is a
    /// no-op until <see cref="Resume"/> is called.
    /// </para>
    /// </summary>
    /// <param name="nowMs">Current monotonic time in milliseconds.</param>
    /// <param name="lastInputMs">Monotonic timestamp of the last user input
    /// in milliseconds. If null, the user has never had input (treated as
    /// idle since the beginning).</param>
    /// <returns>True if the rest-reminder intent fires on this tick
    /// (exactly once per session); false otherwise.</returns>
    public bool Tick(double nowMs, double? lastInputMs)
    {
        if (_suspended)
            return false;

        // Reject invalid monotonic inputs.
        if (double.IsNaN(nowMs) || double.IsInfinity(nowMs) || nowMs < 0)
            return false;

        // F-01 R2: Detect backward (non-monotonic) time. If nowMs is
        // less than the last tick, the monotonic clock has moved
        // backward (e.g. replayed time in tests, clock anomaly). We
        // must NOT accumulate the interval and must NOT move the
        // baseline backward — the baseline stays at the higher value.
        // Discard this tick entirely without changing any state, so
        // the next forward tick measures from the original baseline.
        if (_lastTickMs is { } prevTickForBackward && nowMs < prevTickForBackward)
        {
            return false;
        }

        // F-01 R1/R2/R3: Detect long sampling gaps. If the gap between the
        // previous tick and this tick exceeds MaxSampleGapMs, the
        // interval is unreliable — we cannot know what happened during
        // the gap. Discard the interval and rebase to nowMs without
        // accumulating. This also clears idle tracking so a stale idle
        // start does not trigger a false reset.
        // F-01 R3: Set _pendingRebase so the NEXT valid tick only
        // establishes the baseline without accumulating the gap.
        if (_lastTickMs is { } prevTickForGap)
        {
            var gap = nowMs - prevTickForGap;
            if (gap >= MaxSampleGapMs)
            {
                // Unreliable interval — rebase without accumulating.
                _lastTickMs = nowMs;
                _idleStartMs = null;
                _lastInputMs = null;
                _pendingRebase = true;
                return false;
            }
        }

        // F-01 R1: Determine input validity. A null lastInputMs means
        // the input age is unknown (API failure, missing sample). This
        // is NOT the same as "user has been idle" — we cannot claim the
        // user rested. We rebase without accumulating and without
        // entering the idle-reset path.
        bool hasValidInput = false;
        bool isUserActive = false;

        if (lastInputMs is { } inputMs)
        {
            if (double.IsNaN(inputMs) || double.IsInfinity(inputMs))
            {
                // Invalid input timestamp — treat as missing sample.
                hasValidInput = false;
            }
            else if (inputMs > nowMs)
            {
                // F-01 R1: Future timestamp — the input clock is ahead
                // of the monotonic clock. This is not reliable evidence
                // of recent activity. Treat as missing sample.
                // F-03 R3: Negative inputMs is NOT rejected here — it
                // represents input that occurred before the process
                // started (before the monotonic clock origin). The input
                // age is still computed as (nowMs - inputMs), which is
                // a valid positive value. Only future timestamps
                // (inputMs > nowMs) are rejected.
                hasValidInput = false;
            }
            else
            {
                hasValidInput = true;
                var idleSeconds = (nowMs - inputMs) / 1000.0;
                isUserActive = idleSeconds <= ActiveWindowSeconds;
            }
        }

        if (!hasValidInput)
        {
            // F-01 R1/R3: Missing or unreliable input sample. We cannot
            // determine whether the user is active or idle. Rebase the
            // tick reference to nowMs without accumulating and without
            // entering the idle-reset path. This prevents a null sample
            // from being misinterpreted as "user has rested 5 minutes"
            // and clearing accumulated time.
            // F-01 R3: Set _pendingRebase so the next valid tick only
            // establishes the baseline without accumulating the gap
            // from the missing sample to the next valid sample.
            _lastTickMs = nowMs;
            _idleStartMs = null;
            _lastInputMs = null;
            _pendingRebase = true;
            return false;
        }

        _lastInputMs = lastInputMs;

        // F-01 R3: If the previous tick was a missing sample, long gap,
        // or backward tick, this is the first reliable sample after the
        // break. It only establishes the baseline — do NOT accumulate
        // the interval from the break to now. The next tick will start
        // accumulating from this baseline.
        if (_pendingRebase)
        {
            // F-01 R4: The first valid sample after a missing/long-gap/
            // backward tick only establishes the baseline — do NOT
            // accumulate the interval from the break to now.
            //
            // R4 fix: preserve the active/idle STATE of this sample
            // instead of unconditionally clearing _idleStartMs.
            //
            // - If the sample is ACTIVE: clear _idleStartMs (user is
            //   active right now). The next tick sees wasIdle=false and
            //   accumulates normally from this baseline.
            //
            // - If the sample is IDLE: set _idleStartMs = lastInputMs
            //   (user is idle right now). The next active tick sees
            //   wasIdle=true and takes the idle→active transition path,
            //   which does NOT accumulate the idle interval — it just
            //   starts a fresh reference. This prevents the bug where
            //   clearing _idleStartMs made the next active tick treat
            //   itself as "continuously active" and accumulate the
            //   full gap (e.g. 61000→62000ms).
            _pendingRebase = false;
            _lastTickMs = nowMs;
            if (isUserActive)
                _idleStartMs = null;
            else
                _idleStartMs = lastInputMs;
            return false;
        }

        if (isUserActive)
        {
            // User is active — clear any idle tracking.
            var wasIdle = _idleStartMs is not null;
            _idleStartMs = null;

            if (wasIdle)
            {
                // Transition from idle back to active: do NOT accumulate
                // the idle→active gap. Start a fresh reference at nowMs.
                _lastTickMs = nowMs;
            }
            else if (_lastTickMs is { } prevMs)
            {
                // Continuous active: accumulate elapsed since last tick.
                var elapsed = nowMs - prevMs;
                if (elapsed > 0)
                    _accumulatedActiveMs += elapsed;
                _lastTickMs = nowMs;
            }
            else
            {
                // First tick ever — just set the reference.
                _lastTickMs = nowMs;
            }
        }
        else
        {
            // User is idle (beyond active window) with valid input data.
            if (_idleStartMs is null)
            {
                // Transition from active to idle — record the start of idle.
                // Use the last input time as the idle start, not nowMs,
                // because the user became idle when they stopped inputting.
                _idleStartMs = lastInputMs;
            }

            var idleDurationSeconds = (nowMs - _idleStartMs!.Value) / 1000.0;

            if (idleDurationSeconds >= IdleResetSeconds)
            {
                // Continuous idle ≥ 5 minutes → reset the session.
                Reset();
                _lastTickMs = nowMs;
                return false;
            }

            // Idle but not yet 5 minutes — do not accumulate and do NOT
            // update _lastTickMs. When the user resumes input, the active
            // branch detects the idle→active transition and starts a fresh
            // reference, so the idle interval is not counted.
        }

        // Check threshold: fire exactly once per session.
        if (!_reminderFired && _accumulatedActiveMs / 60000.0 >= EffectiveThresholdMinutes)
        {
            _reminderFired = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Suspends the session (lock screen / sleep / pause).
    /// <para>
    /// After suspension, <see cref="Tick"/> becomes a no-op. The caller
    /// must call <see cref="Resume"/> with the new monotonic baseline to
    /// resume accumulation. The suspended interval is discarded.
    /// </para>
    /// <para>
    /// If the user was idle when suspension occurred, the partial idle
    /// period is also discarded — on resume, the idle timer restarts from
    /// the next tick.
    /// </para>
    /// </summary>
    public void Suspend()
    {
        _suspended = true;
        // Clear the idle start so that on resume, idle timing restarts
        // from the new baseline rather than counting the suspended gap.
        _idleStartMs = null;
    }

    /// <summary>
    /// Resumes the session after a suspension (unlock / wake / unpause).
    /// <para>
    /// The caller supplies the current monotonic time. The tick reference
    /// is set to this value so the next <see cref="Tick"/> measures elapsed
    /// from the resume point. The suspended interval is not backfilled.
    /// </para>
    /// <para>
    /// The accumulated active time and reminder-fired state are preserved
    /// across suspend/resume — only the time reference is reset. A full
    /// reset happens only on idle-reset (5 min continuous idle) or explicit
    /// <see cref="Reset"/>.
    /// </para>
    /// </summary>
    /// <param name="nowMs">Current monotonic time in milliseconds at the
    /// moment of resume.</param>
    public void Resume(double nowMs)
    {
        if (double.IsNaN(nowMs) || double.IsInfinity(nowMs) || nowMs < 0)
            return;

        _suspended = false;
        _lastTickMs = nowMs;
        _idleStartMs = null;
    }

    /// <summary>
    /// Resets the session: accumulated active time → 0, reminder fired → false.
    /// Called internally on idle-reset (5 min continuous idle) and can be
    /// called externally for testing or explicit session reset.
    /// </summary>
    public void Reset()
    {
        _accumulatedActiveMs = 0;
        _reminderFired = false;
        _idleStartMs = null;
        _pendingRebase = false;
    }
}
