using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Emotion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

[TestClass]
public class PettingGestureChainTests
{
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

    [STATestMethod]
    public void F03_HeadZone_XBeyondWidth_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // X=200 exceeds 120px width.
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(200, 20)),
                "X beyond image width must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_HeadZone_NegativeX_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(-5, 20)),
                "Negative X must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_HeadZone_NegativeY_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(60, -10)),
                "Negative Y must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_HeadZone_XAtZero_Transparent_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // X=0 is within the image rectangle but on a transparent pixel
            // (opaque bounds start at X≈30 for the default character).
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(0, 20)),
                "X=0 is in the head rectangle but on a transparent pixel — must NOT be head zone (R5 Alpha check).");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_HeadZone_XAtWidth_Transparent_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // X=120 = image width, but on a transparent pixel.
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(120, 20)),
                "X=width is in the head rectangle but on a transparent pixel — must NOT be head zone (R5 Alpha check).");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_HeadZone_OpaquePixelInHead_IsHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // (60, 20) is an opaque pixel in the upper 1/3 of the default character.
            Assert.IsTrue(window.IsInHeadZone(new System.Windows.Point(60, 20)),
                "Opaque pixel within head rectangle must be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_R4_NegativeXZeroY_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(-1, 0)),
                "Negative X with Y=0 must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_R4_VeryLargeX_SmallY_NotHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(10000, 20)),
                "Very large X with small Y must NOT be in head zone.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F03_R5_TransparentInHeadRect_NotHead_AdjacentOpaque_IsHead()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // (60, 5) is in the head rectangle (Y=5 < 40) but transparent.
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(60, 5)),
                "Transparent pixel within head rectangle must NOT be head zone (R5).");

            // (60, 20) is in the head rectangle AND on an opaque pixel.
            Assert.IsTrue(window.IsInHeadZone(new System.Windows.Point(60, 20)),
                "Adjacent opaque pixel within head rectangle must be head zone.");
        }
        finally
        {
            window.Close();
        }
    }

}