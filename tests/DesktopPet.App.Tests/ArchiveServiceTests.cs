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
            WindowLeft = 250.5,
            WindowTop = 180.3,
            SavedDeviceName = @"\\.\DISPLAY1",
            IsPaused = true
        };

        archive.SaveState(state);

        // Verify file exists.
        Assert.IsTrue(File.Exists(archive.StateFilePath));

        var (loaded, fromBackup, unknown) = archive.LoadState();

        Assert.AreEqual(250.5, loaded.WindowLeft, 0.001);
        Assert.AreEqual(180.3, loaded.WindowTop, 0.001);
        Assert.AreEqual(@"\\.\DISPLAY1", loaded.SavedDeviceName);
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
        archive.SaveState(new PetStateArchive { WindowLeft = 100, WindowTop = 200 });

        // Save again with different values; previous becomes .bak.
        archive.SaveState(new PetStateArchive { WindowLeft = 300, WindowTop = 400 });

        // Corrupt the primary file.
        File.WriteAllText(archive.StateFilePath, "{ \"schemaVersion\": 1, \"windowLeft\": tru");

        var (loaded, fromBackup, _) = archive.LoadState();

        // Should have loaded from backup with the first save's values.
        Assert.IsTrue(fromBackup);
        Assert.AreEqual(100, loaded.WindowLeft, 0.001);
        Assert.AreEqual(200, loaded.WindowTop, 0.001);

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
        Assert.AreEqual(new PetStateArchive().WindowLeft, loaded.WindowLeft, 0.001);
        Assert.AreEqual(new PetStateArchive().WindowTop, loaded.WindowTop, 0.001);

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
              "windowLeft": 500,
              "windowTop": 600,
              "savedDeviceName": null,
              "lastSavedUtc": null,
              "isPaused": false
            }
            """;
        File.WriteAllText(archive.StateFilePath, higherVersionJson);

        // Load it — should detect unknown version.
        var (loaded, _, unknown) = archive.LoadState();
        Assert.IsTrue(unknown);
        Assert.AreEqual(99, loaded.SchemaVersion);
        Assert.AreEqual(500, loaded.WindowLeft, 0.001);

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

        var state = new PetStateArchive { WindowLeft = 42, WindowTop = 84 };
        coordinator.MarkStateDirty(state);

        // Flush should save immediately without waiting for debounce.
        coordinator.Flush();

        Assert.IsTrue(File.Exists(archive.StateFilePath));

        var (loaded, _, _) = archive.LoadState();
        Assert.AreEqual(42, loaded.WindowLeft, 0.001);
        Assert.AreEqual(84, loaded.WindowTop, 0.001);
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
            coordinator.MarkStateDirty(new PetStateArchive { WindowLeft = i, WindowTop = i });
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
        Assert.AreEqual(9, loaded.WindowLeft, 0.001);
    }

    /// <summary>Safe write preserves backup of previous valid file.</summary>
    [TestMethod]
    public void SaveState_SecondSave_CreatesBackupOfPrevious()
    {
        var archive = new ArchiveService(_tempDir);

        archive.SaveState(new PetStateArchive { WindowLeft = 111 });
        Assert.IsFalse(File.Exists(archive.StateBackupPath), "First save should not have a backup.");

        archive.SaveState(new PetStateArchive { WindowLeft = 222 });
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

        Assert.AreEqual(new PetStateArchive().WindowLeft, state.WindowLeft, 0.001);
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

        // Position fields preserved.
        Assert.AreEqual(150, loaded.WindowLeft, 0.001);
        Assert.AreEqual(250, loaded.WindowTop, 0.001);
        Assert.IsFalse(unknown);

        // Emotion fields fall back to defaults.
        Assert.AreEqual(50, loaded.Grievance);
        Assert.IsFalse(loaded.PendingGrievanceExpression);
        Assert.IsNull(loaded.GrievanceExpressionDate);

        // lastDecayUtc should fall back to lastSavedUtc per PRODUCT R-02.
        Assert.AreEqual("2026-10-20T08:00:00.0000000Z", loaded.LastDecayUtc);
    }

    /// <summary>v2 file with emotion values loads correctly.</summary>
    [TestMethod]
    public void LoadState_V2File_PreservesEmotionFields()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            WindowLeft = 100,
            WindowTop = 200,
            Grievance = 85,
            LastDecayUtc = "2026-10-22T12:00:00.0000000Z",
            PendingGrievanceExpression = true,
            GrievanceExpressionDate = "2026-10-22"
        };

        archive.SaveState(state);
        var (loaded, _, unknown) = archive.LoadState();

        Assert.IsFalse(unknown);
        Assert.AreEqual(PetStateArchive.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.AreEqual(85, loaded.Grievance);
        Assert.AreEqual("2026-10-22T12:00:00.0000000Z", loaded.LastDecayUtc);
        Assert.IsTrue(loaded.PendingGrievanceExpression);
        Assert.AreEqual("2026-10-22", loaded.GrievanceExpressionDate);
    }

    /// <summary>Restart with pending expression does not re-award or lose pending state.</summary>
    [TestMethod]
    public void LoadState_V2PendingExpression_SurvivesRestart()
    {
        var archive = new ArchiveService(_tempDir);

        var state = new PetStateArchive
        {
            Grievance = 82,
            PendingGrievanceExpression = true,
            GrievanceExpressionDate = "2026-10-22",
            LastDecayUtc = "2026-10-22T18:00:00.0000000Z"
        };

        archive.SaveState(state);
        var (loaded, _, _) = archive.LoadState();

        // Pending state and date preserved across restart.
        Assert.IsTrue(loaded.PendingGrievanceExpression);
        Assert.AreEqual("2026-10-22", loaded.GrievanceExpressionDate);
        Assert.AreEqual(82, loaded.Grievance);
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
            Grievance = 60,
            LastDecayUtc = "2026-11-04T10:00:00.0000000Z",
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
        Assert.AreEqual(75, loaded.Grievance);
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
}
