namespace DesktopPet.Core.Input;

/// <summary>
/// Result of swipe detection.
/// </summary>
public enum SwipeTrigger
{
    /// <summary>
    /// No trigger this tick.
    /// </summary>
    None,
    /// <summary>
    /// Swipe detected — fire dodge action.
    /// </summary>
    Triggered
}

/// <summary>
/// Configuration for <see cref="SwipeDetector"/>.
/// Distance thresholds are in equivalent 100%-scale pixels.
/// </summary>
public sealed class SwipeDetectorConfig
{
    /// <summary>
    /// Minimum equivalent recent segment speed to trigger a swipe.
    /// Default 1200 eq px/s (R-01).
    /// </summary>
    public double SpeedThreshold { get; set; } = 1200;

    /// <summary>
    /// Maximum distance from the pet entity edge (in equivalent pixels)
    /// for a swipe to trigger. Default 80 eq px (R-01).
    /// </summary>
    public double EdgeDistanceThreshold { get; set; } = 80;

    /// <summary>
    /// Cooldown after a swipe trigger before a new one can fire.
    /// Default 3000 ms (R-01).
    /// </summary>
    public double CooldownMs { get; set; } = 3000;

    /// <summary>
    /// DPI scale of the monitor the pet is currently on.
    /// Used to convert physical pixel distances to equivalent 100%-scale
    /// pixels for the edge distance check.
    /// 1.0 = 100%, 1.5 = 150%, 2.0 = 200%.
    /// The caller should keep this in sync with MouseSampler.Config.DpiScale.
    /// </summary>
    public double DpiScale { get; set; } = 1.0;
}

/// <summary>
/// Pure Core logic swipe (flick) detector.
///
/// Detects fast mouse movement near the pet entity per R-01:
/// - Speed signal: EquivalentRecentSegmentSpeedPxPerSec from MouseSampler
///   (recent segment max, not cancelled by direction reversal).
/// - Distance signal: cursor-to-entity-edge distance in equivalent pixels.
/// - Both signals must be true simultaneously in the same tick.
/// - Cooldown prevents repeated triggers.
///
/// Critical design: the speed signal (EquivalentRecentSegmentSpeedPxPerSec)
/// and the distance signal (PeakSegmentEnd position) both come from the same
/// segment — the one that produced the peak speed. This prevents the "fast
/// swipe from far then slowly approach" false positive where an old speed
/// peak would be combined with a new close distance reading from a different,
/// slower segment.
///
/// This is pure logic with no Win32 dependencies.
/// </summary>
public sealed class SwipeDetector
{
    private readonly SwipeDetectorConfig _config;
    private double _lastTriggerMs = -1;

    public SwipeDetector(SwipeDetectorConfig? config = null)
    {
        _config = config ?? new SwipeDetectorConfig();
    }

    public SwipeDetectorConfig Config => _config;

    /// <summary>
    /// Whether the detector is currently in cooldown.
    /// </summary>
    public bool IsInCooldown => _lastTriggerMs >= 0;

    /// <summary>
    /// Reset all state. Call when the pet is paused, dragged, or after
    /// an interruption to avoid stale cooldown timing.
    /// </summary>
    public void Reset()
    {
        _lastTriggerMs = -1;
    }

    /// <summary>
    /// Update the swipe detector with the current sample result and pet rectangle.
    ///
    /// Both the speed signal (from the sample result) and the distance signal
    /// (from the peak segment's endpoint vs pet rectangle) are evaluated in the
    /// same call. The peak segment endpoint ensures the distance check
    /// corresponds to the segment that actually produced the speed, not a
    /// later slower segment that merely moved closer.
    /// </summary>
    /// <param name="sample">Current mouse sample result from MouseSampler.</param>
    /// <param name="petRect">Pet entity rectangle in physical pixels.</param>
    /// <param name="timeMs">Monotonic timestamp in milliseconds.</param>
    /// <returns>Triggered if a swipe was detected this tick.</returns>
    public SwipeTrigger Update(MouseSampleResult sample, PetRectPx petRect, double timeMs)
    {
        if (!sample.IsValid)
            return SwipeTrigger.None;

        // Check cooldown.
        if (_lastTriggerMs >= 0 && (timeMs - _lastTriggerMs) < _config.CooldownMs)
            return SwipeTrigger.None;

        // Speed signal: equivalent recent segment speed must exceed threshold.
        // This is the primary signal per R-01 — it captures short bursts of
        // fast movement without being diluted or cancelled.
        if (sample.EquivalentRecentSegmentSpeedPxPerSec < _config.SpeedThreshold)
            return SwipeTrigger.None;

        // Distance signal: the position of the segment that produced the
        // peak speed must be within the edge distance threshold of the pet.
        //
        // CRITICAL: We use sample.PeakSegmentEnd — the endpoint of the
        // segment that generated the speed reading — NOT sample.Latest.
        // This prevents combining an old speed peak (from a fast segment
        // that ended far away) with a newer close position (from a slow
        // segment that happened to move near the pet). The speed and
        // distance signals now come from the same segment.
        var distEq = DistanceToEdgeEquivalent(
            sample.PeakSegmentEnd.X, sample.PeakSegmentEnd.Y, petRect, _config.DpiScale);

        if (distEq > _config.EdgeDistanceThreshold)
            return SwipeTrigger.None;

        // Both conditions met: trigger swipe.
        _lastTriggerMs = timeMs;
        return SwipeTrigger.Triggered;
    }

    /// <summary>
    /// Compute the distance from a point to the nearest edge of a rectangle
    /// in equivalent pixels. If the point is inside the rectangle, distance is 0.
    /// </summary>
    private static double DistanceToEdgeEquivalent(
        double x, double y, PetRectPx rect, double dpiScale)
    {
        // Physical pixel distance to nearest edge.
        double dx = 0, dy = 0;

        if (x < rect.Left) dx = rect.Left - x;
        else if (x > rect.Right) dx = x - rect.Right;

        if (y < rect.Top) dy = rect.Top - y;
        else if (y > rect.Bottom) dy = y - rect.Bottom;

        var distPx = Math.Sqrt(dx * dx + dy * dy);

        // Convert to equivalent pixels using the DPI scale derived from the
        // sample's physical-to-equivalent ratio.
        var scale = dpiScale > 0 ? dpiScale : 1.0;
        return distPx / scale;
    }
}
