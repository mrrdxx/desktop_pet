using DesktopPet.Core.Events;

namespace DesktopPet.App.Platform;

/// <summary>
/// PREP-002 S3: Extracts the rare-event plan evaluation and effective-runtime
/// advancement logic from <c>MainWindow.OnUpdateTick</c> into a dedicated
/// platform service. The service is stateless with respect to MainWindow —
/// all dependencies (tracker, scheduler, clock, date, interaction flags) are
/// supplied by the caller. Probe start/timeout and save notification remain
/// in MainWindow to preserve the single-write-position and unified-save
/// invariants.
/// </summary>
public static class RareEventUpdateService
{
    /// <summary>
    /// Advances the effective-runtime tracker and evaluates the rare-event
    /// scheduler for the current tick.
    ///
    /// Semantics preserved from the original inline implementation:
    ///   - <paramref name="nowMs"/> is converted to monotonic minutes and
    ///     passed to <c>tracker.UpdateTo</c>.
    ///   - A plan is ensured for today via <c>EnsurePlanForDate</c> (daily
    ///     shared quota, no re-roll on same date, no carryover).
    ///   - When <paramref name="isExpressingGrievance"/> or
    ///     <paramref name="isPhysicsActive"/> is true, <c>Evaluate</c> is
    ///     skipped entirely (delayed evaluation — the plan retains its
    ///     original date, roll, delay, baseline, and unconsumed state).
    ///   - When <c>Evaluate</c> returns <c>ShouldFire</c>, the plan is marked
    ///     consumed via <c>MarkConsumed</c>.
    ///   - <c>StateChanged</c> is true when a new plan was created, the plan
    ///     date changed, the consumed flag flipped, or ShouldFire fired.
    ///   - <c>new Random()</c> is created per call, matching the original
    ///     behavior.
    ///
    /// The caller is responsible for:
    ///   - Resolving tracker/scheduler via the three-tier priority
    ///     (<c>MainWindow.Resolve</c>).
    ///   - Starting the probe (<c>StartProbe</c>) when
    ///     <see cref="RareEventUpdateResult.ShouldFire"/> is true.
    ///   - Checking probe timeout (<c>EndProbe</c>) — this remains in
    ///     <c>OnUpdateTick</c> regardless of the service result.
    ///   - Calling <c>NotifyStateDirty()</c> when
    ///     <see cref="RareEventUpdateResult.StateChanged"/> is true.
    ///   - Updating <c>_lastRareEventPlanDate</c> /
    ///     <c>_lastRareEventPlanConsumed</c> from the result snapshot.
    /// </summary>
    /// <param name="tracker">Effective runtime tracker (resolved by caller).</param>
    /// <param name="scheduler">Rare-event scheduler (resolved by caller).</param>
    /// <param name="nowMs">Current monotonic time in milliseconds (from MainWindow's clock).</param>
    /// <param name="today">Current local date as yyyy-MM-dd (from caller).</param>
    /// <param name="isDragging">Whether a drag is in progress.</param>
    /// <param name="isExpressingGrievance">Whether grievance expression is active (delays Evaluate).</param>
    /// <param name="isPhysicsActive">Whether physics is active (delays Evaluate).</param>
    /// <returns>Result indicating whether to fire, whether state changed, and the current plan snapshot.</returns>
    public static RareEventUpdateResult EvaluateTick(
        EffectiveRuntimeTracker tracker,
        RareEventScheduler scheduler,
        double nowMs,
        string today,
        bool isDragging,
        bool isExpressingGrievance,
        bool isPhysicsActive)
    {
        var monotonicMinutes = nowMs / 60000.0;
        tracker.UpdateTo(monotonicMinutes);

        var currentAccumulated = tracker.AccumulatedMinutes;
        bool planCreated = scheduler.EnsurePlanForDate(
            today, new Random(), currentAccumulatedMinutes: currentAccumulated);

        // F-09 R10 / P2-003 S3 R5 F-06: Skip Evaluate while grievance or
        // physics is active. Evaluate has a MarkConsumed side-effect — calling
        // it during these states would consume the plan without showing the
        // probe (grievance) or start a probe competing with the throw
        // trajectory (physics). The plan retains its original state; the next
        // idle tick evaluates normally.
        RareEventScheduler.TriggerResult? result = null;
        if (!isExpressingGrievance && !isPhysicsActive)
        {
            result = scheduler.Evaluate(
                currentAccumulated, today,
                isPaused: false,
                isDragging: isDragging,
                isSocialActive: false,
                isRestReminderActive: false);
        }

        // F-08: Detect plan state changes for save notification.
        // The service signals StateChanged for: new plan created (planCreated)
        // or plan consumed (ShouldFire). The caller additionally compares
        // PlanDate/PlanConsumed against its cached _lastRareEventPlanDate/
        // _lastRareEventPlanConsumed to detect cross-day drift, and ORs that
        // into the final save decision.
        var currentPlan = scheduler.CurrentPlan;
        var currentDate = currentPlan?.LocalDate;
        var currentConsumed = currentPlan?.Consumed;

        bool stateChanged = planCreated;

        if (result is not null && result.Value.ShouldFire)
        {
            scheduler.MarkConsumed();
            // ShouldFire always changes Consumed from false to true.
            stateChanged = true;
        }

        return new RareEventUpdateResult(
            ShouldFire: result is not null && result.Value.ShouldFire,
            StateChanged: stateChanged,
            PlanDate: currentDate,
            PlanConsumed: currentPlan?.Consumed);
    }
}

/// <summary>
/// Result of a rare-event update tick.
/// </summary>
/// <param name="ShouldFire">True when the rare event should fire now (caller starts probe).</param>
/// <param name="StateChanged">True when a new plan was created or the plan was consumed (caller calls NotifyStateDirty). The caller should additionally compare PlanDate/PlanConsumed against cached values to detect cross-day drift.</param>
/// <param name="PlanDate">Current plan's local date, or null if no plan exists.</param>
/// <param name="PlanConsumed">Current plan's consumed flag, or null if no plan exists.</param>
public sealed record RareEventUpdateResult(
    bool ShouldFire,
    bool StateChanged,
    string? PlanDate,
    bool? PlanConsumed);
