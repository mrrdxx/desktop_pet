using DesktopPet.Core.Persistence;

namespace DesktopPet.Core.Events;

/// <summary>
/// P6-001: Event kinds for the usage-stats lifecycle observations.
/// The four sources never mix: <see cref="AppStart"/> is the pet itself
/// starting (recorded once per process startup), <see cref="Unlock"/> is a
/// session unlock/resume, <see cref="Resume"/> is a power resume from
/// sleep/hibernate. There is no boot constant because boot is intentionally
/// not recorded (no independent verifiable source with dedup semantics).
/// </summary>
public static class UsageStatsEventTypes
{
    public const string AppStart = "app-start";
    public const string Unlock = "unlock";
    public const string Resume = "resume";
}

/// <summary>
/// P6-001: A pending write unit drained from the collector. Carries the
/// collector's generation so the App-side single-write queue can reject
/// writes that were produced before a clear (generation-mismatch guard —
/// no old async save may resurrect cleared data).
/// </summary>
public sealed class UsageStatsPendingWrite
{
    /// <summary>Collector generation at drain time. Compare with
    /// <see cref="UsageStatsCollector.Generation"/> before persisting.</summary>
    public int Generation { get; set; }

    public List<UsageStatsSegment> Segments { get; set; } = new();
    public List<UsageStatsEventRecord> Events { get; set; } = new();
    public ulong CoverageMismatch { get; set; }
    public bool CoverageMismatchChanged { get; set; }
}

/// <summary>
/// P6-001: Core pure logic for local usage statistics — approximates
/// foreground-active periods and the normalized app base name observing
/// them, with enable/disable and clear entry points.
/// <para>
/// Contract (PRODUCT R-09 / ARCHITECTURE §7 / P6-PLAN R2):
/// <list type="bullet">
/// <item>Disabled by default. No sampling, no updates, no events while
/// disabled; existing (in-retention) data stays and retention cleanup still
/// runs.</item>
/// <item>Foreground sampling: 5 s ticks. A span is only ever committed as a
/// verified interval between two adjacent successful samples of the same
/// trace (same active external app with trustworthy input readings). Input
/// age &gt; 60 s means idle; the unverified tail is dropped and never
/// attributed or back-filled. Sample gaps beyond
/// <see cref="DefaultMaxSampleGapMs"/> (broken trace), invalid readings
/// (null / NaN / Infinity / future), identification failure, an empty app id
/// or suspension all reset the baseline and drop the unverified tail —
/// already-committed segments are never affected.</item>
/// <item>Pause/lock/sleep are independent suspension reasons; while any is
/// active sampling is gated and nothing accumulates. Unlock/resume each
/// clear their own reason (others stay active independently), record their
/// own event and rebuild the baseline. No back-fill across any of these.</item>
/// <item>App id is a normalized base name; same names merge; identification
/// failure drops the interval and counts it once (single policy).</item>
/// <item>Duration is a monotonic difference; the local wall clock only owns
/// the date attribution. Cross-midnight periods are split per local day.</item>
/// <item>Clear bumps <see cref="Generation"/> and wipes segments, events,
/// pending writes and the mismatch counter. Data recorded after a clear is
/// new data, never resurrection. Restart does not restore cleared history.</item>
/// </list>
/// Time sources are injected (<c>Func&lt;double&gt;</c> monotonic ms,
/// <c>Func&lt;DateTimeOffset&gt;</c> local now) so tests use controlled
/// clocks without any App dependency.
/// </para>
/// </summary>
public sealed class UsageStatsCollector
{
    /// <summary>Foreground sampling interval (5 s).</summary>
    public const double DefaultForegroundSampleMs = 5000.0;

    /// <summary>Input age threshold (60 s): within it the user is active.</summary>
    public const double DefaultInputAgeThresholdMs = 60000.0;

    /// <summary>
    /// Maximum accepted gap between samples (15 s = 3 sampling intervals).
    /// Beyond it the trace is treated as broken: close the segment and
    /// rebuild the baseline, never back-fill the gap. Mirrors the P4-001
    /// MaxSampleGapMs idea but for the stats sampler.
    /// </summary>
    public const double DefaultMaxSampleGapMs = 15000.0;

