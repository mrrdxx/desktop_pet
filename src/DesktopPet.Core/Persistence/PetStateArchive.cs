namespace DesktopPet.Core.Persistence;

/// <summary>
/// v1 state archive: pet position and basic session metadata.
/// Stored as state.json in %LOCALAPPDATA%\DesktopPet\.
/// </summary>
public sealed class PetStateArchive
{
    public const int CurrentSchemaVersion = 1;

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
