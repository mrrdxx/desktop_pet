namespace DesktopPet.Core.Physics;

/// <summary>
/// Result of a release-speed evaluation.
/// </summary>
public readonly record struct ReleaseResult
{
    /// <summary>
    /// Whether the release should launch physics (throw).
    /// False means a gentle release — pet stays where it is.
    /// </summary>
    public required bool ShouldThrow { get; init; }

    /// <summary>
    /// Estimated release velocity in physical pixels per millisecond.
    /// (0, 0) when ShouldThrow is false.
    /// Already clamped to the configured maximum speed.
    /// </summary>
    public required double VelocityX { get; init; }
    public required double VelocityY { get; init; }

    /// <summary>
    /// Raw (pre-clamp) speed magnitude in px/ms, for diagnostics.
    /// </summary>
    public required double RawSpeed { get; init; }

    /// <summary>
    /// P2-004: Diagnostics snapshot from EvaluateRelease, or null if not captured.
    /// Populated only when diagnostics are enabled via <see cref="ReleaseSpeedSampler.CaptureDiagnostics"/>.
    /// </summary>
    public ReleaseDiagInfo? Diag { get; init; }
}

/// <summary>
/// P2-004: Diagnostics snapshot from a release evaluation.
/// Captures the sample buffer state and the decision path at the moment
/// EvaluateRelease was called, so short-drag non-trigger cases can be
/// attributed to input timing vs. product logic without guessing.
/// </summary>
public readonly record struct ReleaseDiagInfo
{
    /// <summary>Reason the evaluation ended at this result.</summary>
    public required string Reason { get; init; }

    /// <summary>Number of samples in the window at evaluation time (after pruning).</summary>
    public required int SampleCount { get; init; }

    /// <summary>Monotonic timestamp of the release event.</summary>
    public required double ReleaseTimeMs { get; init; }

    /// <summary>Timestamp of the newest sample before release pruning.</summary>
    public required double NewestSampleTimeMs { get; init; }

    /// <summary>Gap between newest sample and release (ms). Large gap triggers stationary probe.</summary>
    public required double GapMs { get; init; }

    /// <summary>Whether a stationary probe sample was injected by the caller before evaluation.</summary>
    public required bool StationaryProbeInjected { get; init; }

    /// <summary>Snapshot of samples at evaluation time (X, Y, TimeMs).</summary>
    public required IReadOnlyList<(double X, double Y, double TimeMs)> Samples { get; init; }

    /// <summary>Motion segment start index, or -1 if none found.</summary>
    public required int SegStart { get; init; }

    /// <summary>Motion segment end index, or -1 if none found.</summary>
    public required int SegEnd { get; init; }

    /// <summary>Time span of the motion segment (ms), or 0 if none.</summary>
    public required double SegDt { get; init; }

    /// <summary>Raw speed of the motion segment (px/ms), or 0 if none.</summary>
    public required double SegRawSpeed { get; init; }

    /// <summary>Config ThrowThreshold at evaluation time.</summary>
    public required double ThrowThreshold { get; init; }

    /// <summary>Config StopSpeed at evaluation time.</summary>
    public required double StopSpeed { get; init; }

    /// <summary>Config SampleWindowMs at evaluation time.</summary>
    public required double SampleWindowMs { get; init; }

    /// <summary>Config MinSamplesForThrow at evaluation time.</summary>
    public required int MinSamplesForThrow { get; init; }

    /// <summary>Config MinTimeSpanMs at evaluation time.</summary>
    public required double MinTimeSpanMs { get; init; }
}

