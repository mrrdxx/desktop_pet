namespace DesktopPet.Core.Input;

/// <summary>
/// A single mouse position sample in virtual desktop physical pixels
/// with a monotonic timestamp in milliseconds.
/// </summary>
public readonly record struct MouseSample(double X, double Y, double TimeMs);

/// <summary>
/// Result of sampling.
/// </summary>
/// <param name="SpeedPxPerSec">Net displacement speed over the full window (physical px/s). Represents movement trend; opposite directions cancel out.</param>
/// <param name="EquivalentSpeedPxPerSec">Net displacement speed in equivalent 100%-scale px/s.</param>
/// <param name="RecentSegmentSpeedPxPerSec">Maximum single-segment speed among recent segments (physical px/s). Used for flick/swipe detection: does not cancel on direction reversal and is not diluted by older stationary samples beyond the segment count.</param>
/// <param name="EquivalentRecentSegmentSpeedPxPerSec">Recent segment speed in equivalent 100%-scale px/s. This is the primary signal for R-01 swipe threshold (1200 eq px/s).</param>
/// <param name="Latest">The most recent sample fed to the sampler.</param>
/// <param name="PeakSegmentEnd">The endpoint (position) of the segment that produced <see cref="RecentSegmentSpeedPxPerSec"/>. Used by SwipeDetector to ensure the speed signal and distance signal come from the same segment, preventing old speed peaks from combining with newer close positions.</param>
/// <param name="IsValid">True if there are at least 2 samples with positive time delta.</param>
public readonly record struct MouseSampleResult(
    double SpeedPxPerSec,
    double EquivalentSpeedPxPerSec,
    double RecentSegmentSpeedPxPerSec,
    double EquivalentRecentSegmentSpeedPxPerSec,
    MouseSample Latest,
    MouseSample PeakSegmentEnd,
    bool IsValid)
{
    public static MouseSampleResult Invalid => new(0, 0, 0, 0, default, default, false);
}

/// <summary>
/// Configuration for <see cref="MouseSampler"/>.
/// All distance thresholds are in equivalent 100%-scale pixels.
/// </summary>
public sealed class MouseSamplerConfig
{
    /// <summary>
    /// Maximum number of samples kept in the sliding window.
    /// Default 10 (≈333 ms at 30 Hz).
    /// </summary>
    public int WindowSize { get; set; } = 10;

    /// <summary>
    /// Maximum allowed interval between consecutive samples in milliseconds.
    /// Samples arriving after a gap larger than this are treated as a
    /// sampling interruption: the window is cleared and the new sample
    /// starts a fresh window.
    /// Default 200 ms.
    /// </summary>
    public double MaxSampleGapMs { get; set; } = 200;

    /// <summary>
    /// Maximum allowed displacement between consecutive samples in equivalent
    /// pixels. Displacements exceeding this are treated as teleportation
    /// (e.g. cursor jumped across screens or was repositioned) and the window
    /// is cleared.
    /// Default 3000 equivalent pixels.
    /// </summary>
    public double MaxDisplacementPx { get; set; } = 3000;

    /// <summary>
    /// DPI scale of the monitor the pet is currently on.
    /// Used to convert physical pixels to equivalent 100%-scale pixels.
    /// 1.0 = 100%, 1.5 = 150%, 2.0 = 200%.
    /// </summary>
    public double DpiScale { get; set; } = 1.0;

    /// <summary>
    /// Number of most recent consecutive segments to consider for
    /// RecentSegmentSpeed. A segment is the displacement between two
    /// consecutive samples. Default 3 (≈100 ms at 30 Hz).
    /// 
    /// This is the primary signal for flick/swipe detection per R-01:
    /// it captures short bursts of fast movement without being diluted
    /// by older stationary samples or cancelled by direction reversal.
    /// </summary>
    public int RecentSegmentCount { get; set; } = 3;
}

/// <summary>
/// Sliding-window mouse speed/distance sampler.
/// 
/// Receives raw mouse position samples (physical pixels + monotonic timestamp)
/// and computes speed in equivalent 100%-scale pixels per second.
/// 
/// Design per PRODUCT R-01:
/// - Short sampling window (sliding, configurable size).
/// - Filters sampling interruptions (gaps exceeding MaxSampleGapMs).
/// - Filters teleportation (displacement exceeding MaxDisplacementPx).
/// - Cross-screen DPI scaling: physical pixels are divided by DpiScale to
///   produce equivalent 100%-scale pixels, so the same visual distance
///   yields the same speed regardless of monitor DPI.
/// 
/// This is pure Core logic with no Win32 dependencies. The caller provides
/// samples obtained from the platform layer (e.g. GetCursorPos at 30 Hz).
/// </summary>
public sealed class MouseSampler
{
    private readonly MouseSamplerConfig _config;
    private readonly Queue<MouseSample> _window;
    private MouseSample _lastSample;

    public MouseSampler(MouseSamplerConfig? config = null)
    {
        _config = config ?? new MouseSamplerConfig();
        _window = new Queue<MouseSample>(_config.WindowSize);
        _lastSample = default;
    }

