using System.IO;
using System.Text.Json;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// M-05 存档恢复测试：使用临时目录验证 ArchiveService 的
/// 保存/恢复、损坏恢复、未知版本保护和防抖保存。
/// </summary>
[TestClass]
public class ArchiveServiceTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DesktopPet-Test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>M-05-1: 保存后重新加载，设置和位置保留。</summary>
    [TestMethod]
    public void SaveAndLoad_State_PreservesPosition()
    {
        var archive = new ArchiveService(_tempDir);
        var state = new PetStateArchive
        {
            IsPaused = true
        };
        state.Pets.Add(new PetInstanceArchive
        {
            WindowLeft = 250.5,
            WindowTop = 180.3,
            SavedDeviceName = @"\\.\DISPLAY1"
        });

        archive.SaveState(state);

        // Verify file exists.
        Assert.IsTrue(File.Exists(archive.StateFilePath));

        var (loaded, fromBackup, unknown) = archive.LoadState();

        Assert.AreEqual(250.5, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(180.3, loaded.Pets[0].WindowTop, 0.001);
        Assert.AreEqual(@"\\.\DISPLAY1", loaded.Pets[0].SavedDeviceName);
        Assert.IsTrue(loaded.IsPaused);
        Assert.IsFalse(fromBackup);
        Assert.IsFalse(unknown);
        Assert.AreEqual(PetStateArchive.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    /// <summary>M-05-1: Settings 保存后重新加载。</summary>
    [TestMethod]
    public void SaveAndLoad_Settings_PreservesValues()
    {
        var archive = new ArchiveService(_tempDir);
        var settings = new PetSettingsArchive
        {
            AnimationSpeed = 1.5,
            AutoStart = true
        };

        archive.SaveSettings(settings);

        var (loaded, _, _) = archive.LoadSettings();

        Assert.AreEqual(1.5, loaded.AnimationSpeed, 0.001);
        Assert.IsTrue(loaded.AutoStart);
    }

    /// <summary>M-05-2: 截断 JSON 模拟损坏，确认保留坏文件、优先有效备份。</summary>
    [TestMethod]
    public void LoadState_CorruptedFile_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);

        // First save a valid state (creates .bak on next save).
        var s1 = new PetStateArchive();
        s1.Pets.Add(new PetInstanceArchive { WindowLeft = 100, WindowTop = 200 });
        archive.SaveState(s1);

        // Save again with different values; previous becomes .bak.
        var s2 = new PetStateArchive();
        s2.Pets.Add(new PetInstanceArchive { WindowLeft = 300, WindowTop = 400 });
        archive.SaveState(s2);

        // Corrupt the primary file.
        File.WriteAllText(archive.StateFilePath, "{ \"schemaVersion\": 1, \"windowLeft\": tru");

        var (loaded, fromBackup, _) = archive.LoadState();

        // Should have loaded from backup with the first save's values.
        Assert.IsTrue(fromBackup);
        Assert.AreEqual(100, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(200, loaded.Pets[0].WindowTop, 0.001);

        // Corrupted file should be preserved as .corrupted.
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    /// <summary>M-05-2: 损坏文件无备份时回退默认值。</summary>
    [TestMethod]
    public void LoadState_CorruptedNoBackup_FallsBackToDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        // Write a corrupted file directly (no prior valid save = no backup).
        File.WriteAllText(archive.StateFilePath, "{ broken json }}}");

        var (loaded, fromBackup, _) = archive.LoadState();

        // Should fall back to defaults.
        Assert.IsFalse(fromBackup);
        Assert.AreEqual(new PetInstanceArchive().WindowLeft, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(new PetInstanceArchive().WindowTop, loaded.Pets[0].WindowTop, 0.001);

        // Corrupted file preserved.
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    /// <summary>M-05-3: 未知更高 schemaVersion 不能无提示地用旧格式覆盖原文件。</summary>
    [TestMethod]
    public void SaveState_UnknownHigherVersion_DoesNotOverwrite()
    {
        var archive = new ArchiveService(_tempDir);

        // Write a file with a higher schemaVersion directly.
        var higherVersionJson = """
            {
              "schemaVersion": 99,
              "isPaused": false,
              "pets": [
                {
                  "petId": "bear-1",
                  "windowLeft": 500,
                  "windowTop": 600
                }
              ]
            }
            """;
        File.WriteAllText(archive.StateFilePath, higherVersionJson);

        // Load it — should detect unknown version.
        var (loaded, _, unknown) = archive.LoadState();
        Assert.IsTrue(unknown);
        Assert.AreEqual(99, loaded.SchemaVersion);

        // Attempt to save — should NOT overwrite the higher version file.
        archive.SaveState(loaded);

        // Re-read the raw file to verify it still has version 99.
        var rawContent = File.ReadAllText(archive.StateFilePath);
        Assert.IsTrue(rawContent.Contains("\"schemaVersion\": 99"),
            "File should still contain schemaVersion 99, not be overwritten with version 1.");
    }

    /// <summary>M-05-4: 正常退出时最后一次位置变更也能保存（Flush）。</summary>
    [TestMethod]
    public void SaveCoordinator_Flush_SavesImmediately()
    {
        var archive = new ArchiveService(_tempDir);
        using var coordinator = new SaveCoordinator(archive, TimeSpan.FromMilliseconds(100));

        var state = new PetStateArchive();
        state.Pets.Add(new PetInstanceArchive { WindowLeft = 42, WindowTop = 84 });
        coordinator.MarkStateDirty(state);

        // Flush should save immediately without waiting for debounce.
        coordinator.Flush();

        Assert.IsTrue(File.Exists(archive.StateFilePath));

        var (loaded, _, _) = archive.LoadState();
        Assert.AreEqual(42, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(84, loaded.Pets[0].WindowTop, 0.001);
    }

    /// <summary>M-05-4: 频繁拖动不会逐帧写盘（防抖）。</summary>
    [TestMethod]
    public void SaveCoordinator_FrequentMarks_OnlySavesOnceAfterDebounce()
    {
        var archive = new ArchiveService(_tempDir);
        using var coordinator = new SaveCoordinator(archive, TimeSpan.FromMilliseconds(200));

        // Rapidly mark dirty multiple times.
        for (int i = 0; i < 10; i++)
        {
            var s = new PetStateArchive();
            s.Pets.Add(new PetInstanceArchive { WindowLeft = i, WindowTop = i });
            coordinator.MarkStateDirty(s);
            Thread.Sleep(10);
        }

        // File should not exist yet (debounce hasn't elapsed).
        Assert.IsFalse(File.Exists(archive.StateFilePath),
            "File should not be written during rapid changes (debounce).");

        // Wait for debounce to fire.
        Thread.Sleep(400);

        // Now file should exist with the last values.
        Assert.IsTrue(File.Exists(archive.StateFilePath));
        var (loaded, _, _) = archive.LoadState();
        Assert.AreEqual(9, loaded.Pets[0].WindowLeft, 0.001);
    }

    /// <summary>Safe write preserves backup of previous valid file.</summary>
    [TestMethod]
    public void SaveState_SecondSave_CreatesBackupOfPrevious()
    {
        var archive = new ArchiveService(_tempDir);

        var s1 = new PetStateArchive();
        s1.Pets.Add(new PetInstanceArchive { WindowLeft = 111 });
        archive.SaveState(s1);
        Assert.IsFalse(File.Exists(archive.StateBackupPath), "First save should not have a backup.");

        var s2 = new PetStateArchive();
        s2.Pets.Add(new PetInstanceArchive { WindowLeft = 222 });
        archive.SaveState(s2);
        Assert.IsTrue(File.Exists(archive.StateBackupPath), "Second save should create backup of first.");

        // Backup should contain the first save's values.
        var backupJson = File.ReadAllText(archive.StateBackupPath);
        Assert.IsTrue(backupJson.Contains("111"), "Backup should contain previous values.");
    }

    /// <summary>Missing files return defaults without error.</summary>
    [TestMethod]
    public void LoadState_NoFile_ReturnsDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        var (state, fromBackup, unknown) = archive.LoadState();
        var (settings, _, _) = archive.LoadSettings();

        Assert.AreEqual(new PetInstanceArchive().WindowLeft, state.Pets[0].WindowLeft, 0.001);
        Assert.IsFalse(fromBackup);
        Assert.IsFalse(unknown);
        Assert.AreEqual(new PetSettingsArchive().AnimationSpeed, settings.AnimationSpeed, 0.001);
    }

    /// <summary>v1 file (schemaVersion=1) loads with emotion defaults and lastDecayUtc fallback.</summary>
    [TestMethod]
    public void LoadState_V1File_EmotionFieldsFallbackToDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        // Write a v1 state file with no emotion fields.
        var v1Json = """
            {
              "schemaVersion": 1,
              "windowLeft": 150,
              "windowTop": 250,
              "savedDeviceName": "\\\\.\\DISPLAY1",
              "lastSavedUtc": "2026-10-20T08:00:00.0000000Z",
              "isPaused": false
            }
            """;
        File.WriteAllText(archive.StateFilePath, v1Json);

        var (loaded, fromBackup, unknown) = archive.LoadState();

        // Position fields preserved (migrated to pets[0]).
        Assert.AreEqual(150, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(250, loaded.Pets[0].WindowTop, 0.001);
        Assert.IsFalse(unknown);

        // Emotion fields fall back to defaults.
        Assert.AreEqual(50, loaded.Pets[0].Grievance);
        Assert.IsFalse(loaded.Pets[0].PendingGrievanceExpression);
        Assert.IsNull(loaded.Pets[0].GrievanceExpressionDate);

        // lastDecayUtc should fall back to lastSavedUtc per PRODUCT R-02.
        Assert.AreEqual("2026-10-20T08:00:00.0000000Z", loaded.Pets[0].LastDecayUtc);
    }

    /// <summary>v2 file with emotion values loads correctly.</summary>
    [TestMethod]
    public void LoadState_V2File_PreservesEmotionFields()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive();
        state.Pets.Add(new PetInstanceArchive
        {
            WindowLeft = 100,
            WindowTop = 200,
            Grievance = 85,
            LastDecayUtc = "2026-10-22T12:00:00.0000000Z",
            PendingGrievanceExpression = true,
            GrievanceExpressionDate = "2026-10-22"
        });

        archive.SaveState(state);
        var (loaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(PetStateArchive.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(85, loaded.Pets[0].Grievance);
        Assert.AreEqual("2026-10-22T12:00:00.0000000Z", loaded.Pets[0].LastDecayUtc);
        Assert.IsTrue(loaded.Pets[0].PendingGrievanceExpression);
        Assert.AreEqual("2026-10-22", loaded.Pets[0].GrievanceExpressionDate);
    }

    /// <summary>Restart with pending expression does not re-award or lose pending state.</summary>
    [TestMethod]
    public void LoadState_V2PendingExpression_SurvivesRestart()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive();
        state.Pets.Add(new PetInstanceArchive
        {
            Grievance = 82,
            PendingGrievanceExpression = true,
            GrievanceExpressionDate = "2026-10-22",
            LastDecayUtc = "2026-10-22T18:00:00.0000000Z"
        });

        archive.SaveState(state);
        var (loaded, _, _) = archive.LoadState();

        // Pending state and date preserved across restart.
        Assert.IsTrue(loaded.Pets[0].PendingGrievanceExpression);
        Assert.AreEqual("2026-10-22", loaded.Pets[0].GrievanceExpressionDate);
        Assert.AreEqual(82, loaded.Pets[0].Grievance);
    }

    // ── P1-004 v3 rare-event archive tests ──

    /// <summary>v3 archive with rare-event plan saves and reloads all fields.</summary>
    [TestMethod]
    public void SaveAndLoad_V3RareEventPlan_AllFieldsPreserved()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            SchemaVersion = 3,
            AccumulatedRuntimeMinutes = 130.5,
            RareEventPlan = new RareEventPlanSnapshot
            {
                LocalDate = "2026-11-04",
                TriggerDelayMinutes = 45,
                TriggerRoll = 0.3,
                Consumed = false,
                AccumulatedBaselineMinutes = 100.0
            }
        };
        state.Pets.Add(new PetInstanceArchive
        {
            Grievance = 60,
            LastDecayUtc = "2026-11-04T10:00:00.0000000Z"
        });

        archive.SaveState(state);
        Assert.IsTrue(File.Exists(archive.StateFilePath));

        var (loaded, fromBackup, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(PetStateArchive.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(130.5, loaded.AccumulatedRuntimeMinutes, 0.001);

        Assert.IsNotNull(loaded.RareEventPlan);
        Assert.AreEqual("2026-11-04", loaded.RareEventPlan!.LocalDate);
        Assert.AreEqual(45, loaded.RareEventPlan.TriggerDelayMinutes);
        Assert.AreEqual(0.3, loaded.RareEventPlan.TriggerRoll, 0.0001);
        Assert.IsFalse(loaded.RareEventPlan.Consumed);
        Assert.AreEqual(100.0, loaded.RareEventPlan.AccumulatedBaselineMinutes, 0.001);
    }

    /// <summary>v3 archive with consumed plan preserves consumed flag across restart.</summary>
    [TestMethod]
    public void SaveAndLoad_V3ConsumedPlan_ConsumedFlagPreserved()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            AccumulatedRuntimeMinutes = 200.0,
            RareEventPlan = new RareEventPlanSnapshot
            {
                LocalDate = "2026-11-04",
                TriggerDelayMinutes = 30,
                TriggerRoll = 0.7,
                Consumed = true,
                AccumulatedBaselineMinutes = 150.0
            }
        };

        archive.SaveState(state);
        var (loaded, _, _) = archive.LoadState();

        Assert.IsNotNull(loaded.RareEventPlan);
        Assert.IsTrue(loaded.RareEventPlan!.Consumed);
        Assert.AreEqual(200.0, loaded.AccumulatedRuntimeMinutes, 0.001);
    }

    /// <summary>v3 archive with null plan saves and loads correctly.</summary>
    [TestMethod]
    public void SaveAndLoad_V3NullPlan_LoadsAsNull()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            AccumulatedRuntimeMinutes = 50.0,
            RareEventPlan = null
        };

        archive.SaveState(state);
        var (loaded, _, _) = archive.LoadState();

        Assert.IsNull(loaded.RareEventPlan);
        Assert.AreEqual(50.0, loaded.AccumulatedRuntimeMinutes, 0.001);
    }

    /// <summary>v1 file loads with rare-event fields at defaults (no plan, zero accumulated).</summary>
    [TestMethod]
    public void LoadState_V1File_RareEventFieldsFallbackToDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        var v1Json = """
            {
              "schemaVersion": 1,
              "windowLeft": 150,
              "windowTop": 250,
              "savedDeviceName": null,
              "lastSavedUtc": "2026-10-20T08:00:00.0000000Z",
              "isPaused": false
            }
            """;
        File.WriteAllText(archive.StateFilePath, v1Json);

        var (loaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(0.0, loaded.AccumulatedRuntimeMinutes, 0.001);
        Assert.IsNull(loaded.RareEventPlan);
    }

    /// <summary>v2 file loads with rare-event fields at defaults (no plan, zero accumulated).</summary>
    [TestMethod]
    public void LoadState_V2File_RareEventFieldsFallbackToDefaults()
    {
        var archive = new ArchiveService(_tempDir);

        var v2Json = """
            {
              "schemaVersion": 2,
              "windowLeft": 100,
              "windowTop": 200,
              "grievance": 75,
              "lastDecayUtc": "2026-10-22T12:00:00.0000000Z",
              "pendingGrievanceExpression": false,
              "grievanceExpressionDate": null,
              "isPaused": false
            }
            """;
        File.WriteAllText(archive.StateFilePath, v2Json);

        var (loaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(75, loaded.Pets[0].Grievance);
        Assert.AreEqual(0.0, loaded.AccumulatedRuntimeMinutes, 0.001);
        Assert.IsNull(loaded.RareEventPlan);
    }

    /// <summary>
    /// Full restart recovery chain: save plan snapshot → reload → restore into
    /// RareEventScheduler → verify plan is functional (same date, delay, roll).
    /// </summary>
    [TestMethod]
    public void RestartRecovery_SavePlanSnapshot_RestoreIntoScheduler_PlanFunctional()
    {
        var archive = new ArchiveService(_tempDir);

        // Create and save state with a plan snapshot.
        var state = new PetStateArchive
        {
            AccumulatedRuntimeMinutes = 130.0,
            RareEventPlan = new RareEventPlanSnapshot
            {
                LocalDate = "2026-11-04",
                TriggerDelayMinutes = 25,
                TriggerRoll = 0.2,
                Consumed = false,
                AccumulatedBaselineMinutes = 110.0
            }
        };
        archive.SaveState(state);

        // Simulate restart: load archive.
        var (loaded, _, _) = archive.LoadState();

        // Restore plan into scheduler via snapshot → RareEventPlan mapping.
        var scheduler = new RareEventScheduler(0.5);
        RareEventPlan? restoredPlan = null;
        if (loaded.RareEventPlan is not null)
        {
            restoredPlan = new RareEventPlan
            {
                LocalDate = loaded.RareEventPlan.LocalDate!,
                TriggerDelayMinutes = loaded.RareEventPlan.TriggerDelayMinutes,
                TriggerRoll = loaded.RareEventPlan.TriggerRoll,
                Consumed = loaded.RareEventPlan.Consumed,
                AccumulatedBaselineMinutes = loaded.RareEventPlan.AccumulatedBaselineMinutes
            };
        }
        bool accepted = scheduler.RestorePlan(restoredPlan);

        Assert.IsTrue(accepted, "Valid plan snapshot must be accepted by RestorePlan.");
        Assert.IsNotNull(scheduler.CurrentPlan);
        Assert.AreEqual("2026-11-04", scheduler.CurrentPlan!.LocalDate);
        Assert.AreEqual(25, scheduler.CurrentPlan.TriggerDelayMinutes);
        Assert.AreEqual(0.2, scheduler.CurrentPlan.TriggerRoll, 0.0001);
        Assert.IsFalse(scheduler.CurrentPlan.Consumed);
        Assert.AreEqual(110.0, scheduler.CurrentPlan.AccumulatedBaselineMinutes, 0.001);

        // Verify the restored plan is functional: evaluate with enough runtime.
        // Daily effective = 130 - 110 = 20, delay = 25, so 20 < 25 → DelayNotReached.
        var result = scheduler.Evaluate(130.0, "2026-11-04");
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.DelayNotReached, result.Reason);

        // At 136 (daily effective = 26 ≥ 25), plan should be consumed and fire (roll 0.2 < 0.5).
        var result2 = scheduler.Evaluate(136.0, "2026-11-04");
        Assert.IsTrue(result2.ShouldFire);
    }

    /// <summary>
    /// Restart recovery with consumed plan: restored plan should not re-fire.
    /// </summary>
    [TestMethod]
    public void RestartRecovery_ConsumedPlan_DoesNotRefire()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            AccumulatedRuntimeMinutes = 200.0,
            RareEventPlan = new RareEventPlanSnapshot
            {
                LocalDate = "2026-11-04",
                TriggerDelayMinutes = 20,
                TriggerRoll = 0.1,
                Consumed = true,
                AccumulatedBaselineMinutes = 100.0
            }
        };
        archive.SaveState(state);

        var (loaded, _, _) = archive.LoadState();

        var scheduler = new RareEventScheduler(0.5);
        var restoredPlan = new RareEventPlan
        {
            LocalDate = loaded.RareEventPlan!.LocalDate!,
            TriggerDelayMinutes = loaded.RareEventPlan.TriggerDelayMinutes,
            TriggerRoll = loaded.RareEventPlan.TriggerRoll,
            Consumed = loaded.RareEventPlan.Consumed,
            AccumulatedBaselineMinutes = loaded.RareEventPlan.AccumulatedBaselineMinutes
        };
        scheduler.RestorePlan(restoredPlan);

        // Even with enough runtime, consumed plan should not fire.
        var result = scheduler.Evaluate(200.0, "2026-11-04");
        Assert.IsFalse(result.ShouldFire);
        Assert.AreEqual(RareEventScheduler.DenyReason.PlanConsumed, result.Reason);
    }

    /// <summary>
    /// Restart recovery with null plan: scheduler starts fresh, new plan created on next day.
    /// </summary>
    [TestMethod]
    public void RestartRecovery_NullPlan_SchedulerStartsFresh()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            AccumulatedRuntimeMinutes = 50.0,
            RareEventPlan = null
        };
        archive.SaveState(state);

        var (loaded, _, _) = archive.LoadState();

        Assert.IsNull(loaded.RareEventPlan);

        var scheduler = new RareEventScheduler(0.5);
        scheduler.RestorePlan(null);

        Assert.IsNull(scheduler.CurrentPlan);

        // Creating a new plan should work.
        var rng = new Random(42);
        bool created = scheduler.EnsurePlanForDate("2026-11-05", rng, currentAccumulatedMinutes: 50.0);
        Assert.IsTrue(created);
        Assert.IsNotNull(scheduler.CurrentPlan);
        Assert.AreEqual("2026-11-05", scheduler.CurrentPlan!.LocalDate);
    }

    // ── P3-001 S1: v3→v4 Migration Tests ───────────────────────────────

    /// <summary>
    /// P3-001 S1: A v3 archive with flat per-pet fields (windowLeft,
    /// windowTop, grievance, etc.) must be migrated to v4 with all
    /// those fields moved into pets[0].
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void MigrateV3_FlatFields_MoveToPets0()
    {
        var archive = new ArchiveService(_tempDir);
        var v3Json = """
        {
            "schemaVersion": 3,
            "windowLeft": 333.0,
            "windowTop": 444.0,
            "savedDeviceName": "DISPLAY2",
            "lastSavedUtc": "2026-10-15T12:00:00Z",
            "isPaused": true,
            "grievance": 65,
            "lastDecayUtc": "2026-10-15T11:30:00Z",
            "pendingGrievanceExpression": true,
            "grievanceExpressionDate": "2026-10-15",
            "accumulatedRuntimeMinutes": 42.5,
            "rareEventPlan": {
                "localDate": "2026-10-15",
                "triggerDelayMinutes": 30,
                "triggerRoll": 0.3,
                "consumed": false,
                "accumulatedBaselineMinutes": 10.0
            }
        }
        """;
        File.WriteAllText(archive.StateFilePath, v3Json);

        var (loaded, fromBackup, unknown) = archive.LoadState();

        Assert.IsFalse(fromBackup);
        Assert.IsFalse(unknown);
        Assert.AreEqual(4, loaded.SchemaVersion);

        // Per-pet fields must be in pets[0]
        Assert.AreEqual(1, loaded.Pets.Count);
        var pet = loaded.Pets[0];
        Assert.AreEqual("bear-1", pet.PetId);
        Assert.AreEqual(Personality.Proactive, pet.Personality);
        Assert.AreEqual(333.0, pet.WindowLeft, 0.001);
        Assert.AreEqual(444.0, pet.WindowTop, 0.001);
        Assert.AreEqual("DISPLAY2", pet.SavedDeviceName);
        Assert.AreEqual(65, pet.Grievance);
        Assert.AreEqual("2026-10-15T11:30:00Z", pet.LastDecayUtc);
        Assert.IsTrue(pet.PendingGrievanceExpression);
        Assert.AreEqual("2026-10-15", pet.GrievanceExpressionDate);

        // Global fields must remain on the root
        Assert.IsTrue(loaded.IsPaused);
        Assert.AreEqual(42.5, loaded.AccumulatedRuntimeMinutes, 0.001);
        Assert.IsNotNull(loaded.RareEventPlan);
        Assert.AreEqual("2026-10-15", loaded.RareEventPlan!.LocalDate);
        Assert.AreEqual(30, loaded.RareEventPlan.TriggerDelayMinutes);
    }

    /// <summary>
    /// P3-001 S1: v3→v4 migration must NOT duplicate per-pet fields
    /// onto a second pet. Only pets[0] should exist after migration.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void MigrateV3_DoesNotCreateSecondPet()
    {
        var archive = new ArchiveService(_tempDir);
        var v3Json = """
        {
            "schemaVersion": 3,
            "windowLeft": 100.0,
            "windowTop": 200.0,
            "grievance": 50
        }
        """;
        File.WriteAllText(archive.StateFilePath, v3Json);

        var (loaded, _, _) = archive.LoadState();

        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
    }

    /// <summary>
    /// P3-001 S1: A v1 archive (no lastDecayUtc) must fall back
    /// lastDecayUtc to lastSavedUtc during migration.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void MigrateV1_LastDecayUtc_FallsBackToLastSavedUtc()
    {
        var archive = new ArchiveService(_tempDir);
        var v1Json = """
        {
            "schemaVersion": 1,
            "windowLeft": 100.0,
            "windowTop": 200.0,
            "lastSavedUtc": "2026-01-01T00:00:00Z",
            "grievance": 50
        }
        """;
        File.WriteAllText(archive.StateFilePath, v1Json);

        var (loaded, _, _) = archive.LoadState();

        Assert.AreEqual(4, loaded.SchemaVersion);
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.AreEqual("2026-01-01T00:00:00Z", loaded.Pets[0].LastDecayUtc);
    }

    /// <summary>
    /// P3-001 S1: After v3→v4 migration, saving and reloading must
    /// preserve all migrated fields in the v4 format (round-trip).
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void MigrateV3_RoundTrip_PreservesMigratedFields()
    {
        var archive = new ArchiveService(_tempDir);
        var v3Json = """
        {
            "schemaVersion": 3,
            "windowLeft": 555.0,
            "windowTop": 666.0,
            "savedDeviceName": "DISPLAY1",
            "grievance": 70,
            "lastDecayUtc": "2026-08-20T10:00:00Z",
            "pendingGrievanceExpression": true,
            "grievanceExpressionDate": "2026-08-20",
            "isPaused": false,
            "accumulatedRuntimeMinutes": 15.0
        }
        """;
        File.WriteAllText(archive.StateFilePath, v3Json);

        // Load (triggers migration)
        var (loaded, _, _) = archive.LoadState();
        Assert.AreEqual(4, loaded.SchemaVersion);

        // Save (writes v4 format)
        archive.SaveState(loaded);

        // Reload (should be v4, no migration needed)
        var (reloaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(4, reloaded.SchemaVersion);
        Assert.AreEqual(1, reloaded.Pets.Count);
        Assert.AreEqual(555.0, reloaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(666.0, reloaded.Pets[0].WindowTop, 0.001);
        Assert.AreEqual("DISPLAY1", reloaded.Pets[0].SavedDeviceName);
        Assert.AreEqual(70, reloaded.Pets[0].Grievance);
        Assert.AreEqual("2026-08-20T10:00:00Z", reloaded.Pets[0].LastDecayUtc);
        Assert.IsTrue(reloaded.Pets[0].PendingGrievanceExpression);
        Assert.AreEqual("2026-08-20", reloaded.Pets[0].GrievanceExpressionDate);
        Assert.AreEqual(15.0, reloaded.AccumulatedRuntimeMinutes, 0.001);
    }

    /// <summary>
    /// P3-001 S1: A v3 archive with missing optional fields (savedDeviceName,
    /// pendingGrievanceExpression, grievanceExpressionDate) must use defaults.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void MigrateV3_MissingOptionalFields_UseDefaults()
    {
        var archive = new ArchiveService(_tempDir);
        var v3Json = """
        {
            "schemaVersion": 3,
            "windowLeft": 100.0,
            "windowTop": 100.0,
            "grievance": 50
        }
        """;
        File.WriteAllText(archive.StateFilePath, v3Json);

        var (loaded, _, _) = archive.LoadState();

        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.IsNull(loaded.Pets[0].SavedDeviceName);
        Assert.IsFalse(loaded.Pets[0].PendingGrievanceExpression);
        Assert.IsNull(loaded.Pets[0].GrievanceExpressionDate);
        Assert.IsNull(loaded.Pets[0].LastDecayUtc);
    }

    /// <summary>
    /// P3-001 S1: A fresh archive (no file) must get a default pet
    /// with PetId "bear-1" and Personality.Proactive.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void FreshArchive_GetsDefaultPet()
    {
        var archive = new ArchiveService(_tempDir);

        var (loaded, fromBackup, unknown) = archive.LoadState();

        Assert.IsFalse(fromBackup);
        Assert.IsFalse(unknown);
        Assert.AreEqual(4, loaded.SchemaVersion);
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
        Assert.AreEqual(Personality.Proactive, loaded.Pets[0].Personality);
    }

    // ── F-01 R1: 旧档字段类型错误不逃逸异常 ──────────────────────────

    /// <summary>
    /// F-01 R1: v3 archive with wrong-type windowLeft (string "oops" instead
    /// of number) must not throw InvalidOperationException. With a valid
    /// backup, should fall back to backup; without backup, type-mismatched
    /// field uses default. Either way, no unhandled exception escapes.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F01_V3WrongTypeWindowLeft_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":3,"windowLeft":"oops","grievance":65}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":321,"grievance":67}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        // Should have fallen back to backup (type error → corrupted → backup)
        Assert.IsTrue(fromBackup, "Should fall back to backup when primary has type error.");
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.AreEqual(321, loaded.Pets[0].WindowLeft, 0.001,
            "Backup windowLeft should be restored.");
        Assert.AreEqual(67, loaded.Pets[0].Grievance,
            "Backup grievance should be restored.");
    }

    /// <summary>
    /// F-01 R1: v3 archive with wrong-type windowLeft and NO backup.
    /// Must not throw; type-mismatched field falls back to default.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F01_V3WrongTypeWindowLeft_NoBackup_UsesDefaults()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":3,"windowLeft":"oops","grievance":65}""");
        // No backup file.

        var (loaded, fromBackup, _) = archive.LoadState();

        // No backup → defaults. Type-mismatched field uses default value.
        Assert.IsFalse(fromBackup);
        Assert.AreEqual(1, loaded.Pets.Count);
        // grievance is a valid int in the JSON, so it should be read correctly
        // even though windowLeft was wrong type (SafeRead per-field).
        // But since the whole file is treated as corrupted (InvalidOperationException
        // caught in DeserializeFile), we get a fresh default PetStateArchive.
        // The key assertion is: no exception thrown, pet exists with defaults.
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
    }

    /// <summary>
    /// F-01 R1: v3 archive with wrong-type grievance (string instead of int).
    /// Must not throw; with backup, falls back to backup.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F01_V3WrongTypeGrievance_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":3,"windowLeft":100,"grievance":"bad"}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":200,"grievance":75}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsTrue(fromBackup, "Should fall back to backup when primary has type error.");
        Assert.AreEqual(200, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(75, loaded.Pets[0].Grievance);
    }

    /// <summary>
    /// F-01 R1: v3 archive with wrong-type pendingGrievanceExpression (string
    /// instead of bool). Must not throw; with backup, falls back to backup.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F01_V3WrongTypeBool_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":3,"windowLeft":100,"grievance":50,"pendingGrievanceExpression":"notabool"}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":150,"grievance":55}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsTrue(fromBackup, "Should fall back to backup when primary has type error.");
        Assert.AreEqual(150, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(55, loaded.Pets[0].Grievance);
    }

    // ── F-02 R3: v4个体结构校验与备份优先恢复 ────────────────────────

    /// <summary>
    /// F-02 R3: v4 archive with pets:[null] is invalid known-v4 data.
    /// With a valid backup, LoadState must classify the primary as corrupted,
    /// preserve it as .corrupted, and restore the backup data (321/67).
    /// The test must assert FromBackup=true and the actual backup values,
    /// not allow "default or backup either way".
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4NullPet_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[null]}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":321,"grievance":67}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        // Must fall back to backup — not default.
        Assert.IsTrue(fromBackup, "Should fall back to backup when primary has null pet entry.");
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.IsNotNull(loaded.Pets[0]);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);

        // Must restore actual backup values, not defaults (100/50).
        Assert.AreEqual(321, loaded.Pets[0].WindowLeft, 0.001,
            "Backup windowLeft=321 must be restored, not default 100.");
        Assert.AreEqual(67, loaded.Pets[0].Grievance,
            "Backup grievance=67 must be restored, not default 50.");

        // Original corrupted file must be preserved.
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"),
            "Corrupted primary must be preserved as .corrupted.");

        // Production mapping must not crash on restored backup data.
        var (acc, _, _) = App.MapArchiveToEmotionObjects(loaded);
        Assert.IsNotNull(acc);
    }

    /// <summary>
    /// F-02 R3: v4 archive with pets:[null] and NO backup.
    /// LoadState must classify as corrupted, preserve .corrupted,
    /// and fall back to defaults (not from backup).
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4NullPet_NoBackup_PreservesCorruptedAndUsesDefaults()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[null]}""");
        // No backup.

        var (loaded, fromBackup, _) = archive.LoadState();

        // No backup → defaults, not from backup.
        Assert.IsFalse(fromBackup);
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.IsNotNull(loaded.Pets[0]);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
        Assert.AreEqual(100, loaded.Pets[0].WindowLeft, 0.001,
            "Default windowLeft=100 when no backup.");
        Assert.AreEqual(50, loaded.Pets[0].Grievance,
            "Default grievance=50 when no backup.");

        // Original corrupted file must still be preserved.
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"),
            "Corrupted primary must be preserved as .corrupted even without backup.");

        // Production mapping must not crash.
        var (acc, _, _) = App.MapArchiveToEmotionObjects(loaded);
        Assert.IsNotNull(acc);
    }

    /// <summary>
    /// F-02 R3: v4 archive with mixed null and valid entries is invalid
    /// known-v4 data. The entire file is classified as corrupted; with a
    /// valid backup, backup data is restored. We do NOT silently filter
    /// nulls and keep the valid entry from the corrupted file.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4MixedNullAndValid_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[null,{"petId":"bear-1","windowLeft":42,"grievance":60},null]}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":200,"grievance":70}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        // Must fall back to backup, not silently filter nulls and keep
        // the valid entry from the corrupted primary.
        Assert.IsTrue(fromBackup, "Mixed null/valid entries → corrupted → backup recovery.");
        Assert.AreEqual(200, loaded.Pets[0].WindowLeft, 0.001,
            "Backup windowLeft=200, not the 42 from corrupted primary.");
        Assert.AreEqual(70, loaded.Pets[0].Grievance,
            "Backup grievance=70, not the 60 from corrupted primary.");
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    /// <summary>
    /// F-02 R3: v4 archive with empty pets array is a valid fresh-archive
    /// case (not corruption). Default pet is created.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4EmptyPets_CreatesDefaultPet()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[]}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsFalse(fromBackup);
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.IsNotNull(loaded.Pets[0]);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
    }

    /// <summary>
    /// F-02 R3: v4 archive with pets:null (JSON null for the array itself)
    /// is a valid fresh-archive case. Default pet created.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4NullPetsArray_CreatesDefaultPet()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":null}""");

        var (loaded, _, _) = archive.LoadState();

        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.IsNotNull(loaded.Pets[0]);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
    }

    /// <summary>
    /// F-02 R3: After backup recovery from a null-pet primary, saving and
    /// reloading must preserve the v4 format with restored backup values.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4NullPet_WithBackup_RoundTrip_PreservesRestoredData()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[null]}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":321,"grievance":67}""");

        var (loaded, fromBackup, _) = archive.LoadState();
        Assert.IsTrue(fromBackup);
        Assert.AreEqual(321, loaded.Pets[0].WindowLeft, 0.001);

        // Save restored data and reload.
        archive.SaveState(loaded);
        var (reloaded, _, _) = archive.LoadState();

        Assert.AreEqual(4, reloaded.SchemaVersion);
        Assert.AreEqual(1, reloaded.Pets.Count);
        Assert.IsNotNull(reloaded.Pets[0]);
        Assert.AreEqual(321, reloaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(67, reloaded.Pets[0].Grievance);
    }

    // ── F-02 R3: 身份校验（空/重复ID/未知性格/超量）────────────────

    /// <summary>
    /// F-02 R3: v4 archive with a pet that has an empty PetId is invalid.
    /// With backup, must fall back to backup and preserve .corrupted.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4EmptyPetId_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[{"petId":"","windowLeft":42}]}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":300,"grievance":65}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsTrue(fromBackup, "Empty PetId → corrupted → backup recovery.");
        Assert.AreEqual(300, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(65, loaded.Pets[0].Grievance);
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    /// <summary>
    /// F-02 R3: v4 archive with duplicate PetIds is invalid.
    /// With backup, must fall back to backup and preserve .corrupted.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4DuplicatePetId_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[{"petId":"bear-1","windowLeft":10},{"petId":"bear-1","windowLeft":20}]}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":250,"grievance":55}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsTrue(fromBackup, "Duplicate PetId → corrupted → backup recovery.");
        Assert.AreEqual(250, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(55, loaded.Pets[0].Grievance);
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    /// <summary>
    /// F-02 R3: v4 archive with unknown Personality enum value is invalid.
    /// With backup, must fall back to backup and preserve .corrupted.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4UnknownPersonality_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[{"petId":"bear-1","personality":99,"windowLeft":42}]}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":180,"grievance":60}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsTrue(fromBackup, "Unknown Personality → corrupted → backup recovery.");
        Assert.AreEqual(180, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(60, loaded.Pets[0].Grievance);
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    /// <summary>
    /// F-02 R3: v4 archive with more than MaxPets (2) entries is invalid.
    /// With backup, must fall back to backup and preserve .corrupted.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4ExceedsMaxPets_WithBackup_FallsBackToBackup()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[{"petId":"bear-1","windowLeft":10},{"petId":"bear-2","windowLeft":20},{"petId":"bear-3","windowLeft":30}]}""");
        File.WriteAllText(archive.StateBackupPath,
            """{"schemaVersion":3,"windowLeft":220,"grievance":58}""");

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsTrue(fromBackup, "Exceeds MaxPets → corrupted → backup recovery.");
        Assert.AreEqual(220, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(58, loaded.Pets[0].Grievance);
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    /// <summary>
    /// F-02 R3: Identity validation without backup — corrupted file preserved,
    /// defaults used. No crash.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4DuplicatePetId_NoBackup_PreservesCorruptedAndUsesDefaults()
    {
        var archive = new ArchiveService(_tempDir);
        File.WriteAllText(archive.StateFilePath,
            """{"schemaVersion":4,"pets":[{"petId":"bear-1","windowLeft":10},{"petId":"bear-1","windowLeft":20}]}""");
        // No backup.

        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsFalse(fromBackup);
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
        Assert.AreEqual(100, loaded.Pets[0].WindowLeft, 0.001);
        Assert.IsTrue(File.Exists(archive.StateFilePath + ".corrupted"));
    }

    // ── F-02 R3: 有效单/双个体往返不串改 ──────────────────────────────

    /// <summary>
    /// F-02 R3: A valid single-pet v4 archive must round-trip with
    /// identity and all fields preserved.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4ValidSinglePet_RoundTrip_PreservesIdentity()
    {
        var archive = new ArchiveService(_tempDir);
        var state = new PetStateArchive();
        state.Pets.Add(new PetInstanceArchive
        {
            PetId = "bear-1",
            Personality = Personality.Proactive,
            WindowLeft = 350,
            WindowTop = 450,
            Grievance = 62,
            LastDecayUtc = "2026-11-08T10:00:00Z",
            PendingGrievanceExpression = true,
            GrievanceExpressionDate = "2026-11-08"
        });

        archive.SaveState(state);
        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsFalse(fromBackup);
        Assert.AreEqual(1, loaded.Pets.Count);
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
        Assert.AreEqual(Personality.Proactive, loaded.Pets[0].Personality);
        Assert.AreEqual(350, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(450, loaded.Pets[0].WindowTop, 0.001);
        Assert.AreEqual(62, loaded.Pets[0].Grievance);
        Assert.AreEqual("2026-11-08T10:00:00Z", loaded.Pets[0].LastDecayUtc);
        Assert.IsTrue(loaded.Pets[0].PendingGrievanceExpression);
        Assert.AreEqual("2026-11-08", loaded.Pets[0].GrievanceExpressionDate);
    }

    /// <summary>
    /// F-02 R3: A valid two-pet v4 archive must round-trip with both
    /// identities and independent fields preserved.
    /// </summary>
    [TestMethod]
    [TestCategory("Migration")]
    public void F02_V4ValidTwoPets_RoundTrip_PreservesBothIdentities()
    {
        var archive = new ArchiveService(_tempDir);
        var state = new PetStateArchive();
        state.Pets.Add(new PetInstanceArchive
        {
            PetId = "bear-1",
            Personality = Personality.Proactive,
            WindowLeft = 100,
            Grievance = 55
        });
        state.Pets.Add(new PetInstanceArchive
        {
            PetId = "bear-2",
            Personality = Personality.SlowPaced,
            WindowLeft = 300,
            Grievance = 70
        });

        archive.SaveState(state);
        var (loaded, fromBackup, _) = archive.LoadState();

        Assert.IsFalse(fromBackup);
        Assert.AreEqual(2, loaded.Pets.Count);

        // First pet identity and fields preserved.
        Assert.AreEqual("bear-1", loaded.Pets[0].PetId);
        Assert.AreEqual(Personality.Proactive, loaded.Pets[0].Personality);
        Assert.AreEqual(100, loaded.Pets[0].WindowLeft, 0.001);
        Assert.AreEqual(55, loaded.Pets[0].Grievance);

        // Second pet identity and fields preserved, not crosstalked.
        Assert.AreEqual("bear-2", loaded.Pets[1].PetId);
        Assert.AreEqual(Personality.SlowPaced, loaded.Pets[1].Personality);
        Assert.AreEqual(300, loaded.Pets[1].WindowLeft, 0.001);
        Assert.AreEqual(70, loaded.Pets[1].Grievance);
    }
}