    /// <summary>Raw-record retention: keep only the most recent 7 local days
    /// (today + previous 6 days), keyed by the segment's counted date.</summary>
    public const int RawRetentionDays = 7;

    /// <summary>P6-002: fixed aggregation bucket width in minutes (30).</summary>
    public const int BucketMinutes = 30;

    /// <summary>
    /// P6-002: aggregated bucket retention (30 days), keyed by the bucket's
    /// counted local date — expired by counted date, never extended by the
    /// time the bucket happened to be written.
    /// </summary>
    public const int AggregatedRetentionDays = 30;

    private readonly Func<double> _nowMs;
    private readonly Func<DateTimeOffset> _localNow;
    private readonly double _inputAgeThresholdMs;
    private readonly double _maxSampleGapMs;

    private bool _enabled;

    // --- committed state (restored from / exported to the archive) ---
    private readonly List<UsageStatsSegment> _segments = new();
    private readonly List<UsageStatsEventRecord> _events = new();
    private ulong _coverageMismatch;

    // --- P6-002: committed aggregated buckets keyed by app + bucket start ---
    private readonly Dictionary<string, UsageStatsBucket> _buckets = new();

    // --- P6-002: session tracking (one "use" = one uninterrupted session) ---
    // A session is a maximal chain of adjacent verified intervals for the same
    // app. Any ResetBaseline (gap / idle / suspension / disable / lifecycle
    // event) or an app change closes it, so the next verified interval starts
    // a new session. The session count is booked once, in the bucket holding
    // the session's start.
    private bool _sessionOpen;
    private string? _lastCommittedAppId;

    // --- pending increments (drained to the single write queue) ---
    private readonly List<UsageStatsSegment> _pendingSegments = new();
    private readonly List<UsageStatsEventRecord> _pendingEvents = new();
    private bool _pendingCoverageMismatchChanged;
    private int _generation;

    // --- lifecycle suspension reasons (any active reason suspends sampling) ---
    private bool _pauseSuspended;
    private bool _lockSuspended;
    private bool _sleepSuspended;

    // --- confirmed-interval baseline (adjacent successful ticks) ---
    // The previous successful tick's monotonic time / app id — the only
    // span ever committed is between two adjacent successful samples of the
    // same trace. Anything since the last successful sample is unverified
    // and is dropped on interruption.
    private double? _lastSampleMs;
    private string? _lastSampleAppId;
    private DateTimeOffset _lastSampleLocal;

    // --- F-02 R3: already-timed monotonic upper bound (rollback floor) ---
    // The highest monotonic timestamp this collector has accepted since the
    // last clear. Any tick below it is a clock-rollback reading, even when
    // the baseline itself is empty: accepting it as a "fresh baseline"
    // would let a later tick re-accumulate an already-timed span. The floor
    // survives every ResetBaseline (gap, idle, suspension, disable,
    // lifecycle events — the same time domain keeps going and committed
    // data from before an interruption must never be double-counted) and is
    // only reset by ClearAll (a clear starts a brand-new time domain; new
    // data is a new record). Ticks at/above the floor resume the trace as
    // an ordinary gap rebuild: baseline only, then commit only intervals
    // never verified before.
    private double? _monotonicRollbackFloorMs;

    /// <summary>True while collection is enabled (the user switched it on).</summary>
    public bool IsEnabled => _enabled;

    /// <summary>True while any lifecycle suspension reason (pause / lock /
    /// sleep) is active — sampling is gated and nothing accumulates.</summary>
    public bool IsSuspended => _pauseSuspended || _lockSuspended || _sleepSuspended;

    /// <summary>Increments on every clear; used for the write-guard.</summary>
    public int Generation => _generation;

    /// <summary>Committed segments (in-memory view; same as what the archive holds).</summary>
    public IReadOnlyList<UsageStatsSegment> Segments => _segments;

    /// <summary>Committed events (app-start / unlock / resume).</summary>
    public IReadOnlyList<UsageStatsEventRecord> Events => _events;

    /// <summary>Committed identification-failure count.</summary>
    public ulong CoverageMismatchCount => _coverageMismatch;