/// <summary>
/// P2-004: Diagnostics entry for a single RecordSample call.
/// Records the input parameters and whether the sample was accepted or
/// rejected (and why), so short-drag non-trigger cases can be traced at
/// the input level — not just at EvaluateRelease time.
/// </summary>
public readonly record struct SampleAttempt
{
    /// <summary>Source label: "baseline", "move", "probe", etc.</summary>
    public required string Source { get; init; }

    /// <summary>Input X (physical pixels).</summary>
    public required double X { get; init; }

    /// <summary>Input Y (physical pixels).</summary>
    public required double Y { get; init; }

    /// <summary>Input timestamp (monotonic ms).</summary>
    public required double TimeMs { get; init; }

    /// <summary>Timestamp of the newest existing sample at call time, or 0 if buffer was empty.</summary>
    public required double NewestBeforeTimeMs { get; init; }

    /// <summary>Sample count in buffer before this call.</summary>
    public required int CountBefore { get; init; }

    /// <summary>"accepted" or rejection reason: "rejected_invalid_time", "rejected_invalid_coord", "rejected_same_or_older_time".</summary>
    public required string Result { get; init; }

    /// <summary>Sample count in buffer after this call.</summary>
    public required int CountAfter { get; init; }
}

/// <summary>
/// Configuration for <see cref="ReleaseSpeedSampler"/>.
/// All distance values are in physical pixels; all time values in milliseconds.
/// </summary>
public sealed class ReleaseSpeedConfig
{
    /// <summary>
    /// Time window for recent samples used in velocity estimation.
    /// Samples older than this are discarded before computing velocity.
    /// Default 100 ms — covers ~3 frames at 30 FPS.
    /// </summary>
    public double SampleWindowMs { get; set; } = 100;

    /// <summary>
    /// Minimum speed (px/ms) required to trigger a throw.
    /// Below this the release is a gentle drop (no physics).
    /// Default 0.15 px/ms ≈ 150 px/s at 100% DPI.
    /// </summary>
    public double ThrowThresholdPxPerMs { get; set; } = 0.15;

    /// <summary>
    /// Maximum release speed magnitude (px/ms). The estimated velocity
    /// vector is scaled down if its magnitude exceeds this, preserving
    /// direction. Default 2.0 px/ms ≈ 2000 px/s at 100% DPI.
    /// </summary>
    public double MaxSpeedPxPerMs { get; set; } = 2.0;

    /// <summary>
    /// Minimum number of samples in the window required to estimate
    /// velocity. With fewer samples the release is treated as gentle.
    /// Default 2 (need at least a start and end point).
    /// </summary>
    public int MinSamplesForThrow { get; set; } = 2;

    /// <summary>
    /// If the time span of the recent motion segment is less than this,
    /// the velocity estimate is unreliable and the release is treated
    /// as gentle. Prevents division by near-zero dt. Default 5 ms.
    /// </summary>
    public double MinTimeSpanMs { get; set; } = 5;

    /// <summary>
    /// Speed (px/ms) at or below which a sample pair is considered
    /// stationary (not moving). When the most recent sample pair is
    /// stationary, the sampler reports zero velocity regardless of
    /// earlier faster samples — the pet stopped before release.
    /// Default 0.02 px/ms ≈ 20 px/s at 100% DPI.
    /// </summary>
    public double StopSpeedPxPerMs { get; set; } = 0.02;
}

/// <summary>
/// Pure Core logic for estimating release velocity from a drag trajectory
/// and deciding whether the release is a gentle drop or a throw.
///
/// Design (R-04 / D-016):
/// <list type="bullet">
/// <item>Caller feeds physical-pixel positions and monotonic-ms timestamps
///   during an active drag via <see cref="RecordSample"/>.</item>
/// <item>On release, <see cref="EvaluateRelease"/> estimates velocity from
///   the recent motion segment — the last contiguous run of samples where
///   the pet was actually moving. This reflects the release direction even
///   when the drag reversed course near the end.</item>
/// <item>Gentle release (low speed, stopped, or insufficient data) →
///   ShouldThrow=false, pet stays in place.</item>
/// <item>Throw → velocity is direction-preserving and clamped to
///   <see cref="ReleaseSpeedConfig.MaxSpeedPxPerMs"/>.</item>
/// <item>If the pet stopped moving before release (recent sample pairs are
///   stationary), the old velocity is NOT carried forward — the sampler
///   detects the stop and returns zero velocity.</item>
/// <item>Zero-time-delta and out-of-order samples are rejected without
///   corrupting valid history. Sampling gaps just mean fewer samples.</item>
/// <item>DPI / coordinate-context changes must be signaled via
///   <see cref="ClearSampleContext"/>; the sampler cannot detect them from
///   coordinates alone. After clearing, new samples build a fresh baseline
///   and a coordinate jump never produces a throw.</item>
/// </list>
///
/// This class does NOT own pet position, does NOT call Win32, and does
/// NOT start physics. It only produces a <see cref="ReleaseResult"/>.
/// </summary>
public sealed class ReleaseSpeedSampler
{
    private readonly ReleaseSpeedConfig _config;
    private readonly List<DragSample> _samples = new();

