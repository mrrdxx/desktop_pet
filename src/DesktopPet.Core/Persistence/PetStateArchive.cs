namespace DesktopPet.Core.Persistence;

/// <summary>
/// v3 state archive: pet position, session metadata, emotion state, and rare-event plan.
/// Stored as state.json in %LOCALAPPDATA%\DesktopPet\.
/// v1 files are read with emotion and rare-event fields falling back to defaults.
/// v2 files are read with rare-event fields falling back to defaults.
/// </summary>
public sealed class PetStateArchive
{
    public const int CurrentSchemaVersion = 3;

    /// <summary>Schema version for forward/backward compatibility.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Window left position in DIP (WPF coordinate space).</summary>
    public double WindowLeft { get; set; } = 100;

    /// <summary>Window top position in DIP (WPF coordinate space).</summary>
    public double WindowTop { get; set; } = 100;

    /// <summary>Monitor device name saved when position was last persisted.
    /// Used to detect if the saved monitor is still connected on restore.</summary>
    public string? SavedDeviceName { get; set; }

    /// <summary>UTC timestamp of last save.</summary>
    public string? LastSavedUtc { get; set; }

    /// <summary>Whether the pet is currently paused (active behavior stopped).</summary>
    public bool IsPaused { get; set; }

    // --- v2 emotion fields ---

    /// <summary>Grievance value (0-100, neutral 50). v1 fallback: 50.</summary>
    public int Grievance { get; set; } = 50;

    /// <summary>UTC timestamp of last decay calculation (ISO 8601). v1 fallback: LastSavedUtc.</summary>
    public string? LastDecayUtc { get; set; }

    /// <summary>Whether a grievance expression is pending for the next local day. v1 fallback: false.</summary>
    public bool PendingGrievanceExpression { get; set; }

    /// <summary>Local date (yyyy-MM-dd) when pending expression was marked. v1 fallback: null.</summary>
    public string? GrievanceExpressionDate { get; set; }

    // --- v3 rare-event fields ---

    /// <summary>
    /// F-05/P1-004: Accumulated effective runtime minutes at last save.
    /// Persisted so that restart restores the session-level total used by
    /// the daily plan's baseline差值评估. v1/v2 fallback: 0.
    /// </summary>
    public double AccumulatedRuntimeMinutes { get; set; }

    /// <summary>
    /// P1-004: Saved daily rare-event plan snapshot (date, delay, roll, consumed, baseline).
    /// Null when no plan exists. v1/v2 fallback: null (no plan restored, fresh plan created on next day).
    /// </summary>
    public RareEventPlanSnapshot? RareEventPlan { get; set; }
}

/// <summary>
/// P1-004: Serializable snapshot of a RareEventPlan for persistence.
/// Mirrors DesktopPet.Core.Events.RareEventPlan fields.
/// </summary>
public sealed class RareEventPlanSnapshot
{
    /// <summary>Local date (yyyy-MM-dd) that this plan applies to.</summary>
    public string? LocalDate { get; set; }

    /// <summary>Effective runtime minutes from day baseline until candidate trigger (20–120).</summary>
    public int TriggerDelayMinutes { get; set; }

    /// <summary>Pre-rolled random value (0.0–1.0) for deterministic restart behavior.</summary>
    public double TriggerRoll { get; set; }

    /// <summary>True when the event has already been triggered or skipped for this day.</summary>
    public bool Consumed { get; set; }

    /// <summary>F-05: Session-level accumulated minutes at plan creation time (daily delay baseline).</summary>
    public double AccumulatedBaselineMinutes { get; set; }
}

/// <summary>
/// v1 settings archive: user preferences.
/// Stored as settings.json in %LOCALAPPDATA%\DesktopPet\.
/// </summary>
public sealed class PetSettingsArchive
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version for forward/backward compatibility.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Animation speed multiplier (1.0 = default).</summary>
    public double AnimationSpeed { get; set; } = 1.0;

    /// <summary>Whether the pet should auto-start with Windows (future use, not implemented in P0-004).</summary>
    public bool AutoStart { get; set; } = false;
}