    /// <summary>
    /// P6-002: committed aggregated 30-minute buckets, ordered by bucket start
    /// then app id for stable output. Built timely from verified intervals; a
    /// bucket never reconstructs a fine-grained trajectory.
    /// </summary>
    public IReadOnlyList<UsageStatsBucket> Buckets
    {
        get
        {
            var list = _buckets.Values.ToList();
            list.Sort((a, b) =>
            {
                var c = string.CompareOrdinal(a.BucketStartLocal, b.BucketStartLocal);
                return c != 0 ? c : string.CompareOrdinal(a.AppId, b.AppId);
            });
            return list;
        }
    }

    /// <summary>
    /// Creates the collector with injected time sources.
    /// </summary>
    /// <param name="nowMs">Monotonic milliseconds provider (App injects
    /// MonotonicClock.Instance.GetElapsedMs; tests inject a controlled
    /// clock).</param>
    /// <param name="localNow">Local wall-clock provider (App injects
    /// DateTimeOffset.Now; tests inject a fixed date).</param>
    /// <param name="inputAgeThresholdMs">Active-window in ms (60 s default).</param>
    /// <param name="maxSampleGapMs">Broken-trace gap in ms (15 s default).</param>
    public UsageStatsCollector(
        Func<double> nowMs,
        Func<DateTimeOffset> localNow,
        double inputAgeThresholdMs = DefaultInputAgeThresholdMs,
        double maxSampleGapMs = DefaultMaxSampleGapMs)
    {
        _nowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
        _localNow = localNow ?? throw new ArgumentNullException(nameof(localNow));
        _inputAgeThresholdMs = inputAgeThresholdMs;
        _maxSampleGapMs = maxSampleGapMs;
    }

    // ── enable / disable ────────────────────────────────────────────────

    /// <summary>
    /// Enables collection. Existing data is kept; the baseline starts fresh
    /// from the next sample (segments never continue across enable/disable).
    /// F-02 R3: the monotonic rollback floor is intentionally kept — data
    /// verified before a disable stays committed and a clock rollback after
    /// re-enabling must not double-count it (same monotonic time domain).
    /// </summary>
    public void Enable()
    {
        _enabled = true;
    }

    /// <summary>
    /// Disables collection: stops new sampling/updates and drops any
    /// unverified baseline tail (already-committed segments stay). Retention
    /// cleanup is NOT run here — the caller runs it explicitly
    /// (<see cref="PruneToRetention"/>) so cleanup also happens while
    /// disabled. F-02 R3: the monotonic rollback floor survives (same time
    /// domain; committed data stays, so it must not be re-countable).
    /// </summary>
    public void Disable()
    {
        _enabled = false;
        ResetBaseline(); // any unverified tail is dropped; committed data stays
    }

    // ── lifecycle events ────────────────────────────────────────────────

    /// <summary>
    /// Registers the app-start event (pet itself started). Recorded once per
    /// process startup by the App layer. No foreground attribution is made.
    /// </summary>
    public void OnAppStart()
    {
        if (!_enabled) return;
        ResetBaseline();
        RecordEvent(UsageStatsEventTypes.AppStart);
    }

    /// <summary>
    /// Suspends sampling for the global-pause reason. Setting it to true
    /// (or clearing the last remaining reason) drops any unverified tail —
    /// a suspended interval is never attributed or back-filled. No event is
    /// recorded here (the corresponding unlock/resume events are).
    /// </summary>
    public void SetPaused(bool paused) => SetSuspensionReason(ref _pauseSuspended, paused);

    /// <summary>
    /// Suspends sampling for the session-lock reason (lock = true,
    /// unlock = false). Independent from pause/sleep: clearing one reason
    /// does not resume while another reason remains active.
    /// </summary>
    public void SetLocked(bool locked) => SetSuspensionReason(ref _lockSuspended, locked);

    /// <summary>
    /// Suspends sampling for the power-sleep reason (suspend = true,
    /// resume = false). Independent from pause/lock: clearing one reason
    /// does not resume while another reason remains active.
    /// </summary>
    public void SetSleeping(bool sleeping) => SetSuspensionReason(ref _sleepSuspended, sleeping);