    /// <summary>
    /// A single position sample during drag.
    /// </summary>
    private readonly record struct DragSample(double X, double Y, double TimeMs);

    // P2-004: When true, EvaluateRelease populates ReleaseResult.Diag with a
    // full snapshot for offline analysis. Defaults false — zero overhead in
    // production unless explicitly enabled by the diagnostics host.
    private bool _captureDiagnostics;
    private bool _stationaryProbeInjected;

    // P2-004: Log of recent RecordSample attempts (accepted and rejected),
    // for input-level diagnostics. Cleared on Reset. Capped to prevent
    // unbounded growth in long drags.
    private readonly List<SampleAttempt> _attemptLog = new();
    private const int MaxAttemptLogSize = 64;

    public ReleaseSpeedSampler(ReleaseSpeedConfig? config = null)
    {
        _config = config ?? new ReleaseSpeedConfig();
    }

    /// <summary>
    /// P2-004: Enable or disable diagnostics capture in EvaluateRelease.
    /// When enabled, each EvaluateRelease call populates ReleaseResult.Diag.
    /// </summary>
    public bool CaptureDiagnostics
    {
        get => _captureDiagnostics;
        set => _captureDiagnostics = value;
    }

    /// <summary>
    /// P2-004: Called by the App when a stationary probe sample is injected
    /// before EvaluateRelease, so the diagnostics snapshot can record it.
    /// Resets to false after each EvaluateRelease call.
    /// </summary>
    public void MarkStationaryProbeInjected()
    {
        _stationaryProbeInjected = true;
    }

    /// <summary>
    /// P2-004: Read-only access to the recent RecordSample attempt log.
    /// Each entry records the input (x, y, timeMs), the buffer state before
    /// the call, and whether the sample was accepted or rejected (with
    /// reason). Cleared on Reset. Empty when CaptureDiagnostics is false.
    /// </summary>
    public IReadOnlyList<SampleAttempt> AttemptLog => _attemptLog;

    public ReleaseSpeedConfig Config => _config;

    /// <summary>
    /// Number of samples currently held in the buffer.
    /// </summary>
    public int SampleCount => _samples.Count;

    /// <summary>
    /// Whether any samples have been recorded in the current drag.
    /// </summary>
    public bool HasSamples => _samples.Count > 0;

    /// <summary>
    /// P2-003 F-01: Timestamp of the most recent sample, or 0 when empty.
    /// Lets the caller decide whether a stationary probe at release time
    /// is needed (i.e. the last sample is old enough that motion samples
    /// have been pruned). Read-only — does not expose sample positions.
    /// </summary>
    public double NewestSampleTimeMs => _samples.Count > 0 ? _samples[^1].TimeMs : 0;

