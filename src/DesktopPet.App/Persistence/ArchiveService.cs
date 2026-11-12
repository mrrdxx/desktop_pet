using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopPet.Core.Persistence;

namespace DesktopPet.App.Persistence;

/// <summary>
/// Handles loading and saving of settings.json and state.json with safe write
/// (temp file → replace), backup retention, corruption recovery, and schema
/// version checking per ARCHITECTURE §7.
/// </summary>
public sealed class ArchiveService
{
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _directory;
    private bool _stateReadOnly;
    private bool _settingsReadOnly;

    public bool IsStateReadOnly => _stateReadOnly;
    public bool IsSettingsReadOnly => _settingsReadOnly;

    /// <summary>
    /// Creates an ArchiveService rooted at the given directory.
    /// Default: %LOCALAPPDATA%\DesktopPet\
    /// </summary>
    public ArchiveService(string? directory = null)
    {
        _directory = directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet");
    }

    public string StateFilePath => Path.Combine(_directory, "state.json");
    public string StateBackupPath => Path.Combine(_directory, "state.json.bak");
    public string SettingsFilePath => Path.Combine(_directory, "settings.json");
    public string SettingsBackupPath => Path.Combine(_directory, "settings.json.bak");

    // ── State ──────────────────────────────────────────────────────────

    /// <summary>
    /// Load state.json. If corrupted, preserves the bad file, tries backup,
    /// then falls back to defaults. Unknown higher schemaVersion is loaded
    /// read-only (not overwritten on save).
    /// </summary>
    public (PetStateArchive State, bool FromBackup, bool IsUnknownVersion) LoadState()
    {
        var (archive, fromBackup, unknownVersion) = TryLoad<PetStateArchive>(StateFilePath, StateBackupPath);
        var result = archive ?? new PetStateArchive();
        _stateReadOnly = unknownVersion;

        // v3→v4 migration is performed in DeserializeFile when reading
        // pre-v4 JSON. Here we only ensure a default pet exists for
        // fresh archives (no file / new PetStateArchive()).
        if (result.SchemaVersion < 4)
        {
            // Should not normally reach here — DeserializeFile handles
            // pre-v4 migration. But if a default PetStateArchive() was
            // created (no file), ensure it has a default pet.
            result.SchemaVersion = PetStateArchive.CurrentSchemaVersion;
        }

        EnsureDefaultPetList(result);

        return (result, fromBackup, unknownVersion);
    }

    /// <summary>
    /// F-02 R3: Validates the pets array of a known v4 archive.
    /// Returns an error message string if the data is invalid (null entry,
    /// empty/missing PetId, duplicate PetId, unknown Personality value,
    /// or exceeding MaxPets), or null if all entries are valid.
    /// Known v4 invalid data is classified as corrupted so TryLoad can
    /// preserve the original file and attempt backup recovery.
    /// </summary>
    private static string? ValidatePets(PetStateArchive archive)
    {
        if (archive.Pets is null || archive.Pets.Count == 0)
        {
            // Empty/null pets array is handled by EnsureDefaultPetList as
            // a "fresh archive" case (no file or new PetStateArchive).
            // This is not corruption — it's a valid default scenario.
            return null;
        }

        // Max pets enforced by current design (R-05: at most two).
        const int MaxPets = 2;

        var seenIds = new HashSet<string>();

        for (int i = 0; i < archive.Pets.Count; i++)
        {
            var pet = archive.Pets[i];

            if (pet is null)
            {
                return $"pets[{i}] is null";
            }

            // Empty or whitespace PetId is invalid.
            if (string.IsNullOrWhiteSpace(pet.PetId))
            {
                return $"pets[{i}] has empty PetId";
            }

            // Duplicate PetId is invalid — each pet must have a unique stable ID.
            if (!seenIds.Add(pet.PetId))
            {
                return $"duplicate PetId '{pet.PetId}'";
            }

            // Unknown Personality value is invalid.
            if (!Enum.IsDefined(typeof(Personality), pet.Personality))
            {
                return $"pets[{i}] has unknown Personality value {(int)pet.Personality}";
            }
        }

        if (archive.Pets.Count > MaxPets)
        {
            return $"pets count {archive.Pets.Count} exceeds maximum {MaxPets}";
        }

        return null;
    }