    /// <summary>
    /// Current configuration. Properties can be adjusted at runtime
    /// (e.g. when the pet moves to a monitor with different DPI).
    /// Changing WindowSize does not truncate the current queue; new
    /// samples will respect the new limit.
    /// </summary>
    public MouseSamplerConfig Config => _config;

    /// <summary>
    /// Number of samples currently in the window.
    /// </summary>
    public int WindowCount => _window.Count;

    /// <summary>
    /// The last accepted sample, or default if none yet.
    /// </summary>
    public MouseSample LastSample => _lastSample;

    /// <summary>
    /// Clear all samples and reset the sampler state.
    /// Call when the pet is paused, dragged, or after sleep/resume.
    /// </summary>
    public void Clear()
    {
        _window.Clear();
        _lastSample = default;
    }

    /// <summary>
    /// Feed a new mouse sample and get the current speed result.
    /// </summary>
    /// <param name="sample">Mouse position in physical pixels with monotonic timestamp in ms.</param>
    /// <returns>Speed result. IsValid is false if there are fewer than 2 samples.</returns>
    public MouseSampleResult Sample(MouseSample sample)
    {
        var scale = _config.DpiScale > 0 ? _config.DpiScale : 1.0;

        // First sample ever: just store it.
        if (_window.Count == 0 && _lastSample.Equals(default(MouseSample)))
        {
            EnqueueSample(sample);
            return MouseSampleResult.Invalid;
        }

        // Check for sampling interruption (gap too large).
        var gapMs = sample.TimeMs - _lastSample.TimeMs;
        if (gapMs > _config.MaxSampleGapMs)
        {
            _window.Clear();
            EnqueueSample(sample);
            return MouseSampleResult.Invalid;
        }

        // Check for teleportation (equivalent displacement too large).
        var dxPx = sample.X - _lastSample.X;
        var dyPx = sample.Y - _lastSample.Y;
        var dispEq = Math.Sqrt(dxPx * dxPx + dyPx * dyPx) / scale;
        if (dispEq > _config.MaxDisplacementPx)
        {
            _window.Clear();
            EnqueueSample(sample);
            return MouseSampleResult.Invalid;
        }

        EnqueueSample(sample);

        // Need at least 2 samples to compute speed.
        if (_window.Count < 2)
            return MouseSampleResult.Invalid;

        return ComputeSpeed(scale);
    }

    private void EnqueueSample(MouseSample sample)
    {
        _window.Enqueue(sample);
        _lastSample = sample;

        while (_window.Count > _config.WindowSize)
            _window.Dequeue();
    }

    private MouseSampleResult ComputeSpeed(double scale)
    {
        var samples = _window.ToArray();
        var oldest = samples[0];
        var newest = samples[^1];

        var dtMs = newest.TimeMs - oldest.TimeMs;
        if (dtMs <= 0)
            return new MouseSampleResult(0, 0, 0, 0, newest, newest, false);

        // ── Net displacement speed (movement trend) ──
        var dxPx = newest.X - oldest.X;
        var dyPx = newest.Y - oldest.Y;
        var netDistPx = Math.Sqrt(dxPx * dxPx + dyPx * dyPx);
        var speedPxPerSec = netDistPx / (dtMs / 1000.0);
        var equivSpeedPxPerSec = speedPxPerSec / scale;

        // ── Recent segment speed (flick/swipe signal) ──
        // Take the max single-segment speed among the most recent
        // RecentSegmentCount segments. Each segment is the displacement
        // between two consecutive samples. This does not cancel on
        // direction reversal and is not diluted by older stationary
        // samples beyond the segment count.
        var segCount = Math.Min(_config.RecentSegmentCount, samples.Length - 1);
        segCount = Math.Max(segCount, 1);

        var maxSegSpeedPxPerSec = 0.0;
        var peakSegmentEnd = newest; // default to newest if no segment qualifies
        for (int i = samples.Length - segCount; i < samples.Length; i++)
        {
            if (i < 1) continue;
            var sdx = samples[i].X - samples[i - 1].X;
            var sdy = samples[i].Y - samples[i - 1].Y;
            var segDist = Math.Sqrt(sdx * sdx + sdy * sdy);
            var segDt = samples[i].TimeMs - samples[i - 1].TimeMs;
            if (segDt > 0)
            {
                var segSpeed = segDist / (segDt / 1000.0);
                if (segSpeed > maxSegSpeedPxPerSec)
                {
                    maxSegSpeedPxPerSec = segSpeed;
                    peakSegmentEnd = samples[i];
                }
            }
        }

        var equivMaxSegSpeed = maxSegSpeedPxPerSec / scale;

        return new MouseSampleResult(
            speedPxPerSec,
            equivSpeedPxPerSec,
            maxSegSpeedPxPerSec,
            equivMaxSegSpeed,
            newest,
            peakSegmentEnd,
            true);
    }
}
