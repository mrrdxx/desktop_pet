using System.IO;
using System.Text.Json;
using System.Windows;
using DesktopPet.App;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Emotion;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

// D-013: legacy input is accepted, but the removed system cannot survive a save.
[TestClass]
public class AffinityRemovalTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"DesktopPetRemoval_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    [DataRow("95")]
    [DataRow("null")]
    [DataRow("{\"obsolete\":true}")]
    public void LegacyV2_LoadMapSaveReload_DropsRemovedField_PreservesOtherState(string legacyValue)
    {
        var service = new ArchiveService(_directory);
        string json = $$"""
        {
          "schemaVersion": 2, "affinity": {{legacyValue}},
          "windowLeft": 321, "windowTop": 456,
          "savedDeviceName": "test-monitor", "isPaused": true,
          "grievance": 85, "lastDecayUtc": "2026-10-25T12:00:00Z",
          "pendingGrievanceExpression": true, "grievanceExpressionDate": "2026-10-25"
        }
        """;
        File.WriteAllText(service.StateFilePath, json);
        var (state, backup, unknown) = service.LoadState();
        Assert.IsFalse(backup);
        Assert.IsFalse(unknown);
        Assert.AreEqual(json, File.ReadAllText(service.StateFilePath), "Loading must not rewrite user data.");

        var (accumulator, expression, _) = App.MapArchiveToEmotionObjects(state,
            new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
        Assert.AreEqual(85, accumulator.GrievanceInt);
        Assert.IsTrue(expression.ShouldExpress("2026-10-26"));
        accumulator.AddGrievance(5);
        App.MapEmotionObjectsToArchive(state, accumulator, expression);
        service.SaveState(state);

        using var saved = JsonDocument.Parse(File.ReadAllText(service.StateFilePath));
        Assert.IsFalse(saved.RootElement.TryGetProperty("affinity", out _));
        var (reloaded, _, _) = new ArchiveService(_directory).LoadState();
        Assert.AreEqual(PetStateArchive.CurrentSchemaVersion, reloaded.SchemaVersion);
        Assert.AreEqual(321.0, reloaded.WindowLeft);
        Assert.AreEqual(456.0, reloaded.WindowTop);
        Assert.AreEqual("test-monitor", reloaded.SavedDeviceName);
        Assert.IsTrue(reloaded.IsPaused);
        Assert.AreEqual(90, reloaded.Grievance);
        Assert.AreEqual(accumulator.LastDecayUtc.ToString("o"), reloaded.LastDecayUtc);
        Assert.IsTrue(reloaded.PendingGrievanceExpression);
        Assert.AreEqual("2026-10-25", reloaded.GrievanceExpressionDate);
    }

    [TestMethod]
    public void FutureVersion_WithRemovedField_RemainsReadOnly()
    {
        var service = new ArchiveService(_directory);
        const string json = "{\"schemaVersion\":99,\"affinity\":{\"future\":true},\"grievance\":{\"future\":true}}";
        File.WriteAllText(service.StateFilePath, json);
        var (state, _, unknown) = service.LoadState();
        Assert.IsTrue(unknown);
        state.Grievance = 50;
        service.SaveState(state);
        Assert.AreEqual(json, File.ReadAllText(service.StateFilePath));
        Assert.IsFalse(File.Exists(service.StateBackupPath));
        Assert.IsFalse(File.Exists(service.StateFilePath + ".corrupted"));
    }

    [TestMethod]
    public void CorruptPrimary_LegacyBackup_RecoversWithoutReintroducingRemovedField()
    {
        var service = new ArchiveService(_directory);
        const string corrupt = "{\"schemaVersion\":2,";
        File.WriteAllText(service.StateFilePath, corrupt);
        File.WriteAllText(service.StateBackupPath,
            "{\"schemaVersion\":2,\"affinity\":88,\"grievance\":74,\"windowLeft\":212}");
        var (state, backup, unknown) = service.LoadState();
        Assert.IsTrue(backup);
        Assert.IsFalse(unknown);
        Assert.AreEqual(74, state.Grievance);
        Assert.AreEqual(212.0, state.WindowLeft);
        Assert.AreEqual(corrupt, File.ReadAllText(service.StateFilePath + ".corrupted"));
        service.SaveState(state);
        using var saved = JsonDocument.Parse(File.ReadAllText(service.StateFilePath));
        Assert.IsFalse(saved.RootElement.TryGetProperty("affinity", out _));
        Assert.AreEqual(74, saved.RootElement.GetProperty("grievance").GetInt32());
    }

    [TestMethod]
    public void RuntimeAndArchive_ExposeNoRemovedMembers()
    {
        var types = new[] { typeof(EmotionState), typeof(EmotionDecay),
            typeof(EmotionDecayAccumulator), typeof(PetStateArchive), typeof(MainWindow) };
        var members = types.SelectMany(type => type.GetMembers(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static));
        string[] removedNames = ["AddPetIncrement", "_pettingStartMs", "_pettingAccumulatedMs",
            "_pettingCooldownUntilMs", "ProcessPettingMove"];
        Assert.IsFalse(members.Any(member => member.Name.Contains("Affinity", StringComparison.OrdinalIgnoreCase)
            || removedNames.Contains(member.Name)), "Removed state, APIs and scoring timers must be absent.");
    }

    [STATestMethod]
    public void RepeatedHeadMicroStrokes_DoNotChangePersistentState()
    {
        var state = new PetStateArchive { Grievance = 70, LastDecayUtc = "2026-10-25T12:00:00Z" };
        var (accumulator, expression, _) = App.MapArchiveToEmotionObjects(state);
        App.MapEmotionObjectsToArchive(state, accumulator, expression);
        var before = JsonSerializer.Serialize(state);
        var window = new MainWindow { TestEmotionAccumulator = accumulator, TestGrievanceExpression = expression };
        try
        {
            for (int round = 0; round < 3; round++)
            {
                var head = new Point(60, 25);
                var press = new Point(window.Left + head.X, window.Top + head.Y);
                Assert.IsTrue(window.BeginCapturedPointerInteraction(head, press));
                for (int i = 0; i < 25; i++)
                {
                    var delta = new Vector(i % 2 * 2, 0);
                    window.ProcessCapturedPointerMove(head + delta, press + delta, true, round * 2000 + i * 50);
                }
                Assert.IsTrue(window.IsPettingCandidate);
                Assert.IsFalse(window.IsDragging);
                window.EndCapturedPointerInteraction();
                Assert.IsFalse(window.IsPettingCandidate);
            }
            App.MapEmotionObjectsToArchive(state, accumulator, expression);
            Assert.AreEqual(before, JsonSerializer.Serialize(state));
            Assert.AreEqual(70.0, accumulator.GrievanceFloat);
            Assert.IsFalse(expression.IsPending);
        }
        finally { window.Close(); }
    }
}
