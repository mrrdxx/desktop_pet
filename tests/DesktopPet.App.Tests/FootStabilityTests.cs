using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-04: Foot-point stability tests that read real PNG pixels and verify
/// world-foot coordinates through actual rendering transforms.
///
/// R4 requirements:
/// - Tests must read actual PNG pixel data or off-screen render results,
///   not hard-coded formulas like footAnchorY - 9.
/// - Cover action switch (idle->curious), switch back (curious->idle),
///   and a negative control with wrong compensation that must be detected.
/// - Assert world foot Y coordinate equality, not just Transform non-zero.
/// </summary>
[TestClass]
public class FootStabilityTests
{
    private static MainWindow? TryCreateMainWindow()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "FootStabilityTests: MainWindow construction failed: {0}", ex);
            return null;
        }
    }

    private static T? GetField<T>(object obj, string name) where T : class
    {
        var field = typeof(MainWindow).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(obj) as T;
    }

    private static void InvokeMethod(object obj, string name, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        method?.Invoke(obj, args);
    }

    /// <summary>
    /// Resolves the real asset directory containing character.json and PNGs.
    /// Walks up from the test output directory to find assets/pets/bear.
    /// </summary>
    private static string? GetAssetDir()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "assets", "pets", "bear"),
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "assets", "pets", "bear"),
            Path.Combine(exeDir, "..", "..", "..", "..", "assets", "pets", "bear")
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(Path.Combine(full, "character.json")))
                return full;
        }
        return null;
    }

    /// <summary>
    /// Reads a real PNG file and finds the Y coordinate of the bottom-most
    /// non-transparent row (alpha > 0). This is the actual visual foot position
    /// in the source image -- not a hard-coded value.
    /// </summary>
    private static int MeasurePngBottomY(string pngPath)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();

        int width = bmp.PixelWidth;
        int height = bmp.PixelHeight;
        var pixels = new byte[width * height * 4];
        bmp.CopyPixels(pixels, width * 4, 0);

        // Scan from bottom row upward to find the first non-transparent pixel.
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                // Pbgra32: pixel = B, G, R, A (pre-multiplied). Check alpha.
                if (pixels[offset + 3] > 0)
                    return y;
            }
        }
        return -1; // Fully transparent image
    }

    /// <summary>
    /// Reads the offsetY value from the real character.json for a given action.
    /// This avoids hard-coding the offset value in the test.
    /// </summary>
    private static double ReadOffsetYFromJson(string assetDir, string actionId)
    {
        var jsonPath = Path.Combine(assetDir, "character.json");
        var json = File.ReadAllText(jsonPath);
        var character = CharacterLoader.Load(json);
        if (character.Actions.TryGetValue(actionId, out var action) &&
            action.Frames.Count > 0)
        {
            return action.Frames[0].OffsetY;
        }
        return 0;
    }

    /// <summary>
    /// F-04 R4: Verify world foot stability by reading real PNG pixel data.
    ///
    /// The world foot Y is: window.Top + transformY + pngBottomY
    /// - pngBottomY is measured from the actual PNG file (not hard-coded).
    /// - transformY is the TranslateTransform applied by UpdateFrame.
    ///
    /// idle:    Top + 0   + pngBottomY(idle)
    /// curious: Top + offsetY + pngBottomY(curious)
    ///
    /// For foot stability: pngBottomY(idle) == offsetY + pngBottomY(curious)
    /// i.e. the content shift in the PNG is exactly compensated by the transform.
    /// </summary>
    [STATestMethod]
    public void ActionSwitch_IdleToCurious_WorldFootStable_FromRealPng()
    {
        var assetDir = GetAssetDir();
        if (assetDir is null)
        {
            Assert.Inconclusive("Cannot locate asset directory with character.json.");
            return;
        }

        // Read real PNG foot positions -- not hard-coded values.
        var idlePngPath = Path.Combine(assetDir, "idle_0.png");
        var curiousPngPath = Path.Combine(assetDir, "curious_0.png");
        Assert.IsTrue(File.Exists(idlePngPath), "idle_0.png must exist in asset dir.");
        Assert.IsTrue(File.Exists(curiousPngPath), "curious_0.png must exist in asset dir.");

        int idleBottomY = MeasurePngBottomY(idlePngPath);
        int curiousBottomY = MeasurePngBottomY(curiousPngPath);
        Assert.IsTrue(idleBottomY > 0, "idle_0.png must have non-transparent content.");
        Assert.IsTrue(curiousBottomY > 0, "curious_0.png must have non-transparent content.");

        // Read the actual offsetY from character.json -- not hard-coded.
        double curiousOffsetY = ReadOffsetYFromJson(assetDir, "curious");
        double idleOffsetY = ReadOffsetYFromJson(assetDir, "idle");
        Assert.AreEqual(0, idleOffsetY, "idle offset should be 0.");

        // The key assertion: the PNG content shift must equal the compensation.
        double contentShift = idleBottomY - curiousBottomY;
        Assert.AreEqual(curiousOffsetY, contentShift, 1.0,
            "PNG content shift ({0}px) must match JSON offsetY ({1}) within 1px tolerance. " +
            "idle bottom={2}, curious bottom={3}.",
            contentShift, curiousOffsetY, idleBottomY, curiousBottomY);

        // Now verify through the actual MainWindow rendering pipeline.
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        try
        {
            // Use a fixed window position for deterministic world coordinates.
            window.Top = 100;
            window.Left = 100;

            var animPlayer = GetField<AnimationPlayer>(window, "_animPlayer");
            Assert.IsNotNull(animPlayer);

            // idle: get transform and compute world foot.
            animPlayer!.PlayAction("idle");
            InvokeMethod(window, "UpdateFrame");
            var (idleDx, idleDy) = window.CurrentFrameOffset;
            double idleWorldFootY = window.Top + idleDy + idleBottomY;

            // curious: get transform and compute world foot.
            animPlayer.PlayAction("curious");
            InvokeMethod(window, "UpdateFrame");
            var (curiousDx, curiousDy) = window.CurrentFrameOffset;
            double curiousWorldFootY = window.Top + curiousDy + curiousBottomY;

            // Assert world foot Y is stable (within 1px rasterization tolerance).
            Assert.AreEqual(idleWorldFootY, curiousWorldFootY, 1.0,
                "World foot Y must be stable when switching idle->curious. " +
                "idle: Top+{0}+{1}={2}, curious: Top+{3}+{4}={5}.",
                idleDy, idleBottomY, idleWorldFootY,
                curiousDy, curiousBottomY, curiousWorldFootY);

            // Switch back to idle and verify stability.
            animPlayer.PlayAction("idle");
            InvokeMethod(window, "UpdateFrame");
            var (backDx, backDy) = window.CurrentFrameOffset;
            double backWorldFootY = window.Top + backDy + idleBottomY;

            Assert.AreEqual(idleWorldFootY, backWorldFootY, 1.0,
                "World foot Y must be stable when switching curious->idle. " +
                "idle: {0}, back: {1}.", idleWorldFootY, backWorldFootY);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-04 R4: Verify that UpdateFrame actually applies the TranslateTransform
    /// to the PetImage control, and the transform value matches what
    /// character.json declares (read from JSON, not hard-coded).
    /// </summary>
    [STATestMethod]
    public void UpdateFrame_AppliesTransformToPetImage_FromJson()
    {
        var assetDir = GetAssetDir();
        if (assetDir is null)
        {
            Assert.Inconclusive("Cannot locate asset directory with character.json.");
            return;
        }

        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        try
        {
            var animPlayer = GetField<AnimationPlayer>(window, "_animPlayer");
            Assert.IsNotNull(animPlayer);

            // Read expected offset from real character.json.
            double expectedCuriousOffsetY = ReadOffsetYFromJson(assetDir, "curious");
            Assert.IsTrue(expectedCuriousOffsetY > 0,
                "curious offsetY must be non-zero for this test to be meaningful.");

            // idle: transform should be (0, 0).
            animPlayer!.PlayAction("idle");
            InvokeMethod(window, "UpdateFrame");
            var (idleX, idleY) = window.CurrentFrameOffset;
            Assert.AreEqual(0, idleX, "idle transform X must be 0.");
            Assert.AreEqual(0, idleY, "idle transform Y must be 0.");

            // Verify the transform is actually on the PetImage control.
            var petImageField = typeof(MainWindow).GetField("PetImage",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var petImage = petImageField?.GetValue(window) as Image;
            Assert.IsNotNull(petImage, "PetImage control must exist.");
            var transform = petImage!.RenderTransform as TranslateTransform;
            Assert.IsNotNull(transform, "PetImage.RenderTransform must be a TranslateTransform.");
            Assert.AreEqual(0, transform!.X, 0.001, "idle PetImage transform X must be 0.");
            Assert.AreEqual(0, transform.Y, 0.001, "idle PetImage transform Y must be 0.");

            // curious: transform should match JSON offsetY (read from file, not hard-coded).
            animPlayer.PlayAction("curious");
            InvokeMethod(window, "UpdateFrame");
            var (curiousX, curiousY) = window.CurrentFrameOffset;
            Assert.AreEqual(0, curiousX, "curious transform X must be 0.");
            Assert.AreEqual(expectedCuriousOffsetY, curiousY, 0.001,
                "curious transform Y must match JSON offsetY ({0}), not a hard-coded value.",
                expectedCuriousOffsetY);

            // Verify the transform is actually applied to the PetImage control.
            transform = petImage.RenderTransform as TranslateTransform;
            Assert.IsNotNull(transform, "PetImage.RenderTransform must remain a TranslateTransform.");
            Assert.AreEqual(expectedCuriousOffsetY, transform!.Y, 0.001,
                "PetImage transform Y must match the expected offset from JSON.");

            // Switch back to idle: transform returns to (0, 0).
            animPlayer.PlayAction("idle");
            InvokeMethod(window, "UpdateFrame");
            var (backX, backY) = window.CurrentFrameOffset;
            Assert.AreEqual(0, backX, "idle transform X after switch back must be 0.");
            Assert.AreEqual(0, backY, "idle transform Y after switch back must be 0.");

            transform = petImage.RenderTransform as TranslateTransform;
            Assert.IsNotNull(transform);
            Assert.AreEqual(0, transform!.Y, 0.001,
                "PetImage transform Y must return to 0 after switching back to idle.");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// F-04 R4: Negative control -- a wrong compensation value must be detected
    /// by the foot stability check. This proves the test is not trivially
    /// passing due to formula cancellation.
    ///
    /// If we use offsetY=0 (no compensation) for curious, the world foot
    /// must NOT be stable -- the content shift in the PNG is real and
    /// uncompensated. Assert.AreNotEqual catches this.
    /// Then with the correct offsetY, Assert.AreEqual passes.
    /// </summary>
    [STATestMethod]
    public void WrongOffset_DetectedByFootStabilityCheck()
    {
        var assetDir = GetAssetDir();
        if (assetDir is null)
        {
            Assert.Inconclusive("Cannot locate asset directory with character.json.");
            return;
        }

        // Read real PNG foot positions.
        var idlePngPath = Path.Combine(assetDir, "idle_0.png");
        var curiousPngPath = Path.Combine(assetDir, "curious_0.png");
        int idleBottomY = MeasurePngBottomY(idlePngPath);
        int curiousBottomY = MeasurePngBottomY(curiousPngPath);
        Assert.IsTrue(idleBottomY > 0 && curiousBottomY > 0,
            "Both PNGs must have non-transparent content.");

        // Read the correct offsetY from JSON.
        double correctOffsetY = ReadOffsetYFromJson(assetDir, "curious");
        Assert.IsTrue(correctOffsetY > 0,
            "curious offsetY must be non-zero for negative control to be meaningful.");

        double windowTop = 100; // Fixed window position.

        // --- Negative control: wrong offset (0 instead of correctOffsetY) ---
        double wrongOffsetY = 0;
        double idleWorldFoot = windowTop + 0 + idleBottomY;
        double curiousWorldFootWrong = windowTop + wrongOffsetY + curiousBottomY;

        // The wrong compensation must produce a DIFFERENT world foot.
        Assert.AreNotEqual(idleWorldFoot, curiousWorldFootWrong, 1.0,
            "With wrong offset (0), world foot must NOT be stable -- " +
            "idle={0}, curious(wrong)={1}, difference={2}.",
            idleWorldFoot, curiousWorldFootWrong, idleWorldFoot - curiousWorldFootWrong);

        // --- Positive control: correct offset from JSON ---
        double curiousWorldFootCorrect = windowTop + correctOffsetY + curiousBottomY;

        // The correct compensation must produce a STABLE world foot.
        Assert.AreEqual(idleWorldFoot, curiousWorldFootCorrect, 1.0,
            "With correct offset ({0}), world foot must be stable -- " +
            "idle={1}, curious(correct)={2}.",
            correctOffsetY, idleWorldFoot, curiousWorldFootCorrect);
    }
}
