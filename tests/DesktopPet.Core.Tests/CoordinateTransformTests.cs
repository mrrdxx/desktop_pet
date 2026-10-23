using DesktopPet.Core.Geometry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Tests for coordinate transforms between physical pixels and DIP,
/// and between foot-center world position and window top-left.
/// </summary>
[TestClass]
public class CoordinateTransformTests
{
    [TestMethod]
    public void PixelsToDip_100PercentScale_IsIdentity()
    {
        var (dipX, dipY) = CoordinateTransform.PixelsToDip(200, 100, 1.0, 1.0);
        Assert.AreEqual(200, dipX, 0.001);
        Assert.AreEqual(100, dipY, 0.001);
    }

    [TestMethod]
    public void PixelsToDip_150PercentScale_DividesByScale()
    {
        var (dipX, dipY) = CoordinateTransform.PixelsToDip(300, 150, 1.5, 1.5);
        Assert.AreEqual(200, dipX, 0.001);
        Assert.AreEqual(100, dipY, 0.001);
    }

    [TestMethod]
    public void DipToPixels_150PercentScale_MultipliesByScale()
    {
        var (pxX, pxY) = CoordinateTransform.DipToPixels(200, 100, 1.5, 1.5);
        Assert.AreEqual(300, pxX, 0.001);
        Assert.AreEqual(150, pxY, 0.001);
    }

    [TestMethod]
    public void FootCenterToWindowTopLeft_RoundTrip_100Percent()
    {
        var foot = new PetWorldPosition(500, 700);
        var (leftDip, topDip) = CoordinateTransform.FootCenterToWindowTopLeft(
            foot, 120, 120, 1.0, 1.0);

        // left = 500 - 60 = 440, top = 700 - 120 = 580
        Assert.AreEqual(440, leftDip, 0.001);
        Assert.AreEqual(580, topDip, 0.001);

        // Round-trip back
        var recovered = CoordinateTransform.WindowTopLeftToFootCenter(
            leftDip, topDip, 120, 120, 1.0, 1.0);
        Assert.AreEqual(500, recovered.FootCenterX, 0.001);
        Assert.AreEqual(700, recovered.FootCenterY, 0.001);
    }

    [TestMethod]
    public void FootCenterToWindowTopLeft_150Percent_KeepsFootAtSamePixel()
    {
        // At 150%, a 120 DIP window is 180 physical pixels wide.
        // Foot at pixel (500, 700): left = 500 - 90 = 410 px, top = 700 - 180 = 520 px
        // Convert to DIP: 410/1.5 = 273.33, 520/1.5 = 346.67
        var foot = new PetWorldPosition(500, 700);
        var (leftDip, topDip) = CoordinateTransform.FootCenterToWindowTopLeft(
            foot, 120, 120, 1.5, 1.5);

        Assert.AreEqual(410.0 / 1.5, leftDip, 0.01);
        Assert.AreEqual(520.0 / 1.5, topDip, 0.01);

        // Round-trip: converting back should give the same foot position in pixels
        var recovered = CoordinateTransform.WindowTopLeftToFootCenter(
            leftDip, topDip, 120, 120, 1.5, 1.5);
        Assert.AreEqual(500, recovered.FootCenterX, 0.01);
        Assert.AreEqual(700, recovered.FootCenterY, 0.01);
    }

    [TestMethod]
    public void PixelsToDip_ZeroScale_FallsBackToRawValue()
    {
        // Defensive: if DPI scale is 0 (shouldn't happen), don't divide by zero
        var (dipX, dipY) = CoordinateTransform.PixelsToDip(200, 100, 0, 0);
        Assert.AreEqual(200, dipX, 0.001);
        Assert.AreEqual(100, dipY, 0.001);
    }
}
