using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace DesktopPet.App.Platform;

/// <summary>Geometry of the current fixed-size, Stretch=None pet image.
/// Cache decoded alpha once per source; never read PNGs on each input tick.</summary>
internal static class PetImageGeometry
{
    private sealed record Mask(int Width, int Height, byte[] Pixels, Rect Bounds);
    private static readonly ConditionalWeakTable<BitmapSource, Mask> Masks = new();

    private static Mask GetMask(BitmapSource source) => Masks.GetValue(source, bitmap =>
    {
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight;
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (pixels[(y * width + x) * 4 + 3] != 0)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        var bounds = right < 0 ? Rect.Empty : new Rect(left, top, right - left + 1, bottom - top + 1);
        return new Mask(width, height, pixels, bounds);
    });

    private static Rect SourceRect(Image image, BitmapSource source)
    {
        double width = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
        double height = image.ActualHeight > 0 ? image.ActualHeight : image.Height;
        // WPF Stretch=None uses the PNG's natural DIP size and centers it.
        return new Rect((width - source.Width) / 2, (height - source.Height) / 2, source.Width, source.Height);
    }

    internal static bool Contains(Image image, Point localPoint)
    {
        if (image.Source is not BitmapSource source) return false;
        var rect = SourceRect(image, source);
        if (!rect.Contains(localPoint) || rect.Width <= 0 || rect.Height <= 0) return false;
        var mask = GetMask(source);
        int x = (int)Math.Floor((localPoint.X - rect.Left) * mask.Width / rect.Width);
        int y = (int)Math.Floor((localPoint.Y - rect.Top) * mask.Height / rect.Height);
        return x >= 0 && y >= 0 && x < mask.Width && y < mask.Height && mask.Pixels[(y * mask.Width + x) * 4 + 3] != 0;
    }

    internal static Rect OpaqueBounds(Image image)
    {
        if (image.Source is not BitmapSource source) return Rect.Empty;
        var mask = GetMask(source);
        if (mask.Bounds.IsEmpty) return Rect.Empty;
        var rect = SourceRect(image, source);
        return new Rect(rect.Left + mask.Bounds.Left * rect.Width / mask.Width,
            rect.Top + mask.Bounds.Top * rect.Height / mask.Height,
            mask.Bounds.Width * rect.Width / mask.Width, mask.Bounds.Height * rect.Height / mask.Height);
    }
}
