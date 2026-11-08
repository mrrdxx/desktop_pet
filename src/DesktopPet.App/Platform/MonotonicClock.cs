using System.Diagnostics;

namespace DesktopPet.App.Platform;

/// <summary>
/// PREP-002: Shared high-resolution monotonic clock for the entire application.
/// <para>
/// Encapsulates the Stopwatch-based elapsed-milliseconds logic previously
/// duplicated as <c>MainWindow._startTimeMs</c> + <c>GetElapsedMs()</c>.
/// Both <see cref="MainWindow"/> and <c>App</c> obtain time through this
/// class so that <c>App</c> no longer depends on a static accessor on
/// <c>MainWindow</c>.
/// </para>
/// <para>
/// Contract preserved from the original implementation:
/// <list type="bullet">
/// <item>Unit: <c>double</c> milliseconds.</item>
/// <item>Origin: lazily initialised on the first call (the first invocation
/// seeds <c>_startTimestampMs</c> and returns <c>0.0</c>).</item>
/// <item>Monotonic: Stopwatch.GetTimestamp (QPC) with sub-microsecond
/// resolution; never goes backwards within a process.</item>
/// <item>Pause-unaware: the clock keeps ticking during pause; pause/resume
/// compensate by adjusting timer references, not by stopping the clock.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MonotonicClock
{
    // P2-004 F-01: Use high-resolution Stopwatch instead of Environment.TickCount64.
    // TickCount64 has ~10-16ms granularity on Windows .NET 10, causing real
    // fast drags to have identical timestamps for press-down and first Move,
    // which makes RecordSample reject the Move sample (same-or-older time)
    // and lose the displacement from the velocity trajectory.
    // Stopwatch.GetTimestamp uses the QPC counter with sub-microsecond
    // resolution, converted to double milliseconds via Stopwatch.Frequency.
    private const double Uninitialised = -1;
    private double _startTimestampMs = Uninitialised;

    /// <summary>
    /// Gets the single process-wide clock instance.
    /// <para>
    /// MainWindow, App and tests all reference this instance so that they
    /// share the same lazy origin and monotonic time domain. Using a simple
    /// singleton keeps the semantics identical to the original static field
    /// (<c>_startTimeMs</c> was <c>private static</c>).
    /// </para>
    /// </summary>
    public static MonotonicClock Instance { get; } = new();

    /// <summary>
    /// Returns elapsed milliseconds since the first call to this instance.
    /// The origin is lazily seeded so the very first invocation returns 0.0.
    /// </summary>
    public double GetElapsedMs()
    {
        var now = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        if (_startTimestampMs < 0)
            _startTimestampMs = now;
        return now - _startTimestampMs;
    }
}