    /// <summary>
    /// Ensures the archive has at least one valid (non-null) pet entry.
    /// Called after LoadState for fresh archives (no file / new PetStateArchive)
    /// and for unknown-version archives that bypass validation.
    /// </summary>
    private static void EnsureDefaultPetList(PetStateArchive archive)
    {
        // F-02 R3: Null entries in known v4 files are classified as corrupted
        // by ValidatePets and handled by TryLoad (preserve original, attempt
        // backup). This method only handles the fresh-archive case (no file
        // or new PetStateArchive) where Pets is null or empty.
        if (archive.Pets is null || archive.Pets.Count == 0)
        {
            archive.Pets = new List<PetInstanceArchive> { new PetInstanceArchive() };
        }
    }

    /// <summary>
    /// Save state.json using safe write: write temp → replace original,
    /// preserving the previous file as .bak. Does NOT save if the loaded
    /// archive had an unknown higher schemaVersion.
    /// </summary>
    public void SaveState(PetStateArchive state, bool allowOverwriteUnknownVersion = false)
    {
        if (!allowOverwriteUnknownVersion && (_stateReadOnly
            || state.SchemaVersion > PetStateArchive.CurrentSchemaVersion
            || HasHigherVersion(StateFilePath, PetStateArchive.CurrentSchemaVersion)))
        {
            _stateReadOnly = true;
            // Unknown higher version: do not overwrite with our lower version.
            System.Diagnostics.Trace.TraceWarning("State archive is read-only or newer than this application; skipping save to avoid downgrade.");
            return;
        }

        state.SchemaVersion = PetStateArchive.CurrentSchemaVersion;
        state.LastSavedUtc = DateTime.UtcNow.ToString("O");
        SafeWrite(StateFilePath, StateBackupPath, state);
    }

    // ── Settings ───────────────────────────────────────────────────────

    public (PetSettingsArchive Settings, bool FromBackup, bool IsUnknownVersion) LoadSettings()
    {
        var (archive, fromBackup, unknownVersion) = TryLoad<PetSettingsArchive>(SettingsFilePath, SettingsBackupPath);
        _settingsReadOnly = unknownVersion;
        return (archive ?? new PetSettingsArchive(), fromBackup, unknownVersion);
    }

    public void SaveSettings(PetSettingsArchive settings, bool allowOverwriteUnknownVersion = false)
    {
        if (!allowOverwriteUnknownVersion && (_settingsReadOnly
            || settings.SchemaVersion > PetSettingsArchive.CurrentSchemaVersion
            || HasHigherVersion(SettingsFilePath, PetSettingsArchive.CurrentSchemaVersion)))
        {
            _settingsReadOnly = true;
            System.Diagnostics.Trace.TraceWarning("Settings archive is read-only or newer than this application; skipping save to avoid downgrade.");
            return;
        }

        settings.SchemaVersion = PetSettingsArchive.CurrentSchemaVersion;
        SafeWrite(SettingsFilePath, SettingsBackupPath, settings);
    }

    // ── Core logic ─────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to load and deserialize a JSON archive file.
    /// On corruption: preserves the bad file as .corrupted, tries .bak,
    /// then returns null (caller falls back to defaults).
    /// Returns (null, false, false) if the file doesn't exist.
    /// </summary>
    private static (T? Archive, bool FromBackup, bool IsUnknownVersion) TryLoad<T>(
        string filePath, string backupPath) where T : class
    {
        if (!File.Exists(filePath))
        {
            // No primary file; try backup.
            if (File.Exists(backupPath))
            {
                var (backup, _, unknown) = DeserializeFile<T>(backupPath);
                if (backup is not null)
                    return (backup, true, unknown);
            }
            return (null, false, false);
        }

        var (result, corrupted, unknownVersion) = DeserializeFile<T>(filePath);

        if (corrupted)
        {
            // Preserve the corrupted file for diagnosis.
            var corruptedPath = filePath + ".corrupted";
            try
            {
                if (File.Exists(corruptedPath))
                    File.Delete(corruptedPath);
                File.Move(filePath, corruptedPath);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Trace.TraceWarning("Could not preserve corrupted file: {0}", ex.Message);
            }

            // Try backup.
            if (File.Exists(backupPath))
            {
                var (backup, _, backupUnknown) = DeserializeFile<T>(backupPath);
                if (backup is not null)
                    return (backup, true, backupUnknown);
            }

            return (null, false, false);
        }

        return (result, false, unknownVersion);
    }

