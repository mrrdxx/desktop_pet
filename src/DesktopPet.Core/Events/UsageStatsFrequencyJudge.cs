using System.Globalization;
using DesktopPet.Core.Persistence;

namespace DesktopPet.Core.Events;

/// <summary>
/// P6-003: tunable thresholds for the local frequency judge. The defaults are
/// deliberately conservative <b>starting values</b>; they have not been
/// validated against real prediction quality, so the output is a statistical
/// basis only — never a "reliability rate" or a claim about user intent.
/// </summary>
public sealed class UsageStatsFrequencyOptions
{
    /// <summary>Look-back window in complete local days (default 30).</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>Minimum distinct dates with any valid statistics (default 7).</summary>
    public const int DefaultMinObservedDates = 7;

    /// <summary>Minimum distinct dates the app appears in the same half-hour slot (default 5).</summary>
    public const int DefaultMinSupportDates = 5;

    /// <summary>Minimum accumulated sessions for that app+slot in the window (default 5).</summary>
    public const int DefaultMinSessionCount = 5;

    /// <summary>Look-back window in complete local days (excludes the running day).</summary>
    public int WindowDays { get; init; } = DefaultWindowDays;

    /// <summary>
    /// Aggregated retention ceiling in days (P6-002 keeps
    /// <see cref="UsageStatsCollector.AggregatedRetentionDays"/> = 30 days keyed
    /// by counted date, i.e. <c>[today−29, today]</c>). The effective query
    /// window is the intersection with this ceiling so an expired bucket can
    /// never support a prediction — not even before the first normal drain
    /// prunes it. This is a read-only filter; the query never writes or prunes.
    /// </summary>
    public int RetentionDays { get; init; } = UsageStatsCollector.AggregatedRetentionDays;

    /// <summary>
    /// When true (default) the not-yet-finished current local day is excluded
    /// from the window: it neither supports nor contradicts a candidate.
    /// </summary>
    public bool ExcludeToday { get; init; } = true;

    /// <summary>Minimum distinct dates with any valid statistics.</summary>
    public int MinObservedDates { get; init; } = DefaultMinObservedDates;

    /// <summary>Minimum distinct dates the app appears in the same half-hour slot.</summary>
    public int MinSupportDates { get; init; } = DefaultMinSupportDates;

    /// <summary>Minimum accumulated sessions in that app+slot.</summary>
    public int MinSessionCount { get; init; } = DefaultMinSessionCount;
}

/// <summary>
/// P6-003: one internal frequency candidate. These fields are statistical
/// evidence (app identity, half-hour slot, how many distinct dates supported
/// it, how many sessions accumulated), not an accuracy figure.
/// </summary>
public sealed class UsageStatsFrequencyCandidate
{
    /// <summary>Normalized executable base name (same identity as the buckets).</summary>
    public string AppId { get; init; } = "";

    /// <summary>Half-hour slot start, local time-of-day as "HH:mm" (e.g. "09:30").</summary>
    public string SlotStart { get; init; } = "";

    /// <summary>Distinct dates in the window where this app had verified time in this slot.</summary>
    public int SupportDateCount { get; init; }

    /// <summary>Accumulated sessions in that app+slot (one use = one session).</summary>
    public int SessionCount { get; init; }
}

/// <summary>
/// P6-003: result of the frequency judge.
/// <para>
/// <see cref="ObservedDateCount"/> is the number of distinct dates with any
/// valid statistics in the window. It is <b>not</b> a count of fully observed
/// days: a missing date means no verified record, not that the user did not
/// use the app.
/// </para>
/// </summary>
public sealed class UsageStatsFrequencyResult
{
    /// <summary>Whether stats collection was enabled at evaluation time.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Effective look-back window in complete local dates that was actually
    /// applied: the nominal <see cref="UsageStatsFrequencyOptions.WindowDays"/>
    /// intersected with the aggregated retention cap. With retention 30 and
    /// today excluded this is 29, never a full 30-day history.
    /// </summary>
    public int WindowDays { get; init; }

    /// <summary>Distinct dates with any valid statistics in the window (not fully observed days).</summary>
    public int ObservedDateCount { get; init; }

    /// <summary>Qualifying candidates, ordered by support dates then sessions, descending.</summary>
    public IReadOnlyList<UsageStatsFrequencyCandidate> Candidates { get; init; }
        = Array.Empty<UsageStatsFrequencyCandidate>();

    /// <summary>True when no candidate met the thresholds (insufficient samples or disabled).</summary>
    public bool IsSuppressed => Candidates.Count == 0;
}

/// <summary>
/// P6-003: pure, stateless local frequency judge built on the P6-002
/// aggregated buckets. No persistence, no cache — the caller re-evaluates from
/// the current committed buckets, so disabling or clearing makes the next
/// evaluation empty immediately (PRODUCT R-09 / ARCHITECTURE §7).
/// <para>
/// Window: the nominal <see cref="UsageStatsFrequencyOptions.WindowDays"/>
/// complete local dates before "today", intersected with the aggregated
/// retention range — so an out-of-retention bucket is excluded from the very
/// first evaluation, not only after a drain prunes it. The running day is
/// excluded (unfinished, so it neither supports nor contradicts); future dates
/// are excluded too.
/// </para>
/// <para>
/// "Appears in a slot" means a bucket for that app+half-hour slot with
/// <c>ActiveMs &gt; 0</c> existed on that date. Sessions are summed from the
/// buckets' <c>SessionCount</c> (a session is booked once, in its starting
/// bucket). A bucket whose <c>BucketStartLocal</c> cannot be parsed is skipped.
/// </para>
/// </summary>
public static class UsageStatsFrequencyJudge
{
    private const string SlotFormat = "HH:mm";

