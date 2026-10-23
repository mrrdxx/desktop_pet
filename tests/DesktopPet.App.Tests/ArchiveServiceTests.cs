using System.IO;
using System.Text.Json;
using DesktopPet.App.Persistence;
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
}
