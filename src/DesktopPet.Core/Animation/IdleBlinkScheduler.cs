namespace DesktopPet.Core.Animation;

/// <summary>Small, idle-only expression schedule. Never changes actions or position.</summary>
public sealed class IdleBlinkScheduler
{
    public const double MinIntervalMs = 3000;
    public const double MaxIntervalMs = 7000;
    public const double DoubleBlinkProbability = 0.04;
    public const double DoubleBlinkGapMs = 140;
    private const double MaxUpdateGapMs = 500;
    private readonly ActionDef _blink;
    private readonly Func<double> _random;
    private readonly double _durationMs;
    private double _nextAt = -1;
    private double _startedAt = -1;
    private double _lastUpdate = -1;
    private double _pausedAt = -1;
    private bool _eligible;
    private bool _doubleBlink;
    private FrameDef? _displayed;

    public IdleBlinkScheduler(ActionDef blink, Func<double>? random = null)
    {
        if (blink.Frames.Count == 0 || blink.Frames.Any(f => f.DurationMs <= 0))
            throw new ArgumentException("Blink requires positive frame durations.", nameof(blink));
        _blink = blink;
        _durationMs = blink.Frames.Sum(f => (double)f.DurationMs);
        _random = random ?? Random.Shared.NextDouble;
    }

    public double NextBlinkAtMs => _nextAt;
    public bool IsBlinking => _startedAt >= 0;

    /// <summary>Returns an eye-frame override, or null for the ordinary idle frame.</summary>
    public FrameDef? Update(double nowMs, bool eligible)
    {
        if (!eligible)
        {
            _eligible = false;
            _nextAt = _startedAt = -1;
            _displayed = null;
            return null;
        }
        if (_pausedAt >= 0)
            return _displayed;

        bool stale = _lastUpdate >= 0 && nowMs - _lastUpdate > MaxUpdateGapMs;
        _lastUpdate = nowMs;
        if (!_eligible || stale)
        {
            _eligible = true;
            ScheduleNext(nowMs);
            return null;
        }
        if (_startedAt < 0)
        {
            if (nowMs < _nextAt) return null;
            _startedAt = nowMs;
            _doubleBlink = _random() < DoubleBlinkProbability;
        }

        double elapsed = nowMs - _startedAt;
        if (elapsed >= _durationMs)
        {
            if (!_doubleBlink || elapsed >= 2 * _durationMs + DoubleBlinkGapMs)
            {
                ScheduleNext(nowMs);
                return null;
            }
            elapsed -= _durationMs + DoubleBlinkGapMs;
            if (elapsed < 0) return _displayed = null;
        }
        foreach (var frame in _blink.Frames)
        {
            if (elapsed < frame.DurationMs) return _displayed = frame;
            elapsed -= frame.DurationMs;
        }
        return _displayed = null;
    }

    public void Pause(double nowMs)
    {
        if (_pausedAt < 0) _pausedAt = nowMs;
    }

    public void Resume(double nowMs)
    {
        if (_pausedAt < 0) return;
        double skipped = Math.Max(0, nowMs - _pausedAt);
        if (_nextAt >= 0) _nextAt += skipped;
        if (_startedAt >= 0) _startedAt += skipped;
        _lastUpdate = nowMs;
        _pausedAt = -1;
    }

    private void ScheduleNext(double nowMs)
    {
        _startedAt = -1;
        _displayed = null;
        _nextAt = nowMs + MinIntervalMs + Math.Clamp(_random(), 0, 1) * (MaxIntervalMs - MinIntervalMs);
    }
}
