using System.Globalization;

namespace DesktopPet.Core.Events;

/// <summary>
/// Coordinates the daily rare-event schedule for all pets in the process.
/// Rules (PRODUCT R-03):
///   - "Once per day" = at most one event across all pets per local natural day.
///   - Each day gets a candidate trigger point at 20–120 minutes of effective runtime.
///   - At the candidate point, a 50% probability gate determines if the event fires.
///   - The daily plan (date, delay, roll, consumed) is snapshot-persisted; restart does not re-roll.
///   - Events do not start during drag, social, rest reminder, or pause.
///   - If the day ends without an opportunity, the event is skipped (no carryover).
///   - Local date rollback does not re-grant a consumed day's quota.
/// </summary>
public sealed class RareEventScheduler
{
    /// <summary>
    /// Reasons the scheduler may decline to trigger at a given tick.
    /// </summary>
    public enum DenyReason
    {
        None,
        Paused,
        Dragging,
        SocialActive,
        RestReminderActive,
        PlanConsumed,
        DelayNotReached,
        RollFailed,
        DateRollback,
        DateAdvanced,
        InvalidRuntime,
        InvalidPlan,
        InvalidDate
    }

    /// <summary>Result of a trigger evaluation at a single tick.</summary>
    public readonly struct TriggerResult
    {
        public TriggerResult(bool shouldFire, DenyReason denyReason, RareEventPlan? updatedPlan)
        {
            ShouldFire = shouldFire;
            Reason = denyReason;
            UpdatedPlan = updatedPlan;
        }

        /// <summary>True when the rare event should fire now.</summary>
        public bool ShouldFire { get; }

        /// <summary>Why the event was declined (None when ShouldFire is true).</summary>
        public DenyReason Reason { get; }

        /// <summary>
        /// The updated plan after this evaluation (consumed if fired or rolled-fail).
        /// Null when no plan exists yet or date rollback prevented evaluation.
        /// </summary>
        public RareEventPlan? UpdatedPlan { get; }
    }

    private RareEventPlan? _plan;
    private readonly double _triggerProbability;

    /// <summary>
    /// Creates a scheduler with an optional custom trigger probability (default 0.5).
    /// </summary>
    /// <param name="triggerProbability">Probability of triggering at the candidate point.</param>
    public RareEventScheduler(double triggerProbability = RareEventPlan.DefaultTriggerProbability)
    {
        _triggerProbability = triggerProbability;
    }

    /// <summary>Current daily plan, or null if no plan has been created.</summary>
    public RareEventPlan? CurrentPlan => _plan;

    /// <summary>
    /// Ensures a plan exists for the given local date. If the date has advanced
    /// from the existing plan's date, a new plan is created. If the date has
    /// moved backward (rollback), the existing plan is preserved and no new
    /// plan is created — a consumed plan stays consumed.
    /// </summary>
    /// <param name="localDate">Current local date as yyyy-MM-dd.</param>
    /// <param name="random">Random source for plan creation (only used when a new plan is needed).</param>
    /// <param name="minDelayMinutes">Minimum effective runtime minutes (default 20).</param>
    /// <param name="maxDelayMinutes">Maximum effective runtime minutes (default 120).</param>
    /// <param name="currentAccumulatedMinutes">F-05: Current session-level accumulated effective runtime minutes. Used as the daily plan's baseline so the delay is relative to the start of the day, not the entire session. Default 0.</param>
    /// <returns>True if a new plan was created for this date.</returns>
    public bool EnsurePlanForDate(
        string localDate,
        Random random,
        int minDelayMinutes = RareEventPlan.MinTriggerDelayMinutes,
        int maxDelayMinutes = RareEventPlan.MaxTriggerDelayMinutes,
        double currentAccumulatedMinutes = 0.0)
    {
        if (_plan is not null)
        {
            if (!DateOnly.TryParse(localDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var current))
                return false;
            if (!DateOnly.TryParse(_plan.LocalDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var existing))
                return false;

            // Same date: keep existing plan (no re-roll).
            if (current == existing)
                return false;

            // Date moved backward (rollback): keep existing plan, do not create new.
            if (current < existing)
                return false;

            // Date moved forward: create new plan for the new day.
        }

        _plan = RareEventPlan.CreateNew(localDate, random, minDelayMinutes, maxDelayMinutes, currentAccumulatedMinutes);
        return true;
    }

