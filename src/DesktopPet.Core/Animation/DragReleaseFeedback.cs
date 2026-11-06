namespace DesktopPet.Core.Animation;

/// <summary>Face-only settling after a real drag. Does not own position or input.</summary>
public sealed class DragReleaseFeedback
{
    private readonly ActionDef _sequence;
    private double _startedAt = -1;
    private double _pausedAt = -1;
    public bool IsActive => _startedAt >= 0;

    public DragReleaseFeedback(ActionDef sequence)
    {
        if (sequence.Frames.Count == 0 || sequence.Frames.Any(f => f.DurationMs <= 0))
            throw new ArgumentException("Release feedback requires positive frame durations.", nameof(sequence));
        _sequence = sequence;
    }

    public void Start(double nowMs)
    {
        _startedAt = nowMs;
        // A drag may end while the app is paused: freeze the new expression
        // at its own origin, without counting the earlier paused interval.
        if (_pausedAt >= 0) _pausedAt = nowMs;
    }

    public void Cancel() => _startedAt = -1;

    public FrameDef? Update(double nowMs, bool eligible)
    {
        if (!eligible) Cancel();
        if (!IsActive) return null;
        double elapsed = Math.Max(0, (_pausedAt >= 0 ? _pausedAt : nowMs) - _startedAt);
        foreach (var frame in _sequence.Frames)
        {
            if (elapsed < frame.DurationMs) return frame;
            elapsed -= frame.DurationMs;
        }
        Cancel();
        return null;
    }

    public void Pause(double nowMs)
    {
        if (_pausedAt < 0) _pausedAt = nowMs;
    }

    public void Resume(double nowMs)
    {
        if (_pausedAt < 0) return;
        if (IsActive) _startedAt += Math.Max(0, nowMs - _pausedAt);
        _pausedAt = -1;
    }
}
