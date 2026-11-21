namespace DesktopPet.Core.Events;

/// <summary>
/// P4-002: Core state machine for the rest-reminder performance (R-06).
/// <para>
/// Manages the three-phase rest-reminder animation sequence:
/// <list type="bullet">
/// <item><see cref="Phase.Stretching"/>: Plays the "stretch" Once action
/// (arms up / yawn) to clearly signal the rest reminder. Duration is
/// driven by the stretch action's total frame duration.</item>
/// <item><see cref="Phase.Resting"/>: Plays the "rest" Loop action (quiet,
/// eyes-closed resting pose) for the remaining time until
/// <see cref="TotalDurationMs"/> elapses.</item>
/// <item><see cref="Phase.Completed"/>: The performance has finished
/// naturally; the caller should return to idle.</item>
/// </list>
/// </para>
/// <para>
/// The total performance lasts approximately 30 seconds (configurable via
/// <see cref="TotalDurationMs"/>). The caller drives the state machine by
/// calling <see cref="Tick"/> each frame with the current monotonic time.
/// </para>
/// <para>
/// Interaction and lifecycle interruptions:
/// <list type="bullet">
/// <item><see cref="Skip"/>: Called when the user presses the pet body.
/// Immediately ends the performance (phase → Completed, reason → Skipped).
/// The caller returns to idle and enters the normal drag/petting flow.</item>
/// <item><see cref="Cancel"/>: Called when an interruption occurs that
/// should NOT continue the performance: drag, petting, social, rare event,
/// pause, lock screen, sleep, window removal, or exit. The performance
/// ends without playing the rest phase's remaining time. Recovery does
/// NOT resume the performance (no replay per R-06).</item>
/// </list>
/// </para>
/// <para>
/// The state machine does NOT play animations directly — it only tells
/// the caller which action to play and when to switch. The caller (App /
/// MainWindow) owns the AnimationPlayer and window position.
/// </para>
/// <para>
/// Design notes:
/// - The stretch phase duration is derived from the stretch action's
///   total frame duration (sum of DurationMs across frames). The caller
///   supplies this via <see cref="Start"/> so the Core remains free of
///   animation resource concerns.
/// - The rest phase fills the remaining time: TotalDurationMs - stretchDurationMs.
/// - If the stretch action is missing (duration 0), the performance goes
///   directly to the rest phase. If the rest action is also missing, the
///   performance completes immediately (safe skip with diagnostic).
/// - Pause/resume: the caller pauses the animation player; the state
///   machine's Tick is not called while paused (the timer is stopped).
///   On resume, the caller calls <see cref="Resume"/> with the pause
///   duration so the state machine's internal clock is compensated.
///   The performance does NOT replay or restart — it continues from
///   where it was paused, with the paused interval removed.
/// </para>
/// </summary>
public sealed class RestReminderPerformance
{
    /// <summary>
    /// Default total performance duration: approximately 30 seconds.
    /// R-06: "全程约 30 秒".
    /// </summary>
    public const double DefaultTotalDurationMs = 30000.0;

    /// <summary>
    /// The phases of the rest-reminder performance.
    /// </summary>
    public enum Phase
    {
        /// <summary>Not started or has ended.</summary>
        Idle,
        /// <summary>Playing the stretch/yawn opening action.</summary>
        Stretching,
        /// <summary>Playing the quiet resting pose.</summary>
        Resting,
        /// <summary>Performance has completed (naturally or via skip/cancel).</summary>
        Completed
    }

    /// <summary>
    /// The reason the performance ended.
    /// </summary>
    public enum EndReason
    {
        /// <summary>Not ended yet.</summary>
        None,
        /// <summary>Completed naturally after the full duration.</summary>
        NaturalCompletion,
        /// <summary>User pressed the pet to skip.</summary>
        Skipped,
        /// <summary>Interrupted by drag, petting, social, rare event, etc.</summary>
        Cancelled,
        /// <summary>Interrupted by pause, lock screen, or sleep.</summary>
        LifecycleInterrupt
    }

    private Phase _phase = Phase.Idle;
    private EndReason _endReason = EndReason.None;
    private double _startMs;
    private double _stretchDurationMs;
    private double _totalDurationMs;
    private double _pauseCompensationMs;

    /// <summary>Current phase of the performance.</summary>
    public Phase CurrentPhase => _phase;

    /// <summary>Why the performance ended (None if still active).</summary>
    public EndReason Reason => _endReason;

    /// <summary>True if the performance is currently active (Stretching or Resting).</summary>
    public bool IsActive => _phase == Phase.Stretching || _phase == Phase.Resting;

    /// <summary>True if the performance has completed (any end reason).</summary>
    public bool IsCompleted => _phase == Phase.Completed;

