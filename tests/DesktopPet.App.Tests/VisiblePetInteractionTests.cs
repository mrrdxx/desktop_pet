using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.App.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

[TestClass]
public class VisiblePetInteractionTests
{
    [STATestMethod]
    public void AlphaGeometry_TransparentPixelIsNotBody_OpaquePixelIs()
    {
        var pixels = new byte[8 * 8 * 4];
        for (int y = 2; y < 6; y++)
            for (int x = 2; x < 6; x++) pixels[(y * 8 + x) * 4 + 3] = 255;
        var bmp = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 32);
        var image = new Image { Width = 8, Height = 8, Stretch = Stretch.None, Source = bmp };
        Assert.IsFalse(PetImageGeometry.Contains(image, new Point(0, 0)));
        Assert.IsTrue(PetImageGeometry.Contains(image, new Point(4, 4)));
        Assert.IsFalse(PetImageGeometry.Contains(image, new Point(8, 4)));
        Assert.AreEqual(new Rect(2, 2, 4, 4), PetImageGeometry.OpaqueBounds(image));
    }

    [STATestMethod]
    public void RealUpdate_TransparentCornerDoesNotTrigger_BodyDoes()
    {
        var window = new DesktopPet.App.MainWindow();
        try
        {
            double now = 0;
            window.TestGetElapsedMs = () => now;
            window.TestGetCursorPos = () => (104, 104);
            var tick = typeof(DesktopPet.App.MainWindow).GetMethod("OnUpdateTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (int i = 0; i < 55; i++) { tick.Invoke(window, [null, EventArgs.Empty]); now += 33; }
            Assert.AreEqual("idle", window.CurrentAction, "Transparent corner cannot accrue hover dwell");
            Assert.IsFalse(window.HoverDetector.HasTriggered);
            window.TestGetCursorPos = () => (160, 165);
            for (int i = 0; i < 55; i++) { tick.Invoke(window, [null, EventArgs.Empty]); now += 33; }
            Assert.AreEqual("curious", window.CurrentAction, "Actual body still supports hover");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void InteractionBounds_UseOpaqueBody_NotWholeTransparentWindow()
    {
        var window = new DesktopPet.App.MainWindow();
        try
        {
            var method = typeof(DesktopPet.App.MainWindow).GetMethod("GetPetRectPhysicalPx", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var rect = (DesktopPet.Core.Input.PetRectPx)method.Invoke(window, null)!;
            Assert.IsTrue(rect.Left > window.Left && rect.Top > window.Top);
            Assert.IsTrue(rect.Right < window.Left + window.Width && rect.Bottom < window.Top + window.Height);
        }
        finally { window.Close(); }
    }
}
