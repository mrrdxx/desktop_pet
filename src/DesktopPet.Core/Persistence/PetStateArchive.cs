namespace DesktopPet.Core.Persistence;

/// <summary>
/// v2 state archive: pet position, session metadata, and emotion state.
/// Stored as state.json in %LOCALAPPDATA%\DesktopPet\.
/// v1 files are read with emotion fields falling back to defaults.
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

    /// <summary>Affinity value (0-100, neutral 50). v1 fallback: 50.</summary>
    public int Affinity { get; set; } = 50;

    /// <summary>Grievance value (0-100, neutral 50). v1 fallback: 50.</summary>
    public int Grievance { get; set; } = 50;

    /// <summary>UTC timestamp of last decay calculation (ISO 8601). v1 fallback: LastSavedUtc.</summary>
    public string? LastDecayUtc { get; set; }

    /// <summary>Whether a grievance expression is pending for the next local day. v1 fallback: false.</summary>
    public bool PendingGrievanceExpression { get; set; }

    /// <summary>Local date (yyyy-MM-dd) when pending expression was marked. v1 fallback: null.</summary>
    public string? GrievanceExpressionDate { get; set; }

    // --- v3 peek event fields (R-03) ---

    /// <summary>Local date (yyyy-MM-dd) for the current peek event plan. v1/v2 fallback: null.</summary>
    public string? PeekEventDate { get; set; }

    /// <summary>Candidate trigger point in effective-run minutes (20-120). v1/v2 fallback: 0 (no plan).</summary>
    public int PeekCandidateMinutes { get; set; }

    /// <summary>Whether the candidate point has been reached and probability check was made. v1/v2 fallback: false.</summary>
    public bool PeekHasAttempted { get; set; }

    /// <summary>Whether the peek has been triggered (quota consumed). v1/v2 fallback: false.</summary>
    public bool PeekHasTriggered { get; set; }

    /// <summary>Accumulated effective (non-paused) run time in ms for the current day. v1/v2 fallback: 0.</summary>
    public double PeekEffectiveRunMs { get; set; }
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
