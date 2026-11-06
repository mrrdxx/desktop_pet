namespace DesktopPet.Core.Emotion;

/// <summary>
/// Manages a bounded grievance value (0-100, neutral = 50).
/// </summary>
public sealed class EmotionState
{
    public const int Neutral = 50;
    public const int MinValue = 0;
    public const int MaxValue = 100;
    public const int GrievanceExpressionThreshold = 80;

    private int _grievance = Neutral;

    /// <summary>Current grievance (0-100, neutral 50).</summary>
    public int Grievance
    {
        get => _grievance;
        set => _grievance = Clamp(value);
    }

    /// <summary>True when grievance is at or above the expression threshold.</summary>
    public bool IsGrievanceThresholdReached => _grievance >= GrievanceExpressionThreshold;

    /// <summary>Adds corner grievance after full condition is met.</summary>
    public int AddCornerGrievance(int amount = 20)
    {
        Grievance = _grievance + amount;
        return _grievance;
    }

    /// <summary>Resets grievance to neutral after expression completes or is interrupted.</summary>
    public void ResetGrievanceAfterExpression()
    {
        _grievance = Neutral;
    }

    private static int Clamp(int value)
    {
        return value < MinValue ? MinValue : (value > MaxValue ? MaxValue : value);
    }
}
