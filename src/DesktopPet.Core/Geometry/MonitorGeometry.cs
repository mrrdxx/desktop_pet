namespace DesktopPet.Core.Geometry;

/// <summary>
/// Physical pixel rectangle on the virtual desktop. Allows negative coordinates
/// for monitors to the left/above the primary monitor.
/// All values are in virtual desktop physical pixels (not DIP).
/// </summary>
public readonly record struct MonitorRectPx(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public bool Contains(int x, int y)
        => x >= Left && x < Right && y >= Top && y < Bottom;

    public static MonitorRectPx FromLTRB(int left, int top, int right, int bottom)
        => new(left, top, right - left, bottom - top);
}

/// <summary>
/// Working area of a monitor (excluding taskbar), in physical pixels.
/// </summary>
public readonly record struct WorkAreaPx(MonitorRectPx Bounds);

/// <summary>
/// A single monitor's geometry on the virtual desktop.
/// </summary>
public readonly record struct MonitorInfo(
    string? DeviceName,
    MonitorRectPx FullBounds,
    WorkAreaPx WorkArea,
    double DpiScaleX,
    double DpiScaleY)
{
    /// <summary>
    /// DPI scale as a ratio: 1.0 = 100%, 1.5 = 150%, 2.0 = 200%.
    /// </summary>
    public double DpiScale => DpiScaleX;
}