    private void SetSuspensionReason(ref bool flag, bool value)
    {
        var wasSuspended = IsSuspended;
        flag = value;
        if (wasSuspended && IsSuspended)
            return; // still suspended by another reason — keep everything
        ResetBaseline(); // entering suspension, or fully released — drop tail
    }

    /// <summary>
    /// Unlock / session resume: clears the lock reason (other reasons such
    /// as pause/sleep stay active independently), records the unlock event
    /// and rebuilds the baseline. Nothing is back-filled.
    /// </summary>
    public void OnUnlock()
    {
        SetLocked(false);
        if (_enabled)
        {
            RecordEvent(UsageStatsEventTypes.Unlock);
        }
    }

    /// <summary>
    /// Power resume / session resume from sleep: clears the sleep reason
    /// (pause/lock stay active independently), records its own event kind
    /// and rebuilds the baseline.
    /// </summary>
    public void OnResume()
    {
        SetSleeping(false);
        if (_enabled)
        {
            RecordEvent(UsageStatsEventTypes.Resume);
        }
    }

    // ── foreground sampling ─────────────────────────────────────────────

    /// <summary>
    /// One foreground sampling tick (every 5 s in production).
    /// </summary>
    /// <param name="appId">Normalized executable base name of the foreground
    /// external application, or null when there is no relevant external app
    /// (foreground is the pet itself or the desktop).</param>
    /// <param name="lastInputMs">Monotonic timestamp of the last user input,
    /// or null when unknown. When the input age exceeds the threshold the
    /// tick is treated as idle: the current segment is closed and no idle
    /// interval is attributed.</param>
    /// <param name="resolutionFailed">
    /// True when the foreground app could not be identified (name lookup
    /// failed). Single policy: drop the interval and count one coverage
    /// mismatch. Pass both null <paramref name="appId"/> and false here for
    /// "no external app by design" (no mismatch counted).
    /// </param>
    public void SampleTick(string? appId, double? lastInputMs, bool resolutionFailed = false)
    {
        if (!_enabled) return;
        if (IsSuspended) return; // F-01: suspended ticks never sample/accumulate

        var nowMs = _nowMs();

        // F-02 R3: monotonic rollback — a clock going backwards is a broken
        // trace. The rollback floor is the highest already-timed bound and
        // survives baseline resets: a tick below it is rejected even while
        // the baseline is empty, so a "fresh baseline" can never re-attribute
        // an already-timed span. Repeated ticks below the floor stay
        // rejected; a tick at/above it resumes the trace as a normal gap
        // rebuild (baseline only, never back-fill, never double-count).
        if (_monotonicRollbackFloorMs.HasValue && nowMs < _monotonicRollbackFloorMs.Value)
        {
            ResetBaseline(); // drops the unverified tail; the floor is kept
            return;
        }

        // Broken-trace detection: a gap beyond the threshold means we lost
        // the trace. The unverified span since the last successful sample is
        // discarded (never back-filled); the baseline rebuilds from scratch.
        if (_lastSampleMs.HasValue && nowMs - _lastSampleMs.Value > _maxSampleGapMs)
        {
            ResetBaseline();
        }

        // Input validity: null / NaN / Infinity / a timestamp in the future
        // are not trustworthy readings. Treat them like a missing sample:
        // drop the unverified tail and rebuild the baseline.
        if (!IsValidInput(lastInputMs, nowMs))
        {
            ResetBaseline();
            return;
        }

        // Single policy interruptions: identification failure, input idle,
        // or no relevant external app — the interval since the last verified
        // sample is not attributable. Verified history stays; the tail is
        // dropped and the baseline rebuilds.
        if (resolutionFailed)
        {
            ResetBaseline();
            _coverageMismatch++;
            _pendingCoverageMismatchChanged = true;
            return;
        }
        if (lastInputMs.HasValue && nowMs - lastInputMs.Value > _inputAgeThresholdMs)
        {
            ResetBaseline();
            return;
        }
        if (string.IsNullOrWhiteSpace(appId))
        {
            ResetBaseline();
            return;
        }

        // Confirmed tick: this sample is valid and active for appId, so the
        // interval [lastSampleMs, nowMs) is now a verified usage span of the
        // previously observed app (never the unverified tail).
        if (_lastSampleMs.HasValue && _lastSampleAppId is not null)
        {
            CommitVerifiedInterval(_lastSampleAppId, _lastSampleMs.Value, nowMs, _lastSampleLocal);
        }
        else
        {
            // First verified sample after a gap: baseline only, no span yet.
            // Committing now would fabricate an unconfirmed duration.
        }

        _lastSampleMs = nowMs;
        _lastSampleAppId = appId;
        _lastSampleLocal = _localNow();
        // F-02 R3: an accepted sample extends the already-timed monotonic
        // upper bound (monotonic: the rollback branch above already rejected
        // anything below the current floor, so this only ever raises it).
        _monotonicRollbackFloorMs = Math.Max(_monotonicRollbackFloorMs ?? nowMs, nowMs);
    }

