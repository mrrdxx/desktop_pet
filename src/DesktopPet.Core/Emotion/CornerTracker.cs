namespace DesktopPet.Core.Emotion;

/// <summary>
/// Tracks corner-drag events within a 5-minute window.
/// Only awards grievance when: >= 3 corner drags AND the stay after the LAST
/// drag release is continuous for >= 30 seconds.
/// Not persisted across restarts.
/// </summary>
public sealed class CornerTracker
{
    public const int RequiredCornerDrags = 3;
    public const double WindowMinutes = 5.0;
    public const double RequiredStaySeconds = 30.0;

    private readonly List<double> _cornerDragTimes = new();
    private double? _lastDragEndTime;
    private double? _lastDragStayStart;

    /// <summary>
    /// Records a drag that ended in a corner zone.
    /// A new drag end cancels any previous stay, because the pet was moved
    /// and the 30-second stay must be continuous after the final release.
    /// </summary>
    /// <param name="timeMs">Monotonic timestamp in milliseconds when the drag ended.</param>
    public void RecordCornerDrag(double timeMs)
    {
        PruneExpired(timeMs);
        _cornerDragTimes.Add(timeMs);
        _lastDragEndTime = timeMs;
        // New drag end invalidates any previous stay — the pet was just moved.
        _lastDragStayStart = null;
    }

    /// <summary>
    /// Records that the pet is currently staying in a corner after the last drag release.
    /// Only valid if called after the most recent RecordCornerDrag.
    /// </summary>
    /// <param name="timeMs">Monotonic timestamp in milliseconds when the stay began.</param>
    public void RecordStayStart(double timeMs)
    {
        // Stay must begin at or after the last drag end.
        // If no drag has been recorded, ignore the stay.
        if (_lastDragEndTime is null) return;
        _lastDragStayStart = timeMs;
    }

    /// <summary>
    /// Cancels the current stay (e.g. pet left the corner zone or was dragged again).
    /// The next stay must start fresh after a new drag release.
    /// </summary>
    public void CancelStay()
    {
        _lastDragStayStart = null;
    }

    /// <summary>
    /// Checks whether the corner grievance condition is met:
    /// >= 3 corner drags in the window AND a continuous stay of >= 30 seconds
    /// that began at or after the last drag release.
    /// Does NOT auto-clear; caller must call <see cref="Clear"/> after awarding.
    /// </summary>
    /// <param name="timeMs">Current monotonic timestamp in milliseconds.</param>
    /// <returns>True if condition is met.</returns>
    public bool IsConditionMet(double timeMs)
    {
        PruneExpired(timeMs);
        if (_cornerDragTimes.Count < RequiredCornerDrags) return false;
        if (_lastDragEndTime is null || _lastDragStayStart is null) return false;

        // Stay must begin at or after the last drag release.
        if (_lastDragStayStart.Value < _lastDragEndTime.Value) return false;

        var stayDuration = (timeMs - _lastDragStayStart.Value) / 1000.0;
        return stayDuration >= RequiredStaySeconds;
    }

    /// <summary>Clears all corner tracking state. Call after awarding grievance or when window expires.</summary>
    public void Clear()
    {
        _cornerDragTimes.Clear();
        _lastDragEndTime = null;
        _lastDragStayStart = null;
    }

    /// <summary>Current count of corner drags in the active window.</summary>
    public int CornerDragCount => _cornerDragTimes.Count;

    /// <summary>True if a stay is currently being tracked (non-null and after last drag end).</summary>
    public bool IsStayActive => _lastDragStayStart is not null
        && _lastDragEndTime is not null
        && _lastDragStayStart.Value >= _lastDragEndTime.Value;

    private void PruneExpired(double currentTimeMs)
    {
        var windowMs = WindowMinutes * 60 * 1000;
        var threshold = currentTimeMs - windowMs;
        _cornerDragTimes.RemoveAll(t => t < threshold);

        // If all drags expired, reset stay tracking too.
        if (_cornerDragTimes.Count == 0)
        {
            _lastDragEndTime = null;
            _lastDragStayStart = null;
        }
    }
}
