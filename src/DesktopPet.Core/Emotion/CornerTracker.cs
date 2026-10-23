namespace DesktopPet.Core.Emotion;

/// <summary>
/// Tracks corner-drag events within a 5-minute window.
/// Only awards grievance when: >= 3 corner drags AND last one stayed >= 30 seconds.
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
    /// </summary>
    /// <param name="timeMs">Monotonic timestamp in milliseconds when the drag ended.</param>
    public void RecordCornerDrag(double timeMs)
    {
        PruneExpired(timeMs);
        _cornerDragTimes.Add(timeMs);
        _lastDragEndTime = timeMs;
    }

    /// <summary>
    /// Records that the pet is currently staying in a corner after a drag.
    /// </summary>
    /// <param name="timeMs">Monotonic timestamp in milliseconds when the stay began.</param>
    public void RecordStayStart(double timeMs)
    {
        _lastDragStayStart = timeMs;
    }

    /// <summary>
    /// Checks whether the corner grievance condition is met:
    /// >= 3 corner drags in the window AND the last drag's stay >= 30 seconds.
    /// Does NOT auto-clear; caller must call <see cref="Clear"/> after awarding.
    /// </summary>
    /// <param name="timeMs">Current monotonic timestamp in milliseconds.</param>
    /// <returns>True if condition is met.</returns>
    public bool IsConditionMet(double timeMs)
    {
        PruneExpired(timeMs);
        if (_cornerDragTimes.Count < RequiredCornerDrags) return false;
        if (_lastDragEndTime is null || _lastDragStayStart is null) return false;

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