    /// <summary>
    /// Deserialize a JSON file. Detects corruption (invalid JSON) and
    /// unknown higher schemaVersion. For state archives with schemaVersion
    /// &lt; 4, performs v3→v4 migration by extracting flat per-pet fields
    /// from the raw JSON into pets[0].
    /// </summary>
    private static (T? Result, bool Corrupted, bool IsUnknownVersion) DeserializeFile<T>(
        string filePath) where T : class
    {
        try
        {
            var json = File.ReadAllText(filePath);

            // Check schemaVersion before deserializing to our type.
            int? fileVersion = TryReadSchemaVersion(json);
            bool unknownVersion = fileVersion is not null
                && fileVersion > GetCurrentSchemaVersion<T>();

            if (unknownVersion)
            {
                // A newer format may legitimately change any v1 field's type.
                // Parse compatible values opportunistically, but never classify
                // a valid future JSON document as corrupt because v1 rejects it.
                T? futureArchive = null;
                try { futureArchive = JsonSerializer.Deserialize<T>(json, s_jsonOpts); }
                catch (JsonException) { }
                if (futureArchive is null)
                {
                    object fallback = typeof(T) == typeof(PetStateArchive)
                        ? (object)new PetStateArchive { SchemaVersion = fileVersion!.Value }
                        : new PetSettingsArchive { SchemaVersion = fileVersion!.Value };
                    futureArchive = (T)fallback;
                }
                return (futureArchive, false, true);
            }

            // v3→v4 migration: v3 JSON has flat per-pet fields (windowLeft,
            // grievance, etc.) that no longer exist on v4 PetStateArchive.
            // Deserialize as v4 (ignoring unknown fields), then extract the
            // old flat fields from raw JSON to populate pets[0].
            if (typeof(T) == typeof(PetStateArchive)
                && fileVersion is not null
                && fileVersion.Value < 4)
            {
                var v4 = JsonSerializer.Deserialize<PetStateArchive>(json, s_jsonOpts);
                if (v4 is not null)
                {
                    v4 = MigrateV3FlatFieldsToV4(json, v4, fileVersion.Value);
                    return ((T)(object)v4, false, false);
                }
            }

            var result = JsonSerializer.Deserialize<T>(json, s_jsonOpts);

            // F-02 R3: For known v4 state archives, validate the pets array
            // structure after deserialization. Null entries, duplicate IDs,
            // unknown personality values, or exceeding the max count are all
            // invalid v4 data — classify as corrupted so TryLoad preserves
            // the original file and attempts backup recovery.
            if (result is PetStateArchive psa && !unknownVersion)
            {
                var petError = ValidatePets(psa);
                if (petError is not null)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "Invalid v4 pet data in {0}: {1}", filePath, petError);
                    return (null, true, false);
                }
            }