    /// <summary>
    /// The total duration of the performance in milliseconds.
    /// Set by <see cref="Start"/>.
    /// </summary>
    public double TotalDurationMs => _totalDurationMs;

    /// <summary>
    /// The stretch phase duration in milliseconds.
    /// Set by <see cref="Start"/>.
    /// </summary>
    public double StretchDurationMs => _stretchDurationMs;

    /// <summary>
    /// Starts the rest-reminder performance.
    /// </summary>
    /// <param name="nowMs">Current monotonic time in milliseconds.</param>
    /// <param name="stretchDurationMs">Total duration of the stretch action's
    /// frames in milliseconds. If 0, the stretch phase is skipped.</param>
    /// <param name="totalDurationMs">Total performance duration in milliseconds.
    /// Defaults to <see cref="DefaultTotalDurationMs"/> (30 seconds).</param>
    public void Start(double nowMs, double stretchDurationMs, double totalDurationMs = DefaultTotalDurationMs)
    {
        _phase = stretchDurationMs > 0 ? Phase.Stretching : Phase.Resting;
        _endReason = EndReason.None;
        _startMs = nowMs;
        _stretchDurationMs = stretchDurationMs;
        _totalDurationMs = totalDurationMs;
        _pauseCompensationMs = 0;
    }

    /// <summary>
    /// Advances the performance by one tick. Returns the action ID the
    /// caller should be playing at this moment, or null if the performance
    /// is not active.
    /// <para>
    /// When the performance completes naturally, the phase transitions to
    /// Completed and this method returns null (caller should switch to idle).
    /// </para>
    /// </summary>
    /// <param name="nowMs">Current monotonic time in milliseconds.</param>
    /// <returns>The action ID to play ("stretch" or "rest"), or null if
    /// the performance is not active or has just completed.</returns>
    public string? Tick(double nowMs)
    {
        if (_phase == Phase.Idle || _phase == Phase.Completed)
            return null;

        var elapsed = nowMs - _startMs - _pauseCompensationMs;

        // Check for natural completion.
        if (elapsed >= _totalDurationMs)
        {
            _phase = Phase.Completed;
            _endReason = EndReason.NaturalCompletion;
            return null;
        }

        // Phase transitions.
        if (_phase == Phase.Stretching && elapsed >= _stretchDurationMs)
        {
            _phase = Phase.Resting;
            return "rest";
        }

        return _phase == Phase.Stretching ? "stretch" : "rest";
    }

    /// <summary>
    /// User pressed the pet body to skip the performance.
    /// Immediately transitions to Completed with reason Skipped.
    /// The caller should return to idle and enter the normal interaction flow.
    /// </summary>
    public void Skip()
    {
        if (IsActive)
        {
            _phase = Phase.Completed;
            _endReason = EndReason.Skipped;
        }
    }

    /// <summary>
    /// Cancel the performance due to an interruption (drag, petting, social,
    /// rare event, window removal, exit). The performance ends without
    /// completing. Recovery does NOT resume.
    /// </summary>
    public void Cancel()
    {
        if (IsActive)
        {
            _phase = Phase.Completed;
            _endReason = EndReason.Cancelled;
        }
    }

    /// <summary>
    /// Cancel the performance due to a lifecycle interruption (pause, lock
    /// screen, sleep). The performance ends. Recovery does NOT resume
    /// (no replay per R-06).
    /// </summary>
    public void LifecycleCancel()
    {
        if (IsActive)
        {
            _phase = Phase.Completed;
            _endReason = EndReason.LifecycleInterrupt;
        }
    }

    /// <summary>
    /// Compensate the internal clock for a pause duration so the performance
    /// continues from where it was paused. Called by the caller on resume
    /// after a pause/lock/sleep, BEFORE the performance has been cancelled.
    /// <para>
    /// Note: Per R-06, pause/lock/sleep should end the performance (no
    /// replay). The caller should call <see cref="LifecycleCancel"/> on
    /// pause, not resume. This method is provided for the case where the
    /// caller decides to support pause/resume continuity in a future
    /// version. In P4-002, the caller always cancels on lifecycle interrupt.
    /// </para>
    /// </summary>
    /// <param name="pauseDurationMs">The duration of the pause in milliseconds.</param>
    public void Resume(double pauseDurationMs)
    {
        if (IsActive && pauseDurationMs > 0)
        {
            _pauseCompensationMs += pauseDurationMs;
        }
    }

    /// <summary>
    /// Resets the state machine to Idle. Called after the caller has
    /// processed the Completed state and returned to idle.
    /// </summary>
    public void Reset()
    {
        _phase = Phase.Idle;
        _endReason = EndReason.None;
        _startMs = 0;
        _stretchDurationMs = 0;
        _totalDurationMs = 0;
        _pauseCompensationMs = 0;
    }
}
