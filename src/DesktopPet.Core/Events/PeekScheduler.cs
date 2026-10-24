namespace DesktopPet.Core.Events;

/// <summary>
/// Schedules the "peek from screen edge" rare event (PRODUCT R-03).
/// 
/// Rules:
/// - Each local day, at most one peek event across all pets.
/// - On first active tick of a day, a candidate trigger point is randomly
///   chosen within 20–120 minutes of effective (non-paused) run time.
/// - When effective run time reaches the candidate, a 50% probability check
///   determines whether the peek fires.
/// - If the probability check passes but the pet is in an interrupt state
///   (drag, pause, menu, grievance expression), the peek is deferred until
///   a suitable idle window. If no suitable window occurs that day, the
///   peek is skipped — it does NOT carry over to the next day.
/// - All plan/attempt/trigger state is persisted; restart does not re-roll.
/// - Clock rollback (current local date earlier than recorded date) does not
///   trigger a new plan or re-award the daily quota.
/// 
/// This class is pure Core logic: no WPF, no Win32, no file IO.
/// Time and randomness are injected for testability.
/// </summary>
public sealed class PeekScheduler
{
    public const int MinCandidateMinutes = 20;
    public const int MaxCandidateMinutes = 120;
    public const double TriggerProbability = 0.5;
    public const double PeekDurationMs = 5000.0;

    private readonly Random _random;

    /// <summary>Current local date (yyyy-MM-dd) for the active plan.</summary>
    public string? EventDate { get; private set; }

    /// <summary>Candidate trigger point in effective-run minutes (20–120).</summary>
    public int CandidateMinutes { get; private set; }

    /// <summary>True if the candidate point has been reached and the probability check was made.</summary>
    public bool HasAttempted { get; private set; }

    /// <summary>True if the probability check passed (peek should fire when idle).</summary>
    public bool ShouldFire { get; private set; }

    /// <summary>True if the peek has been triggered or the daily quota was consumed (probability failed).</summary>
    public bool HasTriggered { get; private set; }

    /// <summary>Accumulated effective (non-paused) run time in milliseconds for the current day.</summary>
    public double EffectiveRunMs { get; private set; }

    /// <summary>True while the peek animation is actively playing.</summary>
    public bool IsPeeking { get; private set; }

    private double _peekStartMs;

    /// <summary>
    /// Creates a PeekScheduler with a seedable Random for deterministic tests.
    /// Production code passes a new Random(); tests pass new Random(seed).
    /// </summary>
    public PeekScheduler(Random random)
    {
        _random = random;
    }

    /// <summary>
    /// Restores persisted state from archive values (for restart recovery).
    /// </summary>
    public void Restore(string? eventDate, int candidateMinutes, bool hasAttempted,
        bool shouldFire, bool hasTriggered, double effectiveRunMs)
    {
        EventDate = eventDate;
        CandidateMinutes = candidateMinutes;
        HasAttempted = hasAttempted;
        ShouldFire = shouldFire;
        HasTriggered = hasTriggered;
        EffectiveRunMs = effectiveRunMs;
        IsPeeking = false; // Never restore mid-peek; animation restarts from idle.
    }