            return (result, false, unknownVersion);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Trace.TraceWarning("Corrupted JSON in {0}: {1}", filePath, ex.Message);
            return (null, true, false);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.TraceWarning("Cannot read {0}: {1}", filePath, ex.Message);
            return (null, true, false);
        }
        catch (InvalidOperationException ex)
        {
            // F-01 R1: Type-mismatch during deserialization or migration that
            // was not caught by SafeRead helpers (defensive — current migration
            // uses SafeRead* for all fields). Treat as corrupted so the backup
            // recovery path in TryLoad can attempt the .bak file.
            System.Diagnostics.Trace.TraceWarning("Type error in {0}: {1}", filePath, ex.Message);
            return (null, true, false);
        }
    }

    /// <summary>
    /// Extracts v1/v2/v3 flat per-pet fields from raw JSON and populates
    /// pets[0] on the v4 archive. Global fields (isPaused, accumulatedRuntimeMinutes,
    /// rareEventPlan, lastSavedUtc) are already deserialized onto the v4 root
    /// by JsonSerializer. Per-pet fields are read from JsonNode to avoid
    /// relying on properties that were removed from the v4 DTO.
    /// </summary>
    private static PetStateArchive MigrateV3FlatFieldsToV4(
        string json, PetStateArchive v4, int oldVersion)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch { return EnsureDefaultPet(v4); }

        if (node is null)
            return EnsureDefaultPet(v4);

        var pet = new PetInstanceArchive
        {
            PetId = "bear-1",
            Personality = Personality.Proactive // old archives have no personality; default to Proactive
        };

        // F-01 R1: Read each field with type checking. If any known field has
        // the wrong type, the entire migration fails — DeserializeFile catches
        // the InvalidOperationException and treats the file as corrupted, so
        // the backup/default recovery path in TryLoad can attempt the .bak file.
        // This avoids silently dropping valid data from other fields.
        // Missing optional fields (null JsonNode) are fine — they use defaults.
        pet.WindowLeft = ReadOrThrow<double>(node["windowLeft"], 100, "windowLeft");
        pet.WindowTop = ReadOrThrow<double>(node["windowTop"], 100, "windowTop");
        pet.SavedDeviceName = ReadOrThrow<string?>(node["savedDeviceName"], null, "savedDeviceName");

        // Emotion fields (v2+)
        pet.Grievance = ReadOrThrow<int>(node["grievance"], 50, "grievance");

        // v1→v2 lastDecayUtc fallback: v1 files have no lastDecayUtc field.
        // Per PRODUCT R-02, fall back to lastSavedUtc.
        var lastDecay = ReadOrThrow<string?>(node["lastDecayUtc"], null, "lastDecayUtc");
        if (oldVersion < 2 && string.IsNullOrEmpty(lastDecay))
        {
            lastDecay = ReadOrThrow<string?>(node["lastSavedUtc"], null, "lastSavedUtc");
        }
        pet.LastDecayUtc = lastDecay;

        pet.PendingGrievanceExpression = ReadOrThrow<bool>(node["pendingGrievanceExpression"], false, "pendingGrievanceExpression");
        pet.GrievanceExpressionDate = ReadOrThrow<string?>(node["grievanceExpressionDate"], null, "grievanceExpressionDate");

        v4.Pets = new List<PetInstanceArchive> { pet };
        v4.SchemaVersion = PetStateArchive.CurrentSchemaVersion;
        return v4;
    }

    /// <summary>Ensures the archive has at least one valid pet entry (migration path).</summary>
    private static PetStateArchive EnsureDefaultPet(PetStateArchive v4)
    {
        if (v4.Pets is null || v4.Pets.Count == 0)
            v4.Pets = new List<PetInstanceArchive> { new PetInstanceArchive() };
        return v4;
    }

    // ── F-01 R1: Type-checked reads for v3→v4 migration ──────────────
    // ReadOrThrow reads a JSON node with type checking. If the node is
    // missing (null), returns the fallback. If the node exists but has the
    // wrong type, throws InvalidOperationException so DeserializeFile can
    // classify the file as corrupted and trigger backup/default recovery.
    // This prevents silently dropping valid data from other fields when one
    // field has a type error.

    private static T ReadOrThrow<T>(JsonNode? n, T fallback, string fieldName)
    {
        if (n is null) return fallback;
        try
        {
            return n.GetValue<T>();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"Field '{fieldName}' has wrong type in old archive: expected {typeof(T).Name}", ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Field '{fieldName}' has wrong format in old archive: expected {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// Extract schemaVersion from raw JSON without full deserialization.
    /// </summary>
    private static int? TryReadSchemaVersion(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node?["schemaVersion"]?.GetValue<int>();
        }
        catch
        {
            return null;
        }
    }

    private static int GetCurrentSchemaVersion<T>() where T : class
    {
        return typeof(T) == typeof(PetStateArchive)
            ? PetStateArchive.CurrentSchemaVersion
            : PetSettingsArchive.CurrentSchemaVersion;
    }

    // Also protect callers that save through a fresh service or a replacement
    // v1 DTO, instead of relying exclusively on the loaded object's version.
    private static bool HasHigherVersion(string path, int currentVersion)
        => File.Exists(path) && TryReadSchemaVersion(File.ReadAllText(path)) > currentVersion;

    /// <summary>
    /// Safe write: serialize to a temp file in the same directory, then
    /// replace the original. The previous original is kept as .bak.
    /// Per ARCHITECTURE §7: "先写同目录临时文件，完成序列化与关闭后再替换正式文件".
    /// </summary>
    private void SafeWrite(string filePath, string backupPath, object data)
    {
        Directory.CreateDirectory(_directory);

        var tempPath = filePath + ".tmp";

        // Write to temp file first.
        var json = JsonSerializer.Serialize(data, data.GetType(), s_jsonOpts);
        File.WriteAllText(tempPath, json);

        // Ensure temp file is fully written and closed before replacing.
        // Move previous original to backup, then move temp to original.
        if (File.Exists(filePath))
        {
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(filePath, backupPath);
        }

        File.Move(tempPath, filePath);
    }
}
