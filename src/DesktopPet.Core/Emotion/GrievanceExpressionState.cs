namespace DesktopPet.Core.Emotion;

/// <summary>
/// Manages the pending grievance expression state.
/// When grievance reaches threshold, marks pending with the current local date.
/// Expression fires on the next local day upon first encounter.
/// </summary>
public sealed class GrievanceExpressionState
{
    /// <summary>True when a grievance expression is pending for the next day.</summary>
    public bool IsPending { get; private set; }

    /// <summary>The local date (yyyy-MM-dd) when the pending state was marked.</summary>
    public string? MarkedDate { get; private set; }

    /// <summary>
    /// Marks a pending expression if grievance has reached threshold.
    /// Does nothing if already pending.
    /// </summary>
    /// <param name="currentLocalDate">Current local date as yyyy-MM-dd.</param>
    /// <param name="grievanceReachedThreshold">Whether grievance >= threshold.</param>
    /// <returns>True if newly marked.</returns>
    public bool TryMarkPending(string currentLocalDate, bool grievanceReachedThreshold)
    {
        if (IsPending || !grievanceReachedThreshold) return false;
        IsPending = true;
        MarkedDate = currentLocalDate;
        return true;
    }

    /// <summary>
    /// Checks whether the expression should fire now:
    /// pending is true AND current local date is strictly later than the marked date.
    /// Clock rollback (current date earlier than or equal to marked date) does not trigger.
    /// Invalid or unparseable dates are treated as "not later", so expression does not fire
    /// on corrupted date input; the caller should re-mark with a valid date if needed.
    /// </summary>
    /// <param name="currentLocalDate">Current local date as yyyy-MM-dd.</param>
    /// <returns>True if expression should fire.</returns>
    public bool ShouldExpress(string currentLocalDate)
    {
        if (!IsPending || string.IsNullOrEmpty(MarkedDate)) return false;

        // Strict date comparison: only fire when current date is after marked date.
        if (!DateOnly.TryParse(currentLocalDate, out var current))
            return false;
        if (!DateOnly.TryParse(MarkedDate, out var marked))
            return false;

        return current > marked;
    }

    /// <summary>
    /// Clears the pending state after expression completes or is interrupted.
    /// </summary>
    public void Clear()
    {
        IsPending = false;
        MarkedDate = null;
    }

    /// <summary>
    /// Restores state from archive values (for restart recovery).
    /// </summary>
    public void Restore(bool pending, string? markedDate)
    {
        IsPending = pending;
        MarkedDate = markedDate;
    }
}
