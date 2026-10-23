using System.IO;
using DesktopPet.App;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Emotion;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-05: Isolated archive chain tests.
/// Uses a temp directory to actually write and read state.json files,
/// verifying the full load → interact → save → reload chain through
/// ArchiveService and App.MapArchiveToEmotionObjects/MapEmotionObjectsToArchive.
///
/// Coverage:
/// - v2 archive: load → petting +5 → save → reload → verify persisted values.
/// - v1 archive: load → verify default emotion values → save as v2 → reload.
/// - Pending grievance expression persists across save/reload.
/// - LastDecayUtc persists across save/reload.
/// </summary>
[TestClass]
public class IsolatedArchiveChainTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DesktopPetTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>
    /// F-05: Full chain with v2 archive — load, apply petting +5 via the
    /// production mapping, save, reload, and verify persisted affinity.
    /// </summary>
    [STATestMethod]
    public void F05_V2Archive_LoadPetSaveReload_AffinityPersisted()
    {
        // Write a v2 state.json with affinity=50, grievance=50.
        var archive = new ArchiveService(_tempDir);
        var initialState = new PetStateArchive
        {
            SchemaVersion = 2,
            Affinity = 50,
            Grievance = 50,
            LastDecayUtc = DateTime.UtcNow.ToString("O"),
            WindowLeft = 100,
            WindowTop = 100
        };
        archive.SaveState(initialState);

        // Verify file exists.
        Assert.IsTrue(File.Exists(archive.StateFilePath), "state.json must exist after save.");

        // Load from file.
        var (loadedState, fromBackup, unknown) = archive.LoadState();
        Assert.IsFalse(fromBackup, "Should load from primary, not backup.");
        Assert.IsFalse(unknown, "Should not be unknown version.");
        Assert.AreEqual(50, loadedState.Affinity, "Loaded affinity should be 50.");

        // Map to emotion objects (same as App.OnStartup).
        var (acc, expr, tracker) = App.MapArchiveToEmotionObjects(loadedState);
        Assert.AreEqual(50, acc.AffinityInt, "Accumulator affinity should be 50.");

        // Simulate petting: add +5 affinity.
        acc.AddAffinity(5);
        Assert.AreEqual(55, acc.AffinityInt, "After petting, affinity should be 55.");

        // Map back to archive and save.
        App.MapEmotionObjectsToArchive(loadedState, acc, expr);
        archive.SaveState(loadedState);

        // Reload and verify persisted values.
        var (reloadedState, _, _) = archive.LoadState();
        Assert.AreEqual(55, reloadedState.Affinity,
            $"Reloaded affinity must be 55 after save. Got {reloadedState.Affinity}.");
        Assert.AreEqual(2, reloadedState.SchemaVersion, "Schema version must be 2.");
    }

    /// <summary>
    /// F-05: v1 archive (no emotion fields) → load with defaults → save as v2 → reload.
    /// </summary>
    [STATestMethod]
    public void F05_V1Archive_DefaultEmotionValues_UpgradeToV2()
    {
        // Write a v1 state.json (no emotion fields).
        var v1Json = """
        {
            "schemaVersion": 1,
            "windowLeft": 200,
            "windowTop": 200,
            "lastSavedUtc": "2026-10-20T12:00:00Z",
            "isPaused": false
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "state.json"), v1Json);

        var archive = new ArchiveService(_tempDir);
        var (loadedState, _, _) = archive.LoadState();

        // v1 should fall back to default emotion values.
        Assert.AreEqual(1, loadedState.SchemaVersion, "Loaded schema should be v1.");
        Assert.AreEqual(50, loadedState.Affinity, "v1 affinity should default to 50.");
        Assert.AreEqual(50, loadedState.Grievance, "v1 grievance should default to 50.");
        Assert.IsFalse(loadedState.PendingGrievanceExpression, "v1 pending should default to false.");
        Assert.IsNull(loadedState.GrievanceExpressionDate, "v1 expression date should default to null.");

        // v1 → v2: LastDecayUtc falls back to LastSavedUtc.
        Assert.AreEqual("2026-10-20T12:00:00Z", loadedState.LastDecayUtc,
            "v1 LastDecayUtc should fall back to LastSavedUtc.");

        // Map to emotion objects.
        var (acc, expr, _) = App.MapArchiveToEmotionObjects(loadedState);
        Assert.AreEqual(50, acc.AffinityInt, "v1 accumulator affinity should be 50.");
        Assert.IsFalse(expr.IsPending, "v1 expression should not be pending.");

        // Save as v2.
        archive.SaveState(loadedState);

        // Reload — should now be v2 with persisted defaults.
        var (reloadedState, _, _) = archive.LoadState();
        Assert.AreEqual(2, reloadedState.SchemaVersion, "After save, schema should be v2.");
        Assert.AreEqual(50, reloadedState.Affinity, "v2 affinity should still be 50.");
    }

    /// <summary>
    /// F-05: Pending grievance expression persists across save/reload.
    /// </summary>
    [STATestMethod]
    public void F05_PendingGrievanceExpression_PersistsAcrossSaveReload()
    {
        var archive = new ArchiveService(_tempDir);

        // Create state with pending grievance expression.
        var state = new PetStateArchive
        {
            SchemaVersion = 2,
            Affinity = 50,
            Grievance = 85,
            LastDecayUtc = DateTime.UtcNow.ToString("O"),
            PendingGrievanceExpression = true,
            GrievanceExpressionDate = "2026-10-22"
        };
        archive.SaveState(state);

        // Reload.
        var (loadedState, _, _) = archive.LoadState();
        Assert.IsTrue(loadedState.PendingGrievanceExpression, "Pending should be persisted.");
        Assert.AreEqual("2026-10-22", loadedState.GrievanceExpressionDate, "Expression date should be persisted.");

        // Map to emotion objects — should restore pending state.
        var (_, expr, _) = App.MapArchiveToEmotionObjects(loadedState);
        Assert.IsTrue(expr.IsPending, "Expression should be pending after restore.");
        Assert.AreEqual("2026-10-22", expr.MarkedDate, "MarkedDate should be restored.");

        // ShouldExpress with a later date should fire.
        Assert.IsTrue(expr.ShouldExpress("2026-10-23"),
            "Restored expression should fire on the next day.");
    }

    /// <summary>
    /// F-05: LastDecayUtc persists across save/reload, enabling correct
    /// offline decay calculation after restart.
    /// </summary>
    [STATestMethod]
    public void F05_LastDecayUtc_PersistsAcrossSaveReload()
    {
        var archive = new ArchiveService(_tempDir);
        var testTime = new DateTime(2026, 10, 23, 6, 0, 0, DateTimeKind.Utc);

        var state = new PetStateArchive
        {
            SchemaVersion = 2,
            Affinity = 70,
            Grievance = 80,
            LastDecayUtc = testTime.ToString("O")
        };
        archive.SaveState(state);

        // Reload.
        var (loadedState, _, _) = archive.LoadState();
        Assert.AreEqual(testTime.ToString("O"), loadedState.LastDecayUtc,
            "LastDecayUtc should be persisted exactly.");

        // Map to emotion objects — accumulator should use the persisted time.
        var (acc, _, _) = App.MapArchiveToEmotionObjects(loadedState);
        Assert.AreEqual(testTime, acc.LastDecayUtc,
            "Accumulator should restore LastDecayUtc from archive.");

        // Advance decay by 6 hours and verify it works.
        acc.AdvanceTo(testTime + TimeSpan.FromHours(6));
        Assert.IsTrue(acc.GrievanceInt < 80,
            $"Grievance should decay after 6h from archived baseline. Got {acc.GrievanceInt}.");
    }

    /// <summary>
    /// F-05: Full chain — load → corner award +20 → save → reload → verify.
    /// </summary>
    [STATestMethod]
    public void F05_V2Archive_CornerAward_PersistsAcrossSaveReload()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            SchemaVersion = 2,
            Affinity = 50,
            Grievance = 50,
            LastDecayUtc = DateTime.UtcNow.ToString("O")
        };
        archive.SaveState(state);

        var (loadedState, _, _) = archive.LoadState();
        var (acc, expr, _) = App.MapArchiveToEmotionObjects(loadedState);

        // Simulate corner award: +20 grievance.
        acc.AddGrievance(20);
        Assert.AreEqual(70, acc.GrievanceInt, "After corner award, grievance should be 70.");

        // Save and reload.
        App.MapEmotionObjectsToArchive(loadedState, acc, expr);
        archive.SaveState(loadedState);

        var (reloadedState, _, _) = archive.LoadState();
        Assert.AreEqual(70, reloadedState.Grievance,
            $"Reloaded grievance must be 70 after corner award. Got {reloadedState.Grievance}.");
    }
}