    /// <summary>
    /// Record a drag position sample. Call this on each tick while dragging.
    /// </summary>
    /// <param name="x">Foot-center X in physical pixels. Must be finite.</param>
    /// <param name="y">Foot-center Y in physical pixels. Must be finite.</param>
    /// <param name="timeMs">Monotonic timestamp in milliseconds. Must be finite and greater than the last recorded sample's time.</param>
    /// <param name="source">P2-004: Label for diagnostics (e.g. "baseline", "move", "probe"). Default "move".</param>
    public void RecordSample(double x, double y, double timeMs, string source = "move")
    {
        var newestBefore = _samples.Count > 0 ? _samples[^1].TimeMs : 0;
        var countBefore = _samples.Count;

        // F-03: Reject invalid timestamps.
        if (double.IsNaN(timeMs) || double.IsInfinity(timeMs))
        {
            LogAttempt(source, x, y, timeMs, newestBefore, countBefore, "rejected_invalid_time", countBefore);
            return;
        }

        // F-03: Reject invalid coordinates.
        if (double.IsNaN(x) || double.IsInfinity(x) ||
            double.IsNaN(y) || double.IsInfinity(y))
        {
            LogAttempt(source, x, y, timeMs, newestBefore, countBefore, "rejected_invalid_coord", countBefore);
            return;
        }

        // F-03: Compare against the *newest* sample, not the oldest.
        // Reject same-time or out-of-order samples to preserve FIFO monotonic
        // ordering. Do not corrupt existing valid history.
        if (_samples.Count > 0)
        {
            var newest = _samples[^1];
            if (timeMs <= newest.TimeMs)
            {
                LogAttempt(source, x, y, timeMs, newestBefore, countBefore, "rejected_same_or_older_time", countBefore);
                return;
            }
        }

        _samples.Add(new DragSample(x, y, timeMs));
        PruneOldSamples(timeMs);
        LogAttempt(source, x, y, timeMs, newestBefore, countBefore, "accepted", _samples.Count);
    }

    /// <summary>
    /// P2-004: Record a RecordSample attempt in the diagnostics log.
    /// No-op when CaptureDiagnostics is false.
    /// </summary>
    private void LogAttempt(string source, double x, double y, double timeMs,
        double newestBefore, int countBefore, string result, int countAfter)
    {
        if (!_captureDiagnostics)
            return;

        if (_attemptLog.Count >= MaxAttemptLogSize)
            _attemptLog.RemoveAt(0);

        _attemptLog.Add(new SampleAttempt
        {
            Source = source,
            X = x,
            Y = y,
            TimeMs = timeMs,
            NewestBeforeTimeMs = newestBefore,
            CountBefore = countBefore,
            Result = result,
            CountAfter = countAfter,
        });
    }

    /// <summary>
    /// Clear all samples and reset the coordinate context. Call this when
    /// the DPI / monitor context changes (e.g. pet moved to a different
    /// monitor with a different DPI scale), so that old coordinates are
    /// never subtracted from new coordinates.
    ///
    /// F-02: The sampler cannot detect DPI changes from coordinates alone.
    /// The caller (future App integration) must call this when a context
    /// change occurs. After clearing, a fresh baseline is needed before
    /// velocity can be estimated — a single sample after clearing is not
    /// enough to produce a throw.
    /// </summary>
    public void ClearSampleContext()
    {
        _samples.Clear();
    }

