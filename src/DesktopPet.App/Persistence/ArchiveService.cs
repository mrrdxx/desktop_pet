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
        _stateReadOnly = unknownVersion;
        return (archive ?? new PetStateArchive(), fromBackup, unknownVersion);
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
    /// unknown higher schemaVersion.
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

            var result = JsonSerializer.Deserialize<T>(json, s_jsonOpts);
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