    // ── drain / restore / clear / retention ─────────────────────────────

    /// <summary>
    /// Drains and clears the pending write unit. Returns null when there is
    /// nothing to persist. The returned unit carries the generation at drain
    /// time; the App write queue must drop the unit if the generation does
    /// not equal <see cref="Generation"/> when persisting (clear guard).
    /// </summary>
    public UsageStatsPendingWrite? DrainPending()
    {
        if (_pendingSegments.Count == 0 && _pendingEvents.Count == 0 && !_pendingCoverageMismatchChanged)
            return null;

        var write = new UsageStatsPendingWrite
        {
            Generation = _generation,
            Segments = _pendingSegments.ToList(),
            Events = _pendingEvents.ToList(),
            CoverageMismatch = _coverageMismatch,
            CoverageMismatchChanged = _pendingCoverageMismatchChanged
        };

        _pendingSegments.Clear();
        _pendingEvents.Clear();
        _pendingCoverageMismatchChanged = false;
        return write;
    }

    /// <summary>
    /// Restores already-committed data (load at startup). Restored data is
    /// committed, never pending — it is not written back on the next drain.
    /// This is the "restart keeps what was saved; cleared data is an empty
    /// archive" contract.
    /// </summary>
    public void Restore(UsageStatsArchive archive)
    {
        if (archive is null) return;
        _segments.AddRange(archive.Segments ?? new List<UsageStatsSegment>());
        _events.AddRange(archive.Events ?? new List<UsageStatsEventRecord>());
        _coverageMismatch = archive.CoverageMismatchCount;
        if (archive.Buckets is not null)
        {
            foreach (var bucket in archive.Buckets)
            {
                if (bucket is null || string.IsNullOrWhiteSpace(bucket.AppId))
                    continue;
                _buckets[BucketKey(bucket.AppId, bucket.BucketStartLocal)] = bucket;
            }
        }
        // Generation is intentionally NOT restored: a process restart cannot
        // undo a clear's generation bump that was persisted after the clear.
    }

    /// <summary>
    /// Clears all statistics: segments, events, pending writes and the
    /// mismatch counter. Bumps <see cref="Generation"/> so any in-flight
    /// write from before the clear is rejected (no resurrection). The
    /// enable flag is a settings-owned value and is NOT part of this call —
    /// the caller keeps the toggle per "clear keeps switch and config".
    /// </summary>
    public void ClearAll()
    {
        _generation++;
        ResetBaseline();
        _monotonicRollbackFloorMs = null; // F-02 R3: new time domain after a clear
        _segments.Clear();
        _events.Clear();
        _buckets.Clear();
        _sessionOpen = false;
        _lastCommittedAppId = null;
        _coverageMismatch = 0;
        _pendingSegments.Clear();
        _pendingEvents.Clear();
        _pendingCoverageMismatchChanged = false;
    }

    /// <summary>
    /// Prunes committed segments/events outside the raw retention window,
    /// keyed by the counted local date (today + previous
    /// <see cref="RawRetentionDays"/>-1 days are kept). Also prunes pending
    /// duplicates so nothing already expired can be persisted. Runs on every
    /// persist and is still executed while collection is disabled.
    /// Returns the number of committed records removed (segments + events).
    /// </summary>
    public int PruneToRetention()
    {
        var today = DateOnly.FromDateTime(_localNow().Date);
        var rawCutoff = today.AddDays(-(RawRetentionDays - 1)); // oldest kept raw date
        var aggregatedCutoff = today.AddDays(-(AggregatedRetentionDays - 1)); // oldest kept bucket date
        int removed = 0;
        removed += RemoveOutdated(_segments, _pendingSegments, rawCutoff);
        removed += RemoveOutdated(_events, _pendingEvents, rawCutoff);
        removed += PruneBuckets(aggregatedCutoff);
        return removed;
    }