    /// <summary>
    /// Evaluate the release and produce a <see cref="ReleaseResult"/>.
    /// Call this when the user releases the mouse button.
    /// Does NOT clear samples — call <see cref="Reset"/> afterwards.
    /// </summary>
    /// <param name="releaseTimeMs">Monotonic timestamp of the release event. Must be finite.</param>
    public ReleaseResult EvaluateRelease(double releaseTimeMs)
    {
        var probeWasInjected = _stationaryProbeInjected;
        _stationaryProbeInjected = false;

        // F-03: Reject invalid release time — return gentle, never produce
        // a velocity from an invalid timestamp.
        if (double.IsNaN(releaseTimeMs) || double.IsInfinity(releaseTimeMs))
        {
            return FinishResult(GentleRelease(), "invalid_release_time", releaseTimeMs,
                0, 0, probeWasInjected, -1, -1, 0, 0);
        }

        var newestBeforePrune = _samples.Count > 0 ? _samples[^1].TimeMs : 0;
        var gapMs = releaseTimeMs - newestBeforePrune;

        // F-03: If release time is before the newest sample, we cannot use
        // future samples. Treat as gentle to avoid computing with data
        // that came after the release moment.
        if (_samples.Count > 0 && releaseTimeMs < _samples[^1].TimeMs)
        {
            return FinishResult(GentleRelease(), "release_before_newest_sample", releaseTimeMs,
                newestBeforePrune, gapMs, probeWasInjected, -1, -1, 0, 0);
        }

        // Prune to the window ending at release time.
        PruneOldSamples(releaseTimeMs);

        if (_samples.Count < _config.MinSamplesForThrow)
        {
            return FinishResult(GentleRelease(), "insufficient_samples", releaseTimeMs,
                newestBeforePrune, gapMs, probeWasInjected, -1, -1, 0, 0);
        }

        // F-01: Find the recent motion segment — the last contiguous run
        // of sample pairs where the pet was moving above stop speed.
        // This reflects the actual release direction, not the net displacement
        // across the entire window (which can be wrong when the drag reversed).
        var (segStart, segEnd) = FindRecentMotionSegment();

        if (segStart < 0)
        {
            return FinishResult(GentleRelease(), "no_motion_segment_stopped", releaseTimeMs,
                newestBeforePrune, gapMs, probeWasInjected, -1, -1, 0, 0);
        }

        var dt = _samples[segEnd].TimeMs - _samples[segStart].TimeMs;
        if (dt < _config.MinTimeSpanMs)
        {
            return FinishResult(GentleRelease(), "segment_dt_below_min", releaseTimeMs,
                newestBeforePrune, gapMs, probeWasInjected, segStart, segEnd, dt, 0);
        }

        var dx = _samples[segEnd].X - _samples[segStart].X;
        var dy = _samples[segEnd].Y - _samples[segStart].Y;
        var rawVx = dx / dt;
        var rawVy = dy / dt;
        var rawSpeed = Math.Sqrt(rawVx * rawVx + rawVy * rawVy);

        // F-01: If the most recent motion is below stop speed, the pet
        // stopped before release — do not carry old velocity forward.
        if (rawSpeed < _config.ThrowThresholdPxPerMs)
        {
            return FinishResult(GentleRelease(rawSpeed), "speed_below_throw_threshold", releaseTimeMs,
                newestBeforePrune, gapMs, probeWasInjected, segStart, segEnd, dt, rawSpeed);
        }

        // Clamp to max speed, preserving direction.
        var clampedVx = rawVx;
        var clampedVy = rawVy;
        if (rawSpeed > _config.MaxSpeedPxPerMs)
        {
            var scale = _config.MaxSpeedPxPerMs / rawSpeed;
            clampedVx = rawVx * scale;
            clampedVy = rawVy * scale;
        }

        var throwResult = new ReleaseResult
        {
            ShouldThrow = true,
            VelocityX = clampedVx,
            VelocityY = clampedVy,
            RawSpeed = rawSpeed,
        };

        return FinishResult(throwResult, "throw", releaseTimeMs,
            newestBeforePrune, gapMs, probeWasInjected, segStart, segEnd, dt, rawSpeed);
    }

    /// <summary>
    /// P2-004: Attach diagnostics to a ReleaseResult if capture is enabled,
    /// then return the result. When capture is disabled, returns the result
    /// unchanged with zero overhead.
    /// </summary>
    private ReleaseResult FinishResult(
        ReleaseResult result, string reason, double releaseTimeMs,
        double newestBeforePrune, double gapMs, bool probeInjected,
        int segStart, int segEnd, double segDt, double segRawSpeed)
    {
        if (!_captureDiagnostics)
            return result;

        var sampleSnapshot = _samples
            .Select(s => (s.X, s.Y, s.TimeMs))
            .ToList();

        var diag = new ReleaseDiagInfo
        {
            Reason = reason,
            SampleCount = _samples.Count,
            ReleaseTimeMs = releaseTimeMs,
            NewestSampleTimeMs = newestBeforePrune,
            GapMs = gapMs,
            StationaryProbeInjected = probeInjected,
            Samples = sampleSnapshot,
            SegStart = segStart,
            SegEnd = segEnd,
            SegDt = segDt,
            SegRawSpeed = segRawSpeed,
            ThrowThreshold = _config.ThrowThresholdPxPerMs,
            StopSpeed = _config.StopSpeedPxPerMs,
            SampleWindowMs = _config.SampleWindowMs,
            MinSamplesForThrow = _config.MinSamplesForThrow,
            MinTimeSpanMs = _config.MinTimeSpanMs,
        };

        return result with { Diag = diag };
    }

