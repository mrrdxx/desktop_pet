namespace DesktopPet.Core.Geometry;

/// <summary>
/// World position of a pet in virtual desktop physical pixels (floating point).
/// The anchor point is the bottom-center of the pet (foot center).
/// </summary>
public readonly record struct PetWorldPosition(double FootCenterX, double FootCenterY)
{
    public static PetWorldPosition Zero => new(0, 0);
}

/// <summary>
/// Converts between pet world position (foot-center, physical pixels) and
/// window top-left corner (DIP for WPF Left/Top).
/// All DIP↔pixel conversion happens at this boundary, not scattered in UI code.
/// </summary>
public static class CoordinateTransform
{
    /// <summary>
    /// Convert a physical pixel offset to DIP using the monitor's DPI scale.
    /// </summary>
    public static (double DipX, double DipY) PixelsToDip(
        double pixelX, double pixelY, double dpiScaleX, double dpiScaleY)
    {
        if (dpiScaleX <= 0 || dpiScaleY <= 0)
            return (pixelX, pixelY);

        return (pixelX / dpiScaleX, pixelY / dpiScaleY);
    }

    /// <summary>
    /// Convert DIP to physical pixel using the monitor's DPI scale.
    /// </summary>
    public static (double PixelX, double PixelY) DipToPixels(
        double dipX, double dipY, double dpiScaleX, double dpiScaleY)
    {
        return (dipX * dpiScaleX, dipY * dpiScaleY);
    }

    /// <summary>
    /// Compute the window top-left (in DIP) from the pet's foot-center world position.
    /// </summary>
    /// <param name="foot">World position of the pet's foot center (physical pixels).</param>
    /// <param name="windowWidthDip">Window width in DIP.</param>
    /// <param name="windowHeightDip">Window height in DIP.</param>
    /// <param name="dpiScaleX">Horizontal DPI scale of the monitor the pet is on.</param>
    /// <param name="dpiScaleY">Vertical DPI scale of the monitor the pet is on.</param>
    /// <returns>Window Left/Top in DIP.</returns>
    public static (double LeftDip, double TopDip) FootCenterToWindowTopLeft(
        PetWorldPosition foot,
        double windowWidthDip,
        double windowHeightDip,
        double dpiScaleX,
        double dpiScaleY)
    {
        // Window center X = foot X (foot is bottom-center)
        // Window top-left X in pixels = footX - (windowWidthDip * dpiScaleX) / 2
        var windowWidthPx = windowWidthDip * dpiScaleX;
        var windowHeightPx = windowHeightDip * dpiScaleY;

        var leftPx = foot.FootCenterX - windowWidthPx / 2.0;
        var topPx = foot.FootCenterY - windowHeightPx;

        return PixelsToDip(leftPx, topPx, dpiScaleX, dpiScaleY);
    }

    /// <summary>
    /// Compute the pet's foot-center world position from the window top-left (in DIP).
    /// </summary>
    public static PetWorldPosition WindowTopLeftToFootCenter(
        double leftDip,
        double topDip,
        double windowWidthDip,
        double windowHeightDip,
        double dpiScaleX,
        double dpiScaleY)
    {
        var (leftPx, topPx) = DipToPixels(leftDip, topDip, dpiScaleX, dpiScaleY);
        var windowWidthPx = windowWidthDip * dpiScaleX;
        var windowHeightPx = windowHeightDip * dpiScaleY;

        var footX = leftPx + windowWidthPx / 2.0;
        var footY = topPx + windowHeightPx;

        return new PetWorldPosition(footX, footY);
    }
}
