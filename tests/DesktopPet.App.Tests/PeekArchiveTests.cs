using System.IO;
using System.Text.Json;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P1-004: Peek event archive v3 read/write tests (R-03).
/// Verifies that peek scheduler state persists correctly through
/// the save/load cycle, and that v1/v2 files fall back to default
/// peek values when loaded by the v3 ArchiveService.
/// </summary>
[TestClass]
public class PeekArchiveTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-PeekTest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── v3 field persistence round-trip ───────────────────────────────

    /// <summary>Save and reload a state with peek fields set — all fields must survive the round-trip.</summary>
    [TestMethod]
    public void PeekFields_SaveAndReload_PreservesAllValues()
    {
        var archive = new ArchiveService(_tempDir);
        var state = new PetStateArchive
        {
            PeekEventDate = "2026-10-24",
            PeekCandidateMinutes = 45,
            PeekHasAttempted = true,
            PeekHasTriggered = false,
            PeekEffectiveRunMs = 1234567.5
        };

        archive.SaveState(state);
        var (loaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(3, loaded.SchemaVersion);
        Assert.AreEqual("2026-10-24", loaded.PeekEventDate);
        Assert.AreEqual(45, loaded.PeekCandidateMinutes);
        Assert.IsTrue(loaded.PeekHasAttempted);
        Assert.IsFalse(loaded.PeekHasTriggered);
        Assert.AreEqual(1234567.5, loaded.PeekEffectiveRunMs, 0.001);
    }

    /// <summary>Default state (no peek fields set) saves with correct v3 defaults.</summary>
    [TestMethod]
    public void PeekFields_DefaultState_SavesWithV3Defaults()
    {
        var archive = new ArchiveService(_tempDir);
        var state = new PetStateArchive();

        archive.SaveState(state);
        var (loaded, _, _) = archive.LoadState();

        Assert.AreEqual(3, loaded.SchemaVersion);
        Assert.IsNull(loaded.PeekEventDate);
        Assert.AreEqual(0, loaded.PeekCandidateMinutes);
        Assert.IsFalse(loaded.PeekHasAttempted);
        Assert.IsFalse(loaded.PeekHasTriggered);
        Assert.AreEqual(0.0, loaded.PeekEffectiveRunMs, 0.001);
    }

    // ── v2 file compatibility ─────────────────────────────────────────

    /// <summary>A v2 JSON file (no peek fields) loads with peek defaults: null/0/false.</summary>
    [TestMethod]
    public void PeekFields_V2File_LoadsWithPeekDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        // Write a v2 state.json manually — has emotion fields but no peek fields.
        var v2Json = """
        {
            "schemaVersion": 2,
            "windowLeft": 100,
            "windowTop": 200,
            "savedDeviceName": null,
            "lastSavedUtc": "2026-10-22T12:00:00.0000000Z",
            "isPaused": false,
            "affinity": 70,
            "grievance": 55,
            "lastDecayUtc": "2026-10-22T12:00:00.0000000Z",
            "pendingGrievanceExpression": false,
            "grievanceExpressionDate": null
        }
        """;
        File.WriteAllText(archive.StateFilePath, v2Json);

        var (loaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown, "v2 file should not be treated as unknown version.");
        Assert.AreEqual(2, loaded.SchemaVersion, "v2 file should retain schemaVersion 2 on load.");
        Assert.AreEqual(70, loaded.Affinity, "v2 emotion fields should still load.");
        // Peek fields must fall back to defaults.
        Assert.IsNull(loaded.PeekEventDate, "v2 file should have null peekEventDate.");
        Assert.AreEqual(0, loaded.PeekCandidateMinutes, "v2 file should have 0 peekCandidateMinutes.");
        Assert.IsFalse(loaded.PeekHasAttempted, "v2 file should have false peekHasAttempted.");
        Assert.IsFalse(loaded.PeekHasTriggered, "v2 file should have false peekHasTriggered.");
        Assert.AreEqual(0.0, loaded.PeekEffectiveRunMs, 0.001, "v2 file should have 0 peekEffectiveRunMs.");
    }

    // ── v1 file compatibility ─────────────────────────────────────────

    /// <summary>A v1 JSON file (no emotion or peek fields) loads with peek defaults.</summary>
    [TestMethod]
    public void PeekFields_V1File_LoadsWithPeekDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        var v1Json = """
        {
            "schemaVersion": 1,
            "windowLeft": 100,
            "windowTop": 200,
            "savedDeviceName": null,
            "lastSavedUtc": "2026-10-20T10:00:00.0000000Z",
            "isPaused": false
        }
        """;
        File.WriteAllText(archive.StateFilePath, v1Json);

        var (loaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(1, loaded.SchemaVersion, "v1 file should retain schemaVersion 1 on load.");
        Assert.AreEqual(50, loaded.Affinity, "v1 file should fall back to default affinity 50.");
        // Peek fields must also fall back to defaults.
        Assert.IsNull(loaded.PeekEventDate);
        Assert.AreEqual(0, loaded.PeekCandidateMinutes);
        Assert.IsFalse(loaded.PeekHasAttempted);
        Assert.IsFalse(loaded.PeekHasTriggered);
        Assert.AreEqual(0.0, loaded.PeekEffectiveRunMs, 0.001);
    }

    // ── Production mapping round-trip ─────────────────────────────────

    /// <summary>MapArchiveToEmotionObjects restores PeekScheduler state from archive.</summary>
    [TestMethod]
    public void MapArchiveToEmotionObjects_RestoresPeekScheduler()
    {
        var state = new PetStateArchive
        {
            PeekEventDate = "2026-10-24",
            PeekCandidateMinutes = 60,
            PeekHasAttempted = true,
            PeekHasTriggered = false,
            PeekEffectiveRunMs = 50000.0
        };

        var (_, _, _, peek) = App.MapArchiveToEmotionObjects(state);

        Assert.AreEqual("2026-10-24", peek.EventDate);
        Assert.AreEqual(60, peek.CandidateMinutes);
        Assert.IsTrue(peek.HasAttempted);
        Assert.IsFalse(peek.HasTriggered);
        Assert.AreEqual(50000.0, peek.EffectiveRunMs, 0.001);
        Assert.IsFalse(peek.IsPeeking, "IsPeeking should never be restored as true.");
    }

    /// <summary>MapArchiveToEmotionObjects restores ShouldFire when attempted but not triggered (deferred peek).</summary>
    [TestMethod]
    public void MapArchiveToEmotionObjects_RestoresShouldFire_WhenAttemptedNotTriggered()
    {
        var state = new PetStateArchive
        {
            PeekEventDate = "2026-10-24",
            PeekCandidateMinutes = 30,
            PeekHasAttempted = true,
            PeekHasTriggered = false,
            PeekEffectiveRunMs = 30 * 60_000.0
        };

        var (_, _, _, peek) = App.MapArchiveToEmotionObjects(state);

        Assert.IsTrue(peek.HasAttempted);
        Assert.IsTrue(peek.ShouldFire, "Deferred peek (attempted but not triggered) should restore ShouldFire=true.");
        Assert.IsFalse(peek.HasTriggered);
    }

    /// <summary>MapArchiveToEmotionObjects does not restore ShouldFire when already triggered.</summary>
    [TestMethod]
    public void MapArchiveToEmotionObjects_DoesNotRestoreShouldFire_WhenTriggered()
    {
        var state = new PetStateArchive
        {
            PeekEventDate = "2026-10-24",
            PeekCandidateMinutes = 30,
            PeekHasAttempted = true,
            PeekHasTriggered = true,
            PeekEffectiveRunMs = 30 * 60_000.0
        };

        var (_, _, _, peek) = App.MapArchiveToEmotionObjects(state);

        Assert.IsTrue(peek.HasTriggered);
        // ShouldFire doesn't matter when HasTriggered is true, but it should be false
        // because the production mapping only sets it when !HasTriggered.
        Assert.IsFalse(peek.ShouldFire);
    }

    /// <summary>MapEmotionObjectsToArchive syncs PeekScheduler state to archive.</summary>
    [TestMethod]
    public void MapEmotionObjectsToArchive_SyncsPeekScheduler()
    {
        var state = new PetStateArchive();
        var acc = new Core.Emotion.EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
        var expr = new Core.Emotion.GrievanceExpressionState();
        var peek = new PeekScheduler(new Random(42));

        // Simulate a tick to create a plan.
        peek.Tick("2026-10-24", 1000, false, false, 1000);

        App.MapEmotionObjectsToArchive(state, acc, expr, peek);

        Assert.AreEqual("2026-10-24", state.PeekEventDate);
        Assert.AreEqual(peek.CandidateMinutes, state.PeekCandidateMinutes);
        Assert.IsFalse(state.PeekHasAttempted);
        Assert.IsFalse(state.PeekHasTriggered);
        Assert.AreEqual(1000.0, state.PeekEffectiveRunMs, 0.001);
    }

    /// <summary>Full round-trip: create plan → save → reload → restore → verify same plan.</summary>
    [TestMethod]
    public void FullRoundTrip_PlanCreated_Saved_Restored_DoesNotReRoll()
    {
        var archive = new ArchiveService(_tempDir);

        // Create a scheduler and tick to create a plan.
        var peek1 = new PeekScheduler(new Random(42));
        peek1.Tick("2026-10-24", 1000, false, false, 1000);
        var originalCandidate = peek1.CandidateMinutes;

        // Save to archive.
        var state = new PetStateArchive();
        var acc = new Core.Emotion.EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
        var expr = new Core.Emotion.GrievanceExpressionState();
        App.MapEmotionObjectsToArchive(state, acc, expr, peek1);
        archive.SaveState(state);

        // Reload from disk.
        var (loadedState, _, _) = archive.LoadState();

        // Restore into a new scheduler with a DIFFERENT seed.
        var (_, _, _, peek2) = App.MapArchiveToEmotionObjects(loadedState);

        Assert.AreEqual(originalCandidate, peek2.CandidateMinutes,
            "Restored candidate must match original — restart must not re-roll.");
        Assert.AreEqual("2026-10-24", peek2.EventDate);

        // Tick on the same day — should not create a new plan.
        peek2.Tick("2026-10-24", 1000, false, false, 2000);
        Assert.AreEqual(originalCandidate, peek2.CandidateMinutes,
            "Same-day tick after restore must not re-roll candidate.");
    }

    /// <summary>Full round-trip with triggered state: save → reload → restore → does not re-trigger.</summary>
    [TestMethod]
    public void FullRoundTrip_TriggeredState_Saved_Restored_DoesNotReTrigger()
    {
        var archive = new ArchiveService(_tempDir);

        // Find a seed that triggers.
        int seed = -1;
        for (int s = 0; s < 10000; s++)
        {
            var p = new PeekScheduler(new Random(s));
            p.Tick("2026-10-24", 1000, false, false, 1000);
            var r = p.Tick("2026-10-24", p.CandidateMinutes * 60_000.0, false, false, p.CandidateMinutes * 60_000.0 + 1000);
            if (r == PeekTickResult.PeekStarted)
            {
                // Complete the peek.
                p.Tick("2026-10-24", 100, false, false, p.CandidateMinutes * 60_000.0 + 1000 + PeekScheduler.PeekDurationMs);
                seed = s;
                break;
            }
        }
        Assert.IsTrue(seed >= 0, "Could not find a triggering seed.");

        var peek1 = new PeekScheduler(new Random(seed));
        peek1.Tick("2026-10-24", 1000, false, false, 1000);
        peek1.Tick("2026-10-24", peek1.CandidateMinutes * 60_000.0, false, false, peek1.CandidateMinutes * 60_000.0 + 1000);
        peek1.Tick("2026-10-24", 100, false, false, peek1.CandidateMinutes * 60_000.0 + 1000 + PeekScheduler.PeekDurationMs);
        Assert.IsTrue(peek1.HasTriggered);

        // Save.
        var state = new PetStateArchive();
        var acc = new Core.Emotion.EmotionDecayAccumulator(50, 50, DateTime.UtcNow);
        var expr = new Core.Emotion.GrievanceExpressionState();
        App.MapEmotionObjectsToArchive(state, acc, expr, peek1);
        archive.SaveState(state);

        // Reload and restore.
        var (loadedState, _, _) = archive.LoadState();
        var (_, _, _, peek2) = App.MapArchiveToEmotionObjects(loadedState);

        Assert.IsTrue(peek2.HasTriggered, "Restored scheduler should have HasTriggered=true.");

        // Tick — should not re-trigger.
        var result = peek2.Tick("2026-10-24", 60_000.0, false, false, 999_999);
        Assert.AreEqual(PeekTickResult.NoAction, result, "Already triggered scheduler should not re-trigger.");
    }

    /// <summary>v2 file with emotion but no peek fields → save as v3 → reload has peek defaults.</summary>
    [TestMethod]
    public void V2File_UpgradeToV3_PeekFieldsAreDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        // Write a v2 file.
        var v2Json = """
        {
            "schemaVersion": 2,
            "windowLeft": 100,
            "windowTop": 200,
            "savedDeviceName": null,
            "lastSavedUtc": "2026-10-22T12:00:00.0000000Z",
            "isPaused": false,
            "affinity": 65,
            "grievance": 50,
            "lastDecayUtc": "2026-10-22T12:00:00.0000000Z",
            "pendingGrievanceExpression": false,
            "grievanceExpressionDate": null
        }
        """;
        File.WriteAllText(archive.StateFilePath, v2Json);

        // Load (v2), then save (upgrades to v3).
        var (loaded, _, _) = archive.LoadState();
        Assert.AreEqual(2, loaded.SchemaVersion, "Before save, should still be v2.");
        Assert.IsNull(loaded.PeekEventDate, "v2 load should have null peek fields.");

        archive.SaveState(loaded);
        var (reloaded, _, _) = archive.LoadState();

        Assert.AreEqual(3, reloaded.SchemaVersion, "After save, should be upgraded to v3.");
        Assert.AreEqual(65, reloaded.Affinity, "Emotion fields should survive upgrade.");
        Assert.IsNull(reloaded.PeekEventDate, "Peek fields should be defaults after v2→v3 upgrade.");
        Assert.AreEqual(0, reloaded.PeekCandidateMinutes);
        Assert.IsFalse(reloaded.PeekHasAttempted);
        Assert.IsFalse(reloaded.PeekHasTriggered);
        Assert.AreEqual(0.0, reloaded.PeekEffectiveRunMs, 0.001);
    }
}
