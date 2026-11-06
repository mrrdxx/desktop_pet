using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

[TestClass]
public class PettingDetectionTests
{
    private const double PetSize = 120;

    private static MainWindow CreateMainWindowOrFail()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            Assert.Fail($"MainWindow construction failed: {ex.Message}");
            throw;
        }
    }

    private static void SetField<T>(object obj, string name, T value)
    {
        var field = typeof(MainWindow).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail($"Field '{name}' not found.");
        field!.SetValue(obj, value);
    }

    private static void InvokeMethod(object obj, string name, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
            Assert.Fail($"Method '{name}' not found.");
        method!.Invoke(obj, args);
    }

    private static void InvokeTick(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("OnUpdateTick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
            Assert.Fail("OnUpdateTick method not found.");
        method.Invoke(window, [null!, EventArgs.Empty]);
    }

    // ── Head zone detection ────────────────────────────────────────────

    [STATestMethod]
    public void Petting_HeadZone_UpperThird_IsHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Upper third of 120px image: Y <= 40 is head.
            // F-03 R5: Must use an opaque pixel — (60, 20) is opaque in the default character.
            var headPoint = new System.Windows.Point(60, 20);
            Assert.IsTrue(window.IsInHeadZone(headPoint),
                "Opaque point in upper 1/3 should be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Petting_BodyZone_LowerTwoThirds_IsNotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Lower 2/3 of 120px image: Y > 40 is body.
            var bodyPoint = new System.Windows.Point(60, 80);
            Assert.IsFalse(window.IsInHeadZone(bodyPoint),
                "Point in lower 2/3 should NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Petting_HeadZone_Boundary_AtOneThird()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Exactly at 1/3 boundary (Y = 40 for 120px) should be head.
            var boundaryPoint = new System.Windows.Point(60, 40);
            Assert.IsTrue(window.IsInHeadZone(boundaryPoint),
                "Point exactly at 1/3 boundary should be in head zone (<=).");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Petting candidate interrupts hover ──────────────────────────

    [STATestMethod]
    public void Petting_CandidateState_ResetsHover()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            // Build active hover before entering the petting gesture.
            InvokeTick(window);
            clockMs += 33;
            Assert.IsTrue(window.HoverDetector.IsHovering,
                "Hover should be active before petting candidate.");

            // Enter petting candidate state (simulating head press).
            SetField(window, "_isPettingCandidate", true);

            // Tick during petting — should reset hover.
            InvokeTick(window);
            clockMs += 33;

            Assert.IsFalse(window.HoverDetector.IsHovering,
                "HoverDetector must be reset during petting candidate.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Petting candidate state recovery ───────────────────────────────

    [STATestMethod]
    public void Petting_CandidateState_RecoveredAfterEnd()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => (160, 160);

            // Enter petting candidate.
            SetField(window, "_isPettingCandidate", true);

            // End petting.
            InvokeMethod(window, "EndPetting");

            // Tick after petting ends — hover should resume.
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.HoverDetector.IsHovering,
                "Hover dwell must restart after petting ends.");
        }
        finally
        {
            window.Close();
        }
    }

    // ── OnClosed cleans up petting state ───────────────────────────────

    [STATestMethod]
    public void Petting_OnClosed_CleansUpPettingState()
    {
        var window = CreateMainWindowOrFail();
        // Enter petting candidate before close.
        SetField(window, "_isPettingCandidate", true);
        // Close should clean up without throwing.
        window.Close();
        // If we reach here, no exception was thrown — pass.
        Assert.IsFalse(window.IsPettingCandidate, "Close must clear the head gesture.");
    }
}
