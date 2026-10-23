namespace DesktopPet.Core.Emotion;

/// <summary>
/// Manages bounded affinity and grievance values (0-100, neutral = 50).
/// </summary>
public sealed class EmotionState
{
    public const int Neutral = 50;
    public const int MinValue = 0;
    public const int MaxValue = 100;
    public const int HighAffinityThreshold = 70;
    public const int GrievanceExpressionThreshold = 80;

    private int _affinity = Neutral;
    private int _grievance = Neutral;

    /// <summary>Current affinity (0-100, neutral 50).</summary>
    public int Affinity
    {
        get => _affinity;
        set => _affinity = Clamp(value);
    }

    /// <summary>Current grievance (0-100, neutral 50).</summary>
    public int Grievance
    {
        get => _grievance;
        set => _grievance = Clamp(value);
    }

    /// <summary>True when affinity is at or above the high-affinity threshold.</summary>
    public bool IsHighAffinity => _affinity >= HighAffinityThreshold;

    /// <summary>True when grievance is at or above the expression threshold.</summary>
    public bool IsGrievanceThresholdReached => _grievance >= GrievanceExpressionThreshold;

    /// <summary>Adds a pet increment to affinity, returns the new value.</summary>
    public int AddPetIncrement(int increment = 5)
    {
        Affinity = _affinity + increment;
        return _affinity;
    }

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
