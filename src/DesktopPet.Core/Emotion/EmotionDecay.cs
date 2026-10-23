namespace DesktopPet.Core.Emotion;

/// <summary>
/// Calculates emotion decay toward neutral (50) using half-life formulas.
/// Affinity: 7-day half-life. Grievance: 12-hour half-life.
/// </summary>
public static class EmotionDecay
{
    public const double AffinityHalfLifeDays = 7.0;
    public const double GrievanceHalfLifeHours = 12.0;
    public const double MaxDecayDays = 30.0;

    /// <summary>
    /// Decays affinity toward neutral 50.
    /// Formula: newValue = 50 + (value - 50) * 0.5^(elapsedDays / 7)
    /// </summary>
    /// <param name="currentValue">Current affinity value.</param>
    /// <param name="elapsedDays">Elapsed time in days (clamped to [0, 30]).</param>
    /// <returns>Decayed affinity as int, clamped to [0, 100].</returns>
    public static int DecayAffinity(int currentValue, double elapsedDays)
    {
        return (int)System.Math.Round(DecayAffinityFloat(currentValue, elapsedDays));
    }

    /// <summary>
    /// Decays affinity toward neutral 50, returning a double to preserve precision.
    /// Use this when accumulating small time steps to avoid integer rounding loss.
    /// </summary>
    public static double DecayAffinityFloat(double currentValue, double elapsedDays)
    {
        var clampedDays = ClampElapsed(elapsedDays);
        var decayed = EmotionState.Neutral + (currentValue - EmotionState.Neutral) * System.Math.Pow(0.5, clampedDays / AffinityHalfLifeDays);
        return System.Math.Clamp(decayed, EmotionState.MinValue, EmotionState.MaxValue);
    }

    /// <summary>
    /// Decays grievance toward neutral 50.
    /// Formula: newValue = 50 + (value - 50) * 0.5^(elapsedHours / 12)
    /// </summary>
    /// <param name="currentValue">Current grievance value.</param>
    /// <param name="elapsedHours">Elapsed time in hours (clamped to [0, 720]).</param>
    /// <returns>Decayed grievance as int, clamped to [0, 100].</returns>
    public static int DecayGrievance(int currentValue, double elapsedHours)
    {
        return (int)System.Math.Round(DecayGrievanceFloat(currentValue, elapsedHours));
    }

    /// <summary>
    /// Decays grievance toward neutral 50, returning a double to preserve precision.
    /// Use this when accumulating small time steps to avoid integer rounding loss.
    /// </summary>
    public static double DecayGrievanceFloat(double currentValue, double elapsedHours)
    {
        var clampedHours = ClampElapsed(elapsedHours / 24.0) * 24.0;
        var decayed = EmotionState.Neutral + (currentValue - EmotionState.Neutral) * System.Math.Pow(0.5, clampedHours / GrievanceHalfLifeHours);
        return System.Math.Clamp(decayed, EmotionState.MinValue, EmotionState.MaxValue);
    }

    private static double ClampElapsed(double days)
    {
        if (days < 0) return 0;
        if (days > MaxDecayDays) return MaxDecayDays;
        return days;
    }
}

/// <summary>
/// Manages emotion decay with floating-point internal state to prevent
/// precision loss from per-tick integer rounding (G-01).
/// The decay base value and reference time are preserved as doubles;
/// only the public-facing EmotionState is updated with rounded ints
/// when a snapshot is needed.
/// </summary>
public sealed class EmotionDecayAccumulator
{
    private double _affinityFloat;
    private double _grievanceFloat;
    private DateTime _lastDecayUtc;

    /// <summary>Creates an accumulator from current integer values and a decay reference time.</summary>
    public EmotionDecayAccumulator(int affinity, int grievance, DateTime lastDecayUtc)
    {
        _affinityFloat = affinity;
        _grievanceFloat = grievance;
        _lastDecayUtc = lastDecayUtc;
    }

    /// <summary>
    /// Advances decay to the given UTC time, updating internal float state.
    /// Returns the rounded integer snapshot for EmotionState.
    /// Clock rollback (currentUtc earlier than lastDecayUtc) is treated as zero elapsed.
    /// </summary>
    public (int Affinity, int Grievance) AdvanceTo(DateTime currentUtc)
    {
        var elapsed = currentUtc - _lastDecayUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        var elapsedDays = elapsed.TotalDays;
        var elapsedHours = elapsed.TotalHours;

        _affinityFloat = EmotionDecay.DecayAffinityFloat(_affinityFloat, elapsedDays);
        _grievanceFloat = EmotionDecay.DecayGrievanceFloat(_grievanceFloat, elapsedHours);
        _lastDecayUtc = currentUtc;

        return ((int)System.Math.Round(_affinityFloat), (int)System.Math.Round(_grievanceFloat));
    }

    /// <summary>Current affinity as int (rounded from float state).</summary>
    public int AffinityInt => (int)System.Math.Round(_affinityFloat);

    /// <summary>Current grievance as int (rounded from float state).</summary>
    public int GrievanceInt => (int)System.Math.Round(_grievanceFloat);

    /// <summary>Current affinity as double (full precision).</summary>
    public double AffinityFloat => _affinityFloat;

    /// <summary>Current grievance as double (full precision).</summary>
    public double GrievanceFloat => _grievanceFloat;

    /// <summary>Last decay reference time in UTC.</summary>
    public DateTime LastDecayUtc => _lastDecayUtc;

    /// <summary>
    /// Applies an external increment (e.g. petting +5) to the float state.
    /// Use this instead of modifying EmotionState directly to keep float precision.
    /// </summary>
    public void AddAffinity(int amount)
    {
        _affinityFloat = System.Math.Clamp(_affinityFloat + amount, EmotionState.MinValue, EmotionState.MaxValue);
    }

    /// <summary>
    /// Applies an external increment (e.g. corner +20) to the float state.
    /// </summary>
    public void AddGrievance(int amount)
    {
        _grievanceFloat = System.Math.Clamp(_grievanceFloat + amount, EmotionState.MinValue, EmotionState.MaxValue);
    }

    /// <summary>
    /// Resets grievance to neutral (after expression completes or is interrupted).
    /// </summary>
    public void ResetGrievanceAfterExpression()
    {
        _grievanceFloat = EmotionState.Neutral;
    }
}
