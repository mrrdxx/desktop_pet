using System.IO;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

[TestClass]
public class ArchiveVersionProtectionTests
{
    private string _directory = null!;
    private const string FutureState = "{\"schemaVersion\":99,\"windowLeft\":{\"newFormat\":1},\"futurePayload\":42}";
    private const string FutureSettings = "{\"schemaVersion\":99,\"animationSpeed\":{\"newFormat\":2},\"futurePayload\":77}";

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DesktopPet-Version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public void FutureStateWithChangedFieldType_IsReadOnlyAndNeverTreatedAsCorrupt()
    {
        var archive = new ArchiveService(_directory);
        File.WriteAllText(archive.StateFilePath, FutureState);
        var loaded = archive.LoadState();

        Assert.IsTrue(loaded.IsUnknownVersion);
        Assert.IsTrue(archive.IsStateReadOnly);
        Assert.AreEqual(99, loaded.State.SchemaVersion);
        Assert.IsFalse(File.Exists(archive.StateFilePath + ".corrupted"));
        using (var saves = new SaveCoordinator(archive))
        {
            // Simulate normal shutdown with a replacement v1 state snapshot.
            saves.MarkStateDirty(new PetStateArchive { WindowLeft = 350, WindowTop = 260 });
        }
        Assert.AreEqual(FutureState, File.ReadAllText(archive.StateFilePath));
        Assert.IsFalse(File.Exists(archive.StateBackupPath));
    }

    [TestMethod]
    public void FutureSettingsWithChangedFieldType_SurvivesNormalCoordinatorFlush()
    {
        var archive = new ArchiveService(_directory);
        File.WriteAllText(archive.SettingsFilePath, FutureSettings);
        var loaded = archive.LoadSettings();
        Assert.IsTrue(loaded.IsUnknownVersion);
        Assert.IsTrue(archive.IsSettingsReadOnly);
        Assert.IsFalse(File.Exists(archive.SettingsFilePath + ".corrupted"));
        using (var saves = new SaveCoordinator(archive))
        {
            saves.MarkSettingsDirty(new PetSettingsArchive { AnimationSpeed = 2 });
        }
        Assert.AreEqual(FutureSettings, File.ReadAllText(archive.SettingsFilePath));
        Assert.IsFalse(File.Exists(archive.SettingsBackupPath));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FreshServiceCannotOverwriteFutureFileWithoutLoadingIt(bool settings)
    {
        var archive = new ArchiveService(_directory);
        var path = settings ? archive.SettingsFilePath : archive.StateFilePath;
        var original = settings ? FutureSettings : FutureState;
        File.WriteAllText(path, original);
        if (settings)
            archive.SaveSettings(new PetSettingsArchive());
        else
            archive.SaveState(new PetStateArchive());
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.IsFalse(File.Exists(path + ".tmp"));
    }

    [TestMethod]
    public void EditingCompatibleFutureDtoVersionDoesNotDisableFileProtection()
    {
        var archive = new ArchiveService(_directory);
        const string original = "{\"schemaVersion\":99,\"windowLeft\":500,\"futurePayload\":42}";
        File.WriteAllText(archive.StateFilePath, original);
        var loaded = archive.LoadState();
        Assert.AreEqual(500, loaded.State.WindowLeft, 0.001);
        loaded.State.SchemaVersion = 1;
        archive.SaveState(loaded.State);
        Assert.AreEqual(original, File.ReadAllText(archive.StateFilePath));
    }

    [TestMethod]
    public void FutureBackupRetainsReadOnlyProtectionAfterCorruptPrimaryRecovery()
    {
        var archive = new ArchiveService(_directory);
        File.WriteAllText(archive.StateFilePath, "{ broken");
        File.WriteAllText(archive.StateBackupPath, FutureState);
        var loaded = archive.LoadState();
        Assert.IsTrue(loaded.FromBackup);
        Assert.IsTrue(loaded.IsUnknownVersion);
        archive.SaveState(new PetStateArchive());
        Assert.IsFalse(File.Exists(archive.StateFilePath));
        Assert.AreEqual(FutureState, File.ReadAllText(archive.StateBackupPath));
        Assert.AreEqual("{ broken", File.ReadAllText(archive.StateFilePath + ".corrupted"));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ReadOnlyProtectionIsIndependentForStateAndSettings(bool futureSettings)
    {
        var archive = new ArchiveService(_directory);
        if (futureSettings)
        {
            File.WriteAllText(archive.SettingsFilePath, FutureSettings);
            archive.LoadSettings();
            archive.SaveState(new PetStateArchive { WindowLeft = 420 });
            Assert.AreEqual(420, archive.LoadState().State.WindowLeft, 0.001);
            Assert.AreEqual(FutureSettings, File.ReadAllText(archive.SettingsFilePath));
        }
        else
        {
            File.WriteAllText(archive.StateFilePath, FutureState);
            archive.LoadState();
            archive.SaveSettings(new PetSettingsArchive { AnimationSpeed = 1.7 });
            Assert.AreEqual(1.7, archive.LoadSettings().Settings.AnimationSpeed, 0.001);
            Assert.AreEqual(FutureState, File.ReadAllText(archive.StateFilePath));
        }
    }
}