    /// <summary>
    /// Advances the scheduler by one tick.
    /// 
    /// Parameters:
    /// - currentLocalDate: Current local date as yyyy-MM-dd (from DateTime.Now).
    /// - deltaMs: Milliseconds elapsed since the last tick (only counts if not paused).
    /// - isPaused: Whether the pet is currently paused (paused time doesn't count).
    /// - isInterrupted: Whether a high-priority interrupt is active (drag, menu,
    ///   grievance expression, etc.). Peek won't start while true.
    /// - nowMs: Monotonic timestamp in milliseconds (for peek duration tracking).
    /// 
    /// Returns a PeekTickResult describing what the caller should do.
    /// </summary>
    public PeekTickResult Tick(string currentLocalDate, double deltaMs, bool isPaused,
        bool isInterrupted, double nowMs)
    {
        // Handle date change: if the local date has advanced past the recorded
        // date, start a new daily plan. Clock rollback (current date earlier
        // than or equal to recorded date) does NOT trigger a new plan.
        if (NeedsNewPlan(currentLocalDate))
        {
            StartNewPlan(currentLocalDate);
        }

        // If no plan is active (e.g. date was rolled back and no plan exists),
        // do nothing.
        if (EventDate is null)
            return PeekTickResult.NoAction;

        // Accumulate effective run time (only when not paused).
        if (!isPaused && deltaMs > 0)
        {
            EffectiveRunMs += deltaMs;
        }

        // Handle ongoing peek animation.
        if (IsPeeking)
        {
            if (isInterrupted)
            {
                // Peek interrupted by high-priority action — end peek,
                // mark as triggered (quota consumed).
                IsPeeking = false;
                HasTriggered = true;
                return PeekTickResult.PeekInterrupted;
            }

            if (nowMs - _peekStartMs >= PeekDurationMs)
            {
                // Peek animation completed naturally.
                IsPeeking = false;
                HasTriggered = true;
                return PeekTickResult.PeekCompleted;
            }

            return PeekTickResult.PeekInProgress;
        }

        // If already triggered today, nothing more to do.
        if (HasTriggered)
            return PeekTickResult.NoAction;

        // Check if we've reached the candidate point and haven't attempted yet.
        if (!HasAttempted && EffectiveRunMs >= CandidateMinutes * 60_000.0)
        {
            HasAttempted = true;
            ShouldFire = _random.NextDouble() < TriggerProbability;

            // If probability check failed, the daily quota is consumed —
            // no peek will fire today. Mark as triggered so the scheduler
            // doesn't keep checking. This also simplifies persistence:
            // HasTriggered=true means "done for today", whether or not
            // the peek animation actually played.
            if (!ShouldFire)
            {
                HasTriggered = true;
            }
        }

        // If the probability check passed and we're not interrupted, fire the peek.
        if (HasAttempted && ShouldFire && !isInterrupted && !isPaused)
        {
            IsPeeking = true;
            _peekStartMs = nowMs;
            return PeekTickResult.PeekStarted;
        }

        return PeekTickResult.NoAction;
    }

    /// <summary>
    /// Explicitly ends the peek animation (e.g. when the app is closing or
    /// the animation system forces a return to idle).
    /// Marks the peek as triggered (quota consumed).
    /// </summary>
    public void EndPeek()
    {
        if (IsPeeking)
        {
            IsPeeking = false;
            HasTriggered = true;
        }
    }

    /// <summary>
    /// Determines whether a new daily plan is needed.
    /// A new plan is needed when:
    /// - No plan exists yet (EventDate is null), OR
    /// - The current local date is strictly later than the recorded event date.
    /// Clock rollback (current date earlier than or equal to recorded date)
    /// does NOT trigger a new plan.
    /// </summary>
    private bool NeedsNewPlan(string currentLocalDate)
    {
        if (string.IsNullOrEmpty(EventDate))
            return true;

        if (!DateOnly.TryParse(currentLocalDate, out var current))
            return false;

        if (!DateOnly.TryParse(EventDate, out var recorded))
            return false;

        return current > recorded;
    }

    /// <summary>
    /// Starts a new daily plan: picks a random candidate in [20, 120] minutes
    /// and resets all attempt/trigger state.
    /// </summary>
    private void StartNewPlan(string currentLocalDate)
    {
        EventDate = currentLocalDate;
        CandidateMinutes = _random.Next(MinCandidateMinutes, MaxCandidateMinutes + 1);
        HasAttempted = false;
        ShouldFire = false;
        HasTriggered = false;
        EffectiveRunMs = 0;
        IsPeeking = false;
    }
}

/// <summary>
/// Result of a PeekScheduler.Tick call, telling the caller what action to take.
/// </summary>
public enum PeekTickResult
{
    /// <summary>No action needed this tick.</summary>
    NoAction,

    /// <summary>Peek animation should start now. Play the peek action.</summary>
    PeekStarted,

    /// <summary>Peek animation is in progress. Continue playing.</summary>
    PeekInProgress,

    /// <summary>Peek animation completed naturally. Return to idle.</summary>
    PeekCompleted,

    /// <summary>Peek was interrupted by a high-priority action. Return to idle.</summary>
    PeekInterrupted,
}