    /// <summary>
    /// Evaluates the aggregated buckets and returns internal frequency
    /// candidates. Returns an empty result when collection is disabled or the
    /// observation thresholds are not met.
    /// </summary>
    /// <param name="buckets">Committed P6-002 buckets (read-only input).</param>
    /// <param name="localNow">Current local wall clock (injected for tests).</param>
    /// <param name="enabled">Global stats switch state; false → no prediction.</param>
    /// <param name="options">Optional thresholds (defaults are conservative).</param>
    public static UsageStatsFrequencyResult Evaluate(
        IEnumerable<UsageStatsBucket>? buckets,
        DateTimeOffset localNow,
        bool enabled,
        UsageStatsFrequencyOptions? options = null)
    {
        options ??= new UsageStatsFrequencyOptions();
        var windowDays = Math.Max(1, options.WindowDays);
        var retentionDays = Math.Max(1, options.RetentionDays);

        var today = DateOnly.FromDateTime(localNow.DateTime);

        // Nominal window: `WindowDays` complete local dates before today.
        // Intersect it with the P6-002 aggregated retention range
        // `[today−(RetentionDays−1), today]` so a bucket already outside
        // retention can never support a prediction — not even before the first
        // normal drain prunes it. This is a pure date filter: the query never
        // writes or prunes.
        var endExclusive = options.ExcludeToday ? today : today.AddDays(1);
        var nominalFirstDay = today.AddDays(-windowDays);
        var retentionOldestDay = today.AddDays(-(retentionDays - 1));
        var firstDay = nominalFirstDay > retentionOldestDay ? nominalFirstDay : retentionOldestDay;

        // Report how many dates can actually participate, not the nominal
        // WindowDays: with retention [today−29, today] and today excluded, at
        // most 29 complete dates are available (never a full 30-day history).
        var appliedWindowDays = Math.Max(0, endExclusive.DayNumber - firstDay.DayNumber);

        if (!enabled)
            return new UsageStatsFrequencyResult { IsEnabled = false, WindowDays = appliedWindowDays };

        var observedDates = new HashSet<DateOnly>();
        var groups = new Dictionary<(string AppId, string Slot), GroupAccumulator>();

        if (buckets is not null)
        {
            foreach (var bucket in buckets)
            {
                if (bucket is null) continue;

                var appId = bucket.AppId?.Trim();
                if (string.IsNullOrEmpty(appId)) continue;

                // "Appears" requires verified foreground time in the bucket.
                if (!(bucket.ActiveMs > 0.0)) continue;

                if (!DateTimeOffset.TryParse(
                        bucket.BucketStartLocal,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var start))
                {
                    continue;
                }

                var date = DateOnly.FromDateTime(start.DateTime);
                if (date < firstDay || date >= endExclusive) continue;

                observedDates.Add(date);

                var slot = start.ToString(SlotFormat, CultureInfo.InvariantCulture);
                var key = (appId, slot);
                if (!groups.TryGetValue(key, out var accumulator))
                {
                    accumulator = new GroupAccumulator();
                    groups[key] = accumulator;
                }
                accumulator.Dates.Add(date);
                if (bucket.SessionCount > 0)
                    accumulator.SessionCount += bucket.SessionCount;
            }
        }

        var candidates = new List<UsageStatsFrequencyCandidate>();
        if (observedDates.Count >= Math.Max(1, options.MinObservedDates))
        {
            foreach (var pair in groups)
            {
                if (pair.Value.Dates.Count < Math.Max(1, options.MinSupportDates)) continue;
                if (pair.Value.SessionCount < Math.Max(1, options.MinSessionCount)) continue;

                candidates.Add(new UsageStatsFrequencyCandidate
                {
                    AppId = pair.Key.AppId,
                    SlotStart = pair.Key.Slot,
                    SupportDateCount = pair.Value.Dates.Count,
                    SessionCount = pair.Value.SessionCount
                });
            }
            candidates.Sort(CompareCandidates);
        }

        return new UsageStatsFrequencyResult
        {
            IsEnabled = true,
            WindowDays = appliedWindowDays,
            ObservedDateCount = observedDates.Count,
            Candidates = candidates
        };
    }

    private static int CompareCandidates(
        UsageStatsFrequencyCandidate a,
        UsageStatsFrequencyCandidate b)
    {
        var c = b.SupportDateCount.CompareTo(a.SupportDateCount);
        if (c != 0) return c;

        c = b.SessionCount.CompareTo(a.SessionCount);
        if (c != 0) return c;

        c = string.CompareOrdinal(a.AppId, b.AppId);
        return c != 0 ? c : string.CompareOrdinal(a.SlotStart, b.SlotStart);
    }

    private sealed class GroupAccumulator
    {
        public HashSet<DateOnly> Dates { get; } = new();
        public int SessionCount { get; set; }
    }
}
