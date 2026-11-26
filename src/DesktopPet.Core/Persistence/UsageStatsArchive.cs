namespace DesktopPet.Core.Persistence;

/// <summary>
/// P6-001: A single approximated foreground-active time segment attributed
/// to one normalized executable base name. Persisted as an original record
/// in stats.json.
/// <para>
/// Time contract (per PRODUCT R-09 / ARCHITECTURE §7):
/// <list type="bullet">
/// <item><see cref="StartLocal"/> carries the explicit local calendar date,
/// time and UTC offset. <see cref="Date"/> is the local date of the start
/// (the day the activity is counted against).</item>
/// <item><see cref="DurationMs"/> is a monotonic-clock difference, never a
/// wall-clock difference. The implied end is therefore
/// <c>StartLocal + DurationMs</c> (approximate; the local clock and the
/// monotonic clock are not converted to each other).</item>
/// <item>Cross-midnight active periods are split into one segment per local
/// day at midnight. Gaps (idle input, sample gaps, lock/sleep/pause) are
/// never back-filled.</item>
/// </list>
/// </para>
/// </summary>
public sealed class UsageStatsSegment
{
    /// <summary>
    /// Normalized executable base name (lower-cased file name without
    /// extension) of the foreground application. Never a full path and
    /// never a PID. Same-name programs merge into one identity.
    /// </summary>
    public string AppId { get; set; } = "";

    /// <summary>
    /// Local date (yyyy-MM-dd) the segment is counted against.
    /// This is the retention key: raw records expire by this date.
    /// </summary>
    public string Date { get; set; } = "";

    /// <summary>
    /// Local wall-clock start of the segment, ISO 8601 with UTC offset
    /// (round-trip "O" format), e.g. "2026-11-26T10:05:00.0000000+08:00".
    /// </summary>
    public string StartLocal { get; set; } = "";

    /// <summary>
    /// Monotonic-difference duration in milliseconds. Must be &gt; 0.
    /// </summary>
    public double DurationMs { get; set; }
}

/// <summary>
/// P6-001: A lifecycle event observation. Only the three supported event
/// kinds are recorded: app-start (pet itself starting), unlock (session
/// unlock / session resume), resume (power resume from sleep/hibernate).
/// boot is intentionally never recorded (no independent verifiable source).
/// Events are kept apart from foreground segments so the four sources never
/// mix names (an external app is never called a "start").
/// </summary>
public sealed class UsageStatsEventRecord
{
    /// <summary>One of <c>app-start</c>, <c>unlock</c>, <c>resume</c>.</summary>
    public string EventType { get; set; } = "";

    /// <summary>Local date (yyyy-MM-dd) the event occurred on.</summary>
    public string Date { get; set; } = "";

    /// <summary>
    /// Local wall-clock occurrence, ISO 8601 with UTC offset ("O" format).
    /// </summary>
    public string OccurredLocal { get; set; } = "";
}

/// <summary>
/// P6-002: One aggregated fixed 30-minute bucket for a single app. Derived
/// timely from verified foreground intervals (never from raw-record
/// expiration), so aggregation does not wait for the raw data to age out.
/// <para>
/// Frequency contract (PRODUCT R-09 / ARCHITECTURE §7): a "use" is a
/// <b>session</b> — a maximal uninterrupted chain of verified foreground
/// intervals for the same app (interrupted by idle/gap/suspension/disable/
/// app switch). <see cref="SessionCount"/> increments once per session, in
/// the bucket containing the session's start, so a session that crosses a
/// bucket (or midnight) boundary contributes its duration to each bucket it
/// occupies but is counted as one use only. Each 5 s raw sample is never a
/// use. A bucket never reconstructs fine-grained trajectories.
/// </para>
/// </summary>
public sealed class UsageStatsBucket
{
    /// <summary>Normalized executable base name (same identity as raw segments).</summary>
    public string AppId { get; set; } = "";

    /// <summary>
    /// Local date (yyyy-MM-dd) the bucket is counted against — the local date
    /// of the bucket start. This is the retention key: aggregated buckets
    /// expire by this counted date (30 days), independent of raw retention.
    /// </summary>
    public string Date { get; set; } = "";

    /// <summary>
    /// Local wall-clock bucket start floored to the fixed 30-minute grid,
    /// ISO 8601 with UTC offset (round-trip "O" format).
    /// </summary>
    public string BucketStartLocal { get; set; } = "";

    /// <summary>Total verified foreground-active milliseconds in this bucket (monotonic differences).</summary>
    public double ActiveMs { get; set; }

    /// <summary>Number of distinct foreground sessions that started in this bucket.</summary>
    public int SessionCount { get; set; }
}

/// <summary>
/// P6-001: Independent statistics archive (stats.json).
/// <para>
/// Storage is deliberately separate from the pet archive (state.json /
/// settings.json) per PRODUCT R-09 and ARCHITECTURE §7: clearing usage
/// stats never touches pet position/personality/archive, and the toggle is
/// persisted in settings (kept on clear). Own schema version so the stats
/// format can evolve without migrating the pet archive.
/// </para>
/// </summary>
public sealed class UsageStatsArchive
{
    /// <summary>
    /// Schema version. v2 (P6-002) adds <see cref="Buckets"/>. Older v1 files
    /// deserialize with an empty bucket list (additive change, no migration).
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Schema version for forward/backward compatibility.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>UTC timestamp of last save.</summary>
    public string? LastSavedUtc { get; set; }

    /// <summary>
    /// Running count of application-identification failures since the last
    /// clear. These intervals are discarded (not attributed) and counted
    /// here so the coverage limitation is honest (single policy: drop + count,
    /// no separate unknown bucket).
    /// </summary>
    public ulong CoverageMismatchCount { get; set; }

    /// <summary>Original foreground-active segments (raw 7-day retention).</summary>
    public List<UsageStatsSegment> Segments { get; set; } = new();

    /// <summary>Recorded lifecycle events (app-start / unlock / resume).</summary>
    public List<UsageStatsEventRecord> Events { get; set; } = new();

    /// <summary>
    /// Aggregated fixed 30-minute buckets (aggregated 30-day retention, keyed
    /// by counted local date). Built timely from verified intervals; used for
    /// long-lived frequency/duration facts, never fine-grained trajectory.
    /// </summary>
    public List<UsageStatsBucket> Buckets { get; set; } = new();

    /// <summary>
    /// P6-004（第一小步）：上一次主动气泡展示的本地日期（yyyy-MM-dd），用于
    /// “每个本地日期全局最多展示一次”，跨重启同样生效。附加字段，v2 旧文件
    /// 缺失反序列化为 null；清除统计时重置。
    /// </summary>
    public string? BubbleLastShownLocalDate { get; set; }
}