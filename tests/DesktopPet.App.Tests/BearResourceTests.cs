using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

[TestClass]
public class BearResourceTests
{
    [STATestMethod]
    public void BlinkFrames_ChangeOnlyEyes_PreserveAlphaMouthAndBody()
    {
        var window = new MainWindow();
        try
        {
            var path = (string)typeof(MainWindow).GetField("_assetDir", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            byte[] Pixels(string name)
            {
                var image = BitmapFrame.Create(new Uri(Path.Combine(path,name)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[120*120*4]; converted.CopyPixels(pixels,120*4,0); return pixels;
            }
            var original=Pixels("idle_0.png");
            foreach(var name in new[]{"blink_half.png","blink_closed.png"})
            {
                var blink=Pixels(name); int changed=0;
                for(int y=0;y<120;y++)
                for(int x=0;x<120;x++)
                {
                    int index=(y*120+x)*4;
                    Assert.AreEqual(original[index+3],blink[index+3],name+" changed alpha");
                    bool eye = y>=39 && y<57 && ((x>=37 && x<52)||(x>=68 && x<83));
                    for(int c=0;c<3;c++)
                    {
                        if(!eye) Assert.AreEqual(original[index+c],blink[index+c],$"{name} changed non-eye ({x},{y})");
                        if(original[index+c]!=blink[index+c]) changed++;
                    }
                }
                Assert.IsTrue(changed>0,name+" must actually change the eyes");
            }
            Assert.IsFalse(Pixels("blink_half.png").SequenceEqual(Pixels("blink_closed.png")), "Half and closed must be distinct.");
            var character = CharacterLoader.Load(File.ReadAllText(Path.Combine(path,"character.json")));
            Assert.AreEqual(1,character.Actions["idle"].Frames.Count,"Idle no longer contains the content smile.");
            CollectionAssert.AreEqual(new[]{"blink_half.png","blink_closed.png","blink_half.png","idle_0.png"},
                character.Actions["blink"].Frames.Select(f=>f.ImagePath).ToArray());
            Assert.AreEqual(220,character.Actions["blink"].Frames.Sum(f=>f.DurationMs));
        }
        finally {window.Close();}
    }

    [STATestMethod]
    public void RuntimeSelectsApprovedBear_AndLoadsVisibleTransparentImage()
    {
        var window = new MainWindow();
        try
        {
            var path = (string)typeof(MainWindow).GetField("_assetDir", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            Assert.AreEqual("bear", Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
            var character = CharacterLoader.Load(File.ReadAllText(Path.Combine(path, "character.json")));
            Assert.AreEqual("bear-v1", character.Id);
            var image = (System.Windows.Controls.Image)window.FindName("PetImage");
            Assert.IsInstanceOfType<BitmapSource>(image.Source);
            Assert.IsTrue(window.IsInHeadZone(new System.Windows.Point(60, 20)), "The real bear forehead must be pettable.");
            Assert.IsFalse(window.IsInHeadZone(new System.Windows.Point(4, 4)), "The transparent corner must not be pettable.");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void EveryBearFrame_Is120PixelsAt96Dpi_Transparent_AndSharesWorldFoot()
    {
        var window = new MainWindow();
        try
        {
            var path = (string)typeof(MainWindow).GetField("_assetDir", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            var character = CharacterLoader.Load(File.ReadAllText(Path.Combine(path, "character.json")));
            foreach (var action in character.Actions.Values)
            foreach (var frame in action.Frames)
            {
                var bitmap = BitmapFrame.Create(new Uri(Path.Combine(path, frame.ImagePath)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.AreEqual(120, bitmap.PixelWidth, frame.ImagePath);
                Assert.AreEqual(120, bitmap.PixelHeight, frame.ImagePath);
                Assert.AreEqual(96.0, bitmap.DpiX, 0.1, frame.ImagePath);
                Assert.AreEqual(96.0, bitmap.DpiY, 0.1, frame.ImagePath);
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[120 * 120 * 4];
                converted.CopyPixels(pixels, 120 * 4, 0);
                foreach (var corner in new[] { 0, 119, 119 * 120, 120 * 120 - 1 })
                    Assert.AreEqual((byte)0, pixels[corner * 4 + 3], frame.ImagePath + " has opaque corners");
                int bottom = -1;
                for (int y = 0; y < 120; y++)
                for (int x = 0; x < 120; x++)
                    if (pixels[(y * 120 + x) * 4 + 3] > 0) bottom = y;
                Assert.IsTrue(bottom >= 0, frame.ImagePath + " must not be empty");
                Assert.AreEqual((double)character.FootAnchorY, bottom + frame.OffsetY, 0.001, frame.ImagePath + " has an unstable foot");
            }
            foreach (var required in new[] { "idle", "curious", "drag", "grievance", "happy", "annoyed", "sleepy", "content", "bashful" })
                Assert.IsTrue(character.Actions.ContainsKey(required), "Missing static expression/action: " + required);
            Assert.IsFalse(character.Actions.ContainsKey("dodge"), "D-009 removes the fast-mouse dodge action.");
            Assert.AreNotEqual(character.Actions["curious"].Frames[0].ImagePath, character.Actions["grievance"].Frames[0].ImagePath,
                "Curiosity and grievance must not share a placeholder face.");
        }
        finally { window.Close(); }
    }
}
