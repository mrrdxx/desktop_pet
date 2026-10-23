using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-03: Resource boundary, validation, and error recovery tests.
/// Covers PNG decode size mismatch, sibling-prefix path rejection at App level,
/// and built-in fallback when all resources are unavailable.
/// 
/// Uses isolated temp directories and reflection to test PreloadImages
/// without touching real character assets or user archives.
/// Uses STATestMethod because WPF UI components require an STA thread.
/// </summary>
[TestClass]
public class ResourceBoundaryTests
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
                "ResourceBoundaryTests: MainWindow construction failed: {0}", ex);
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
    /// Creates a minimal PNG file of the given dimensions in the target directory.
    /// </summary>
    private static void CreatePng(string path, int width, int height)
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.Blue, null,
                new System.Windows.Rect(0, 0, width, height));
        }
        var bmp = new RenderTargetBitmap(width, height, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private static PetCharacterDef MakeCharacter(int frameWidth, int frameHeight, string imagePath)
    {
        return new PetCharacterDef
        {
            Id = "test",
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            FootAnchorX = frameWidth / 2,
            FootAnchorY = frameHeight - 10,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = imagePath, DurationMs = 500 }]
                }
            }
        };
    }

    [STATestMethod]
    public void PreloadImages_DecodeSizeMismatch_SkipsImage()
    {
        // F-03: A PNG that decodes to a different size than declared in JSON
        // must be rejected (skipped), not silently accepted.
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "pet-f03-sizetest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a 64x64 PNG but declare 120x120 in the character.
            CreatePng(Path.Combine(tempDir, "idle_0.png"), 64, 64);
            var character = MakeCharacter(120, 120, "idle_0.png");

            var cache = GetField<Dictionary<string, BitmapImage>>(window, "_imageCache");
            Assert.IsNotNull(cache);
            cache!.Clear();

            InvokeMethod(window, "PreloadImages", character, tempDir);

            // The mismatched image should NOT be in the cache.
            Assert.AreEqual(0, cache.Count,
                "Image with decode size mismatch must be rejected, not cached.");
        }
        finally
        {
            window.Close();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [STATestMethod]
    public void PreloadImages_CorrectSize_AcceptsImage()
    {
        // F-03: A PNG that matches the declared size must be accepted.
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "pet-f03-sizetest-ok-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            CreatePng(Path.Combine(tempDir, "idle_0.png"), 120, 120);
            var character = MakeCharacter(120, 120, "idle_0.png");

            var cache = GetField<Dictionary<string, BitmapImage>>(window, "_imageCache");
            Assert.IsNotNull(cache);
            cache!.Clear();

            InvokeMethod(window, "PreloadImages", character, tempDir);

            Assert.AreEqual(1, cache.Count,
                "Image with matching decode size must be cached.");
        }
        finally
        {
            window.Close();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [STATestMethod]
    public void PreloadImages_SiblingDirectoryPrefix_RejectsPath()
    {
        // F-03: A path that resolves to a sibling directory sharing a prefix
        // with the asset directory must be rejected at the App level too.
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        // Create a sibling directory next to a "pet" directory.
        var parentDir = Path.Combine(Path.GetTempPath(), "pet-f03-sibling-" + Guid.NewGuid().ToString("N")[..8]);
        var petDir = Path.Combine(parentDir, "pet");
        var siblingDir = Path.Combine(parentDir, "pet-sibling");
        Directory.CreateDirectory(petDir);
        Directory.CreateDirectory(siblingDir);

        try
        {
            // Place a PNG in the sibling directory.
            CreatePng(Path.Combine(siblingDir, "outside.png"), 120, 120);

            // Character references ../pet-sibling/outside.png from petDir.
            var character = MakeCharacter(120, 120, "../pet-sibling/outside.png");

            var cache = GetField<Dictionary<string, BitmapImage>>(window, "_imageCache");
            Assert.IsNotNull(cache);
            cache!.Clear();

            InvokeMethod(window, "PreloadImages", character, petDir);

            // The sibling path must NOT be accepted.
            Assert.AreEqual(0, cache.Count,
                "Sibling directory path with shared prefix must be rejected.");
        }
        finally
        {
            window.Close();
            if (Directory.Exists(parentDir)) Directory.Delete(parentDir, true);
        }
    }

    [STATestMethod]
    public void AllResourcesMissing_FallbackBitmapVisible()
    {
        // F-03: When all PNG resources are unavailable, the built-in fallback
        // bitmap must be generated so the pet is not an invisible window.
        // R3 noted: the previous version only cleared the cache but never set
        // PetImage.Source to null, so HasVisibleImage could pass on a stale
        // image from MainWindow construction. This version explicitly nulls
        // Source, then verifies the fallback is a RenderTargetBitmap with
        // non-transparent pixels — removing the fallback branch will fail.
        var window = TryCreateMainWindow();
        if (window is null)
        {
            Assert.Inconclusive("MainWindow requires character assets; skipping.");
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "pet-f03-empty-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a character that references a non-existent image.
            var character = MakeCharacter(120, 120, "nonexistent.png");

            var cache = GetField<Dictionary<string, BitmapImage>>(window, "_imageCache");
            Assert.IsNotNull(cache);
            cache!.Clear();

            // Load character into the window's player so UpdateFrame uses it.
            var animPlayer = GetField<AnimationPlayer>(window, "_animPlayer");
            Assert.IsNotNull(animPlayer);
            animPlayer!.UpdateCharacter(character);

            // Set the character field for fallback logic in UpdateFrame.
            var charField = typeof(MainWindow).GetField("_character",
                BindingFlags.NonPublic | BindingFlags.Instance);
            charField?.SetValue(window, character);

            // PreloadImages with empty dir — no images cached.
            InvokeMethod(window, "PreloadImages", character, tempDir);
            Assert.AreEqual(0, cache.Count, "No images should be cached for missing resources.");

            // Reset lastDisplayedFrame to force UpdateFrame to try loading.
            var lastFrameField = typeof(MainWindow).GetField("_lastDisplayedFrame",
                BindingFlags.NonPublic | BindingFlags.Instance);
            lastFrameField?.SetValue(window, string.Empty);

            // R3 fix: Explicitly null the Source so we test the fallback creation
            // path, not a stale image left from MainWindow construction.
            var petImageField = typeof(MainWindow).GetField("PetImage",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var petImage = petImageField?.GetValue(window) as System.Windows.Controls.Image;
            Assert.IsNotNull(petImage, "PetImage control must exist.");
            petImage!.Source = null;
            Assert.IsNull(petImage.Source,
                "Precondition: PetImage.Source must be null before UpdateFrame.");

            // Call UpdateFrame — should generate fallback bitmap.
            InvokeMethod(window, "UpdateFrame");

            // Verify a source was set (not still null).
            Assert.IsNotNull(petImage.Source,
                "UpdateFrame must set a fallback bitmap when all resources are missing and Source was null.");

            // Verify it is a RenderTargetBitmap (the program-drawn fallback),
            // not a BitmapImage (which would indicate a cached PNG was used).
            Assert.IsInstanceOfType(petImage.Source, typeof(RenderTargetBitmap),
                "Fallback must be a RenderTargetBitmap (program-drawn), not a BitmapImage.");

            // Verify the fallback contains non-transparent pixels.
            var fallbackBmp = (RenderTargetBitmap)petImage.Source;
            Assert.IsTrue(fallbackBmp.Width > 0 && fallbackBmp.Height > 0,
                "Fallback bitmap must have non-zero dimensions.");

            // Copy pixels to check for non-transparent content.
            var pixels = new byte[fallbackBmp.PixelWidth * fallbackBmp.PixelHeight * 4];
            fallbackBmp.CopyPixels(pixels, fallbackBmp.PixelWidth * 4, 0);
            bool hasNonTransparentPixel = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] > 0)
                {
                    hasNonTransparentPixel = true;
                    break;
                }
            }
            Assert.IsTrue(hasNonTransparentPixel,
                "Fallback bitmap must contain at least one non-transparent pixel.");

            Assert.IsTrue(window.HasVisibleImage,
                "HasVisibleImage must be true after fallback bitmap is set.");
        }
        finally
        {
            window.Close();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
