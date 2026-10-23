namespace DesktopPet.Core.Input;

/// <summary>
/// Rectangle of the pet entity in physical screen pixels.
/// Used by hover/swipe detectors for distance-to-edge calculations.
/// </summary>
public readonly record struct PetRectPx(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public double CenterX => Left + Width / 2;
    public double CenterY => Top + Height / 2;
}

/// <summary>
/// Result of hover detection.
/// </summary>
public enum HoverTrigger
{
    /// <summary>
    /// No trigger this tick.
    /// </summary>
    None,
    /// <summary>
    /// Hover dwell threshold reached — fire once.
    /// </summary>
    Triggered
}

/// <summary>
/// Configuration for <see cref="HoverDetector"/>.
/// All time values are in milliseconds.
/// </summary>
public sealed class HoverDetectorConfig
{
    /// <summary>
    /// How long the cursor must stay within the entity rectangle
    /// before a hover is triggered. Default 1200 ms (R-01).
    /// </summary>
    public double DwellMs { get; set; } = 1200;

    /// <summary>
    /// How long the cursor must be outside the entity rectangle
    /// before a new hover can trigger. Default 500 ms (R-01).
    /// </summary>
    public double LeaveResetMs { get; set; } = 500;

    /// <summary>
    /// Maximum distance (in equivalent pixels) from the entity rectangle
    /// within which the cursor is considered "hovering near".
    /// If the cursor is within the rectangle or within this margin,
    /// it counts as hovering. Default 0 (must be inside the rectangle).
    /// </summary>
    public double HoverMarginPx { get; set; } = 0;
}

/// <summary>
/// Pure Core logic hover detector.
///
/// Detects when the mouse cursor dwells on or near the pet entity
/// for a configured duration (R-01: 1.2 seconds). Fires once per hover
/// session; requires the cursor to leave for a configured reset period
/// (R-01: 0.5 seconds) before a new hover can trigger.
///
/// This is pure logic with no Win32 dependencies. The caller provides
/// mouse position, pet rectangle, and a monotonic timestamp each tick.
/// </summary>
public sealed class HoverDetector
{
    private readonly HoverDetectorConfig _config;
    private double _dwellStartMs = -1;
    private double _lastInsideMs = -1;
    private double _leaveStartMs = -1;
    private bool _hasTriggered;

    public HoverDetector(HoverDetectorConfig? config = null)
    {
        _config = config ?? new HoverDetectorConfig();
    }

    public HoverDetectorConfig Config => _config;

    /// <summary>
    /// Whether the cursor is currently inside the hover zone.
    /// </summary>
    public bool IsHovering => _dwellStartMs >= 0;

    /// <summary>
    /// Whether a hover has already been triggered in the current session.
    /// </summary>
    public bool HasTriggered => _hasTriggered;

    /// <summary>
    /// Reset all state. Call when the pet is paused, dragged, or after
    /// an interruption to avoid stale dwell timing.
    /// </summary>
    public void Reset()
    {
        _dwellStartMs = -1;
        _lastInsideMs = -1;
        _leaveStartMs = -1;
        _hasTriggered = false;
    }

    /// <summary>
    /// Update the hover detector with the current mouse position and time.
    /// </summary>
    /// <param name="mouseX">Mouse X in physical pixels.</param>
    /// <param name="mouseY">Mouse Y in physical pixels.</param>
    /// <param name="petRect">Pet entity rectangle in physical pixels.</param>
    /// <param name="timeMs">Monotonic timestamp in milliseconds.</param>
    /// <returns>Triggered if the dwell threshold was reached this tick.</returns>
    public HoverTrigger Update(double mouseX, double mouseY, PetRectPx petRect, double timeMs)
    {
        var inside = IsInsideHoverZone(mouseX, mouseY, petRect);

        if (inside)
        {
            // If the cursor was outside long enough (>= LeaveResetMs since
            // the *first* outside sample), start a fresh dwell session.
            // CRITICAL: _leaveStartMs records the moment the cursor first
            // left the hover zone, not every outside sample. This prevents
            // continuous outside sampling from resetting the leave timer
            // and blocking re-trigger after a genuine leave period.
            if (_leaveStartMs >= 0 && (timeMs - _leaveStartMs) >= _config.LeaveResetMs)
            {
                _dwellStartMs = -1;
                _hasTriggered = false;
            }

            _lastInsideMs = timeMs;
            _leaveStartMs = -1;

            if (_dwellStartMs < 0)
            {
                _dwellStartMs = timeMs;
                _hasTriggered = false;
            }

            if (!_hasTriggered && (timeMs - _dwellStartMs) >= _config.DwellMs)
            {
                _hasTriggered = true;
                return HoverTrigger.Triggered;
            }
        }
        else
        {
            // Cursor is outside. Record the *first* moment of leaving
            // (inside→outside transition). Do NOT overwrite on subsequent
            // outside samples — the leave duration must be measured from
            // the actual departure, not the most recent outside tick.
            if (_leaveStartMs < 0)
                _leaveStartMs = timeMs;
        }

        return HoverTrigger.None;
    }

    private bool IsInsideHoverZone(double x, double y, PetRectPx rect)
    {
        var margin = _config.HoverMarginPx;
        return x >= rect.Left - margin && x <= rect.Right + margin &&
               y >= rect.Top - margin && y <= rect.Bottom + margin;
    }
}
