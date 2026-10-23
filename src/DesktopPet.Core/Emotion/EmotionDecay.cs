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
        var clampedDays = ClampElapsed(elapsedDays);
        var decayed = EmotionState.Neutral + (currentValue - EmotionState.Neutral) * System.Math.Pow(0.5, clampedDays / AffinityHalfLifeDays);
        return (int)System.Math.Round(decayed);
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
        var clampedHours = ClampElapsed(elapsedHours / 24.0) * 24.0;
        var decayed = EmotionState.Neutral + (currentValue - EmotionState.Neutral) * System.Math.Pow(0.5, clampedHours / GrievanceHalfLifeHours);
        return (int)System.Math.Round(decayed);
    }

    private static double ClampElapsed(double days)
    {
        if (days < 0) return 0;
        if (days > MaxDecayDays) return MaxDecayDays;
        return days;
    }
}