    /// <summary>
    /// Clear all samples. Call after the release has been processed
    /// (whether it resulted in a throw or gentle drop) and the drag
    /// session is fully ended.
    /// </summary>
    public void Reset()
    {
        _samples.Clear();
        _stationaryProbeInjected = false;
        _attemptLog.Clear();
    }

    /// <summary>
    /// Find the recent motion segment: scan backward from the newest sample
    /// to find the last contiguous run of sample pairs where per-pair speed
    /// is above the stop threshold. Return the indices (start, end) of that
    /// segment, or (-1, -1) if no motion is found.
    ///
    /// F-01: This ensures the estimated velocity reflects the most recent
    /// direction of actual movement, not the net displacement across the
    /// entire window. If the drag went right then left, the segment captures
    /// the leftward motion. If the pet then stopped, the segment is empty
    /// and the release is gentle.
    /// </summary>
    private (int Start, int End) FindRecentMotionSegment()
    {
        if (_samples.Count < 2)
            return (-1, -1);

        var stopSpeed = _config.StopSpeedPxPerMs;
        var end = _samples.Count - 1;

        // Scan backward from the end. If the last pair is stationary,
        // the pet stopped — no motion segment, return gentle.
        if (!IsPairMoving(end - 1, end, stopSpeed))
            return (-1, -1);

        // F-01: Extend the segment backward only through pairs that are
        // both moving AND in the same direction as the last pair. This
        // ensures the segment captures the most recent directional motion
        // run, not the net displacement across a direction reversal.
        var lastDx = _samples[end].X - _samples[end - 1].X;
        var lastDy = _samples[end].Y - _samples[end - 1].Y;

        var start = end - 1;
        while (start > 0)
        {
            if (!IsPairMoving(start - 1, start, stopSpeed))
                break;

            var dx = _samples[start].X - _samples[start - 1].X;
            var dy = _samples[start].Y - _samples[start - 1].Y;
            if (!SameDirection(dx, dy, lastDx, lastDy))
                break;

            start--;
        }

        return (start, end);
    }

    /// <summary>
    /// Check whether two displacement vectors point in the same general
    /// direction. Two vectors are same-direction if both components have
    /// the same sign (or either is near-zero). This prevents a direction
    /// reversal from being included in the recent motion segment.
    /// </summary>
    private static bool SameDirection(double dx1, double dy1, double dx2, double dy2)
    {
        const double epsilon = 1e-9;

        // X component must not flip sign (allow near-zero on either side).
        if (Math.Abs(dx1) > epsilon && Math.Abs(dx2) > epsilon &&
            Math.Sign(dx1) != Math.Sign(dx2))
            return false;

        // Y component must not flip sign (allow near-zero on either side).
        if (Math.Abs(dy1) > epsilon && Math.Abs(dy2) > epsilon &&
            Math.Sign(dy1) != Math.Sign(dy2))
            return false;

        return true;
    }

    private bool IsPairMoving(int earlier, int later, double stopSpeed)
    {
        var s0 = _samples[earlier];
        var s1 = _samples[later];
        var dt = s1.TimeMs - s0.TimeMs;
        if (dt <= 0)
            return false;
        var dx = s1.X - s0.X;
        var dy = s1.Y - s0.Y;
        var speed = Math.Sqrt(dx * dx + dy * dy) / dt;
        return speed > stopSpeed;
    }

    private void PruneOldSamples(double currentTimeMs)
    {
        var cutoff = currentTimeMs - _config.SampleWindowMs;
        // Remove from front while older than cutoff.
        var removeUpTo = 0;
        while (removeUpTo < _samples.Count && _samples[removeUpTo].TimeMs < cutoff)
            removeUpTo++;
        if (removeUpTo > 0)
            _samples.RemoveRange(0, removeUpTo);
    }

    private static ReleaseResult GentleRelease(double rawSpeed = 0)
    {
        return new ReleaseResult
        {
            ShouldThrow = false,
            VelocityX = 0,
            VelocityY = 0,
            RawSpeed = rawSpeed,
        };
    }
}
