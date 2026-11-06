using System.Globalization;

namespace DesktopPet.Core.Events;

/// <summary>
/// Immutable snapshot of a single day's rare-event plan.
/// Persisted to archive so that restart does not re-roll the daily schedule.
/// All fields are serializable for snapshot save/restore.
/// </summary>
public sealed class RareEventPlan
{
    /// <summary>Local date (yyyy-MM-dd) that this plan applies to.</summary>
    public string LocalDate { get; init; } = string.Empty;

    /// <summary>
    /// Effective runtime minutes from session start until the candidate trigger point.
    /// Range: [MinTriggerDelayMinutes, MaxTriggerDelayMinutes] (20–120 by default).
    /// </summary>
    public int TriggerDelayMinutes { get; init; }

    /// <summary>
    /// Pre-rolled random value (0.0–1.0) compared against TriggerProbability at trigger time.
    /// Stored so that restart produces the same trigger decision.
    /// </summary>
    public double TriggerRoll { get; init; }

    /// <summary>True when the event has already been triggered or skipped for this day.</summary>
    public bool Consumed { get; init; }

    /// <summary>
    /// F-05: Session-level accumulated minutes at the time this daily plan was created.
    /// The daily trigger delay is relative to this baseline, not the total session
    /// accumulated minutes. This prevents a new day's short delay from firing
    /// immediately when the previous day already accumulated significant runtime.
    /// Example: Day1 accumulated 130 min and fired. Day2 creates a plan with delay=20.
    /// If baseline=130, then day2 needs accumulated >= 150 (130+20) to fire, not just 130.
    /// </summary>
    public double AccumulatedBaselineMinutes { get; init; }

    /// <summary>Default minimum effective runtime minutes before trigger.</summary>
    public const int MinTriggerDelayMinutes = 20;

    /// <summary>Default maximum effective runtime minutes before trigger.</summary>
    public const int MaxTriggerDelayMinutes = 120;

    /// <summary>Default trigger probability (50%).</summary>
    public const double DefaultTriggerProbability = 0.5;

    /// <summary>
    /// Validates all fields of this plan.
    /// A plan is valid when:
    ///   - LocalDate is a parseable yyyy-MM-dd date.
    ///   - TriggerDelayMinutes is in [0, MaxTriggerDelayMinutes].
    ///   - TriggerRoll is a finite number in [0.0, 1.0).
    /// </summary>
    public bool IsValid =>
        DateOnly.TryParse(LocalDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        && TriggerDelayMinutes >= 0
        && TriggerDelayMinutes <= MaxTriggerDelayMinutes
        && !double.IsNaN(TriggerRoll)
        && !double.IsInfinity(TriggerRoll)
        && TriggerRoll >= 0.0
        && TriggerRoll < 1.0
        && !double.IsNaN(AccumulatedBaselineMinutes)
        && !double.IsInfinity(AccumulatedBaselineMinutes)
        && AccumulatedBaselineMinutes >= 0;

    /// <summary>
    /// Creates a new plan for the given local date using a provided random source.
    /// The trigger delay is uniformly distributed in [minDelay, maxDelay] minutes.
    /// A trigger roll is pre-generated so that restart yields the same outcome.
    /// Throws if localDate is not a valid yyyy-MM-dd date.
    /// </summary>
    /// <param name="localDate">Local date as yyyy-MM-dd.</param>
    /// <param name="random">Controlled random source for deterministic testing.</param>
    /// <param name="minDelayMinutes">Minimum effective runtime minutes before trigger (default 20).</param>
    /// <param name="maxDelayMinutes">Maximum effective runtime minutes before trigger (default 120).</param>
    /// <param name="accumulatedBaselineMinutes">F-05: Session-level accumulated minutes at plan creation time. The daily delay is relative to this baseline. Must be a finite, non-negative value (default 0).</param>
    /// <exception cref="ArgumentException">Thrown when localDate is not a valid date.</exception>
    public static RareEventPlan CreateNew(
        string localDate,
        Random random,
        int minDelayMinutes = MinTriggerDelayMinutes,
        int maxDelayMinutes = MaxTriggerDelayMinutes,
        double accumulatedBaselineMinutes = 0.0)
    {
        if (!DateOnly.TryParse(localDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException($"Invalid local date: '{localDate}'. Expected yyyy-MM-dd.", nameof(localDate));

        if (minDelayMinutes < 0) minDelayMinutes = 0;
        if (maxDelayMinutes < minDelayMinutes) maxDelayMinutes = minDelayMinutes;

        // F-05: Sanitize baseline.
        if (double.IsNaN(accumulatedBaselineMinutes) || double.IsInfinity(accumulatedBaselineMinutes) || accumulatedBaselineMinutes < 0)
            accumulatedBaselineMinutes = 0;

        var delayMinutes = random.Next(minDelayMinutes, maxDelayMinutes + 1);
        var roll = random.NextDouble();

        return new RareEventPlan
        {
            LocalDate = localDate,
            TriggerDelayMinutes = delayMinutes,
            TriggerRoll = roll,
            Consumed = false,
            AccumulatedBaselineMinutes = accumulatedBaselineMinutes
        };
    }

    /// <summary>
    /// Determines whether the pre-rolled trigger roll passes the probability gate.
    /// </summary>
    /// <param name="triggerProbability">Probability threshold (default 0.5).</param>
    /// <returns>True if the roll indicates the event should fire.</returns>
    public bool ShouldTrigger(double triggerProbability = DefaultTriggerProbability)
    {
        return TriggerRoll < triggerProbability;
    }

    /// <summary>
    /// Returns a copy of this plan marked as consumed.
    /// </summary>
    public RareEventPlan AsConsumed() => new()
    {
        LocalDate = LocalDate,
        TriggerDelayMinutes = TriggerDelayMinutes,
        TriggerRoll = TriggerRoll,
        Consumed = true,
        AccumulatedBaselineMinutes = AccumulatedBaselineMinutes
    };
}
