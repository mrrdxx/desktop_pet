namespace DesktopPet.Core.Animation;

/// <summary>Short curiosity lifetime, independent from hover re-arm timing.</summary>
public sealed class CuriosityReaction
{
    public const double DurationMs = 2000;
    public const double LeaveGraceMs = 300;
    private double _endsAt = -1;
    private double _outsideSince = -1;
    private double _pausedAt = -1;
    public bool IsActive => _endsAt >= 0;

    public void Start(double nowMs)
    {
        _endsAt = nowMs + DurationMs;
        _outsideSince = -1;
    }

    public bool ShouldEnd(double nowMs, bool isOverEntity)
    {
        if (!IsActive || _pausedAt >= 0) return false;
        if (isOverEntity) _outsideSince = -1;
        else if (_outsideSince < 0) _outsideSince = nowMs;
        return nowMs >= _endsAt || (_outsideSince >= 0 && nowMs - _outsideSince >= LeaveGraceMs);
    }

    public void Cancel()
    {
        _endsAt = _outsideSince = -1;
    }

    public void Pause(double nowMs)
    {
        if (_pausedAt >= 0) return;
        _pausedAt = nowMs;
        _outsideSince = -1;
    }

    public void Resume(double nowMs)
    {
        if (_pausedAt < 0) return;
        if (_endsAt >= 0) _endsAt += Math.Max(0, nowMs - _pausedAt);
        _outsideSince = -1;
        _pausedAt = -1;
    }
}