    /// <summary>
    /// Evaluates whether the rare event should fire at the current tick.
    /// Does NOT start the event during drag, social, rest reminder, or pause.
    /// If the candidate delay is reached and the roll passes, marks the plan consumed.
    /// If the roll fails, also marks consumed (one attempt per day).
    /// 
    /// F-01: The current local date must match the plan's date. If the date
    /// has moved backward (rollback), evaluation is denied with DateRollback
    /// and the plan is NOT consumed. This prevents re-triggering on a date
    /// whose quota was already used.
    /// 
    /// F-03: Invalid effectiveRuntimeMinutes (NaN, Infinity, negative) are
    /// rejected with InvalidRuntime and do not consume the plan.
    /// </summary>
    /// <param name="effectiveRuntimeMinutes">Accumulated effective (non-paused, non-idle-excluded) runtime in minutes. Must be a finite, non-negative number.</param>
    /// <param name="currentLocalDate">Current local date as yyyy-MM-dd. Must match the plan's date for evaluation to proceed.</param>
    /// <param name="isPaused">Whether the pet is currently paused.</param>
    /// <param name="isDragging">Whether a drag is in progress.</param>
    /// <param name="isSocialActive">Whether a social session is active.</param>
    /// <param name="isRestReminderActive">Whether a rest reminder is in progress.</param>
    /// <returns>Trigger result with decision, reason, and updated plan.</returns>
    public TriggerResult Evaluate(
        double effectiveRuntimeMinutes,
        string currentLocalDate,
        bool isPaused = false,
        bool isDragging = false,
        bool isSocialActive = false,
        bool isRestReminderActive = false)
    {
        if (_plan is null)
            return new TriggerResult(false, DenyReason.None, null);

        // F-03: Reject invalid runtime values without consuming the plan.
        if (double.IsNaN(effectiveRuntimeMinutes) || double.IsInfinity(effectiveRuntimeMinutes) || effectiveRuntimeMinutes < 0)
            return new TriggerResult(false, DenyReason.InvalidRuntime, _plan);

        // F-03: Reject if the current plan is invalid (bad snapshot).
        if (!_plan.IsValid)
            return new TriggerResult(false, DenyReason.InvalidPlan, _plan);

        // F-01: Date consistency check — current date must match plan date.
        if (!DateOnly.TryParse(currentLocalDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentDate))
            return new TriggerResult(false, DenyReason.InvalidDate, _plan);

        if (!DateOnly.TryParse(_plan.LocalDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var planDate))
            return new TriggerResult(false, DenyReason.InvalidPlan, _plan);

        // F-01: Date rollback — current date is earlier than plan date.
        // Deny without consuming; prevents re-triggering on an already-used date.
        if (currentDate < planDate)
            return new TriggerResult(false, DenyReason.DateRollback, _plan);

        // F-01: Date advanced but EnsurePlanForDate not yet called for new date.
        // Deny without consuming; caller should call EnsurePlanForDate first.
        if (currentDate > planDate)
            return new TriggerResult(false, DenyReason.DateAdvanced, _plan);

        if (isPaused)
            return new TriggerResult(false, DenyReason.Paused, _plan);

        if (isDragging)
            return new TriggerResult(false, DenyReason.Dragging, _plan);

        if (isSocialActive)
            return new TriggerResult(false, DenyReason.SocialActive, _plan);

        if (isRestReminderActive)
            return new TriggerResult(false, DenyReason.RestReminderActive, _plan);

        if (_plan.Consumed)
            return new TriggerResult(false, DenyReason.PlanConsumed, _plan);

        // F-05: Daily effective runtime = total accumulated - plan's baseline.
        // The delay is relative to the start of the day (when the plan was created),
        // not the entire session. This prevents a new day's short delay from firing
        // immediately when the previous day already accumulated significant runtime.
        var dailyEffectiveMinutes = effectiveRuntimeMinutes - _plan.AccumulatedBaselineMinutes;

        if (dailyEffectiveMinutes < _plan.TriggerDelayMinutes)
            return new TriggerResult(false, DenyReason.DelayNotReached, _plan);

        // Delay reached: evaluate the pre-rolled trigger.
        var consumed = _plan.AsConsumed();
        _plan = consumed;

        if (_plan!.ShouldTrigger(_triggerProbability))
        {
            return new TriggerResult(true, DenyReason.None, consumed);
        }

        return new TriggerResult(false, DenyReason.RollFailed, consumed);
    }

    /// <summary>
    /// Forces the current plan to be marked consumed without firing.
    /// Used when the day ends without an opportunity (skip, no carryover).
    /// </summary>
    public void MarkConsumed()
    {
        if (_plan is not null && !_plan.Consumed)
        {
            _plan = _plan.AsConsumed();
        }
    }

    /// <summary>
    /// Restores a plan from a saved snapshot (for restart recovery).
    /// F-03: Only accepts plans that pass IsValid validation.
    /// If the saved plan is invalid (bad date, NaN roll, out-of-range delay),
    /// it is rejected and the existing plan (if any) is preserved.
    /// Returns true if the plan was accepted, false if rejected.
    /// </summary>
    /// <param name="saved">The saved plan snapshot, or null if none.</param>
    /// <returns>True if the plan was valid and accepted; false if rejected or null.</returns>
    public bool RestorePlan(RareEventPlan? saved)
    {
        if (saved is null)
        {
            _plan = null;
            return true;
        }

        if (!saved.IsValid)
            return false;

        _plan = saved;
        return true;
    }

    /// <summary>
    /// Returns a snapshot of the current plan suitable for persistence.
    /// Null when no plan exists.
    /// </summary>
    public RareEventPlan? GetSnapshot() => _plan;
}
