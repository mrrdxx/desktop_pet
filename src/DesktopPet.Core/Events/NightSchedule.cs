namespace DesktopPet.Core.Events;

/// <summary>
/// P4-003: Night-time sleepy schedule (R-06).
/// <para>
/// The pet enters a "sleepy" state when the local time is within the night
/// window: 23:30 (inclusive) to 06:00 (exclusive) on the next day. The
/// check is purely local-time based so it naturally handles midnight
/// cross-over; callers only need to supply the current local time.
/// </para>
/// <para>
/// This class holds only the boundary logic; animation depth, priority
/// arbitration with drag/rest-reminder/etc. live in the App layer
/// (see docs/ARCHITECTURE.md "P4-003 夜间困倦接入").
/// </para>
/// </summary>
public static class NightSchedule
{
    /// <summary>
    /// Start of the night window: 23:30 local time (inclusive).
    /// </summary>
    public static readonly TimeOnly NightStart = new(23, 30);

    /// <summary>
    /// End of the night window: 06:00 local time (exclusive).
    /// </summary>
    public static readonly TimeOnly NightEnd = new(6, 0);

    /// <summary>
    /// Returns true when the given local time-of-day falls inside the
    /// night window [23:30, 24:00) ∪ [00:00, 06:00).
    /// </summary>
    public static bool IsNight(TimeOnly local)
    {
        return local >= NightStart || local < NightEnd;
    }
}