    /// <summary>
    /// P6-002: removes aggregated buckets whose counted date is before the
    /// aggregated retention cutoff. Runs on every persist, including while
    /// collection is disabled — expiration is by counted date, not by when the
    /// bucket was generated, and does not wait for raw records to age out.
    /// </summary>
    private int PruneBuckets(DateOnly cutoff)
    {
        var expired = new List<string>();
        foreach (var kv in _buckets)
        {
            if (DateOnly.TryParse(kv.Value.Date, out var date) && date < cutoff)
                expired.Add(kv.Key);
        }
        foreach (var key in expired)
            _buckets.Remove(key);
        return expired.Count;
    }

    private static int RemoveOutdated<T>(
        List<T> committed, List<T> pending, DateOnly cutoff) where T : class
    {
        string DateOf(T item) => item switch
        {
            UsageStatsSegment s => s.Date,
            UsageStatsEventRecord e => e.Date,
            _ => ""
        };

        int removed = 0;
        // Don't use RemoveAll with closures over 'committed' being cleared
        // while we count — standard filter loop keeps it simple.
        for (int i = committed.Count - 1; i >= 0; i--)
        {
            if (DateOnly.TryParse(DateOf(committed[i]), out var d) && d < cutoff)
            {
                committed.RemoveAt(i);
                removed++;
            }
        }
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (DateOnly.TryParse(DateOf(pending[i]), out var d) && d < cutoff)
                pending.RemoveAt(i);
        }
        return removed;
    }

    // ── internals ───────────────────────────────────────────────────────

    /// <summary>Drops any unverified tail and clears the confirmed-sample
    /// baseline. Committed segments already saved are never affected.
    /// F-02 R3: the monotonic rollback floor is intentionally NOT cleared
    /// here — it is the already-timed upper bound and must survive every
    /// interruption (gap, idle, suspension, disable, lifecycle events) so a
    /// later clock rollback below it can never rebuild a baseline that
    /// re-accumulates an already-timed span. Only <see cref="ClearAll"/>
    /// resets the floor (a clear starts a new time domain).</summary>
    private void ResetBaseline()
    {
        _lastSampleMs = null;
        _lastSampleAppId = null;
        // P6-002: any interruption (gap / idle / suspension / disable /
        // lifecycle event) closes the current session, so the next verified
        // interval starts a new "use".
        _sessionOpen = false;
    }

    /// <summary>Checks that a last-input reading is trustworthy: not null,
    /// not NaN/Infinity, not in the future (a future timestamp is an
    /// unreliable reading — the input cannot post-date the current sample).</summary>
    private static bool IsValidInput(double? lastInputMs, double nowMs)
    {
        if (!lastInputMs.HasValue) return false;
        var v = lastInputMs.Value;
        if (double.IsNaN(v) || double.IsInfinity(v)) return false;
        return v <= nowMs;
    }

    /// <summary>Commits the verified interval [startMs, endMs) attributed to
    /// <paramref name="appId"/>, splitting across local midnights so each
    /// piece is dated to the local day of its start. Duration is a monotonic
    /// difference; the local clock only anchors the start date (approximation
    /// for the time until midnight).</summary>
    private void CommitVerifiedInterval(
        string appId, double startMs, double endMs, DateTimeOffset startLocal)
    {
        if (string.IsNullOrWhiteSpace(appId) || endMs <= startMs)
            return;

        // P6-002: decide once, before the split, whether this verified
        // interval starts a new session for the app. The session count is
        // booked in the bucket that holds the session's start (the first
        // day/bucket piece only), never duplicated across the split pieces.
        var startsNewSession = BeginOrContinueSession(appId);
        var firstPiece = true;

        var cursorMs = startMs;
        var cursorLocal = startLocal;
        while (true)
        {
            var midnight = new DateTimeOffset(
                cursorLocal.Date.AddDays(1), cursorLocal.Offset);
            var tillMidnightMs = (midnight - cursorLocal).TotalMilliseconds;
            var partEndMs = cursorMs + tillMidnightMs;
            if (partEndMs >= endMs)
            {
                AddSegment(appId, cursorLocal, endMs - cursorMs, startsNewSession && firstPiece);
                return;
            }
            if (tillMidnightMs > 0.0)
            {
                AddSegment(appId, cursorLocal, tillMidnightMs, startsNewSession && firstPiece);
                firstPiece = false;
            }
            cursorMs = partEndMs;
            cursorLocal = midnight;
        }
    }

    /// <summary>
    /// P6-002: opens a new session for <paramref name="appId"/> when none is
    /// open or the app changed; otherwise the committed interval continues the
    /// current session. Returns true only when a new session started (book its
    /// count once).
    /// </summary>
    private bool BeginOrContinueSession(string appId)
    {
        if (_sessionOpen && _lastCommittedAppId == appId)
            return false; // same uninterrupted session continues
        _sessionOpen = true;
        _lastCommittedAppId = appId;
        return true;
    }

    private void AddSegment(
        string appId, DateTimeOffset startLocal, double durationMs, bool countSessionStart)
    {
        if (string.IsNullOrWhiteSpace(appId) || durationMs <= 0.0)
            return;
        var segment = new UsageStatsSegment
        {
            AppId = appId,
            Date = startLocal.ToString("yyyy-MM-dd"),
            StartLocal = startLocal.ToString("O"),
            DurationMs = Math.Round(durationMs, 3)
        };
        _segments.Add(segment);
        _pendingSegments.Add(segment);

        // P6-002: timely incremental aggregation — split the same verified
        // piece into fixed 30-minute buckets immediately (this piece is
        // already within a single local day from the midnight split).
        AddBuckets(appId, startLocal, durationMs, countSessionStart);
    }

    /// <summary>
    /// P6-002: splits one verified piece (within a single local day) into
    /// fixed 30-minute buckets and accumulates duration; books the session
    /// count on the first bucket only.
    /// </summary>
    private void AddBuckets(
        string appId, DateTimeOffset startLocal, double durationMs, bool countSessionStart)
    {
        var cursorLocal = startLocal;
        var remaining = durationMs;
        var firstBucket = true;
        while (remaining > 0.0)
        {
            var bucketStart = FloorToBucket(cursorLocal);
            var nextBucket = bucketStart.AddMinutes(BucketMinutes);
            var tillNextMs = (nextBucket - cursorLocal).TotalMilliseconds;
            if (tillNextMs <= 0.0)
                tillNextMs = remaining; // safety: degenerate split takes the rest
            var pieceMs = Math.Min(remaining, tillNextMs);

            var key = BucketKey(appId, bucketStart.ToString("O"));
            if (!_buckets.TryGetValue(key, out var bucket))
            {
                bucket = new UsageStatsBucket
                {
                    AppId = appId,
                    Date = bucketStart.ToString("yyyy-MM-dd"),
                    BucketStartLocal = bucketStart.ToString("O")
                };
                _buckets[key] = bucket;
            }
            bucket.ActiveMs += pieceMs;
            if (firstBucket && countSessionStart)
                bucket.SessionCount++;

            cursorLocal = cursorLocal.AddMilliseconds(pieceMs);
            remaining -= pieceMs;
            firstBucket = false;
        }
    }

    /// <summary>Floors a local timestamp to the fixed 30-minute bucket grid.</summary>
    private static DateTimeOffset FloorToBucket(DateTimeOffset local)
    {
        var minute = local.Minute < BucketMinutes ? 0 : BucketMinutes;
        return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, minute, 0, local.Offset);
    }

    private static string BucketKey(string appId, string bucketStartLocal) =>
        appId + "\u0001" + bucketStartLocal;

    private void RecordEvent(string eventType)
    {
        var local = _localNow();
        var record = new UsageStatsEventRecord
        {
            EventType = eventType,
            Date = local.ToString("yyyy-MM-dd"),
            OccurredLocal = local.ToString("O")
        };
        _events.Add(record);
        _pendingEvents.Add(record);
    }
}

    