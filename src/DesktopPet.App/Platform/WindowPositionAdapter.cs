using DesktopPet.Core.Geometry;

namespace DesktopPet.App.Platform;

/// <summary>
/// F-02: Testable adapter that encapsulates the full DIP↔foot coordinate
/// adaptation pipeline used by MainWindow's ClampWindowToWorkArea,
/// OnDisplayLayoutChanged, and RecoverPet.
///
/// This class isolates the coordinate boundary so tests can inject synthetic
/// monitor layouts and verify cross-monitor DPI consistency without Win32 calls
/// or WPF window handles.
/// </summary>
public static class WindowPositionAdapter
{
    /// <summary>
    /// Clamp a window position to a visible work area, using the TARGET monitor's
    /// DPI for the reverse (foot→DIP) conversion. This ensures cross-monitor moves
    /// with different DPI scales are consistent.
    ///
    /// Pipeline: DIP Left/Top → foot physical px (using source DPI) →
    /// Core.ClampToWorkArea (uses target monitor DPI internally) →
    /// DIP Left/Top (using TARGET monitor DPI).
    /// </summary>
    /// <param name="monitors">Current monitor layout (injectable for tests).</param>
    /// <param name="leftDip">Window Left in DIP.</param>
    /// <param name="topDip">Window Top in DIP.</param>
    /// <param name="winWidthDip">Window width in DIP.</param>
    /// <param name="winHeightDip">Window height in DIP.</param>
    /// <param name="sourceDpiX">DPI scale X of the window's current monitor.</param>
    /// <param name="sourceDpiY">DPI scale Y of the window's current monitor.</param>
    /// <returns>Clamped (LeftDip, TopDip) using the target monitor's DPI.</returns>
    public static (double LeftDip, double TopDip) ClampToWorkArea(
        IReadOnlyList<MonitorInfo> monitors,
        double leftDip,
        double topDip,
        double winWidthDip,
        double winHeightDip,
        double sourceDpiX,
        double sourceDpiY)
    {
        if (monitors.Count == 0)
            return (leftDip, topDip);

        // Forward: DIP → foot physical pixels using source DPI.
        var foot = CoordinateTransform.WindowTopLeftToFootCenter(
            leftDip, topDip, winWidthDip, winHeightDip, sourceDpiX, sourceDpiY);

        // Core clamp (uses target monitor DPI internally).
        var clamped = MonitorLocator.ClampToWorkArea(monitors, foot, winWidthDip, winHeightDip);

        // Find target monitor for the clamped position.
        var targetMonitor = MonitorLocator.FindMonitor(monitors, clamped.FootCenterX, clamped.FootCenterY)
                             ?? MonitorLocator.FindNearestMonitor(monitors, clamped.FootCenterX, clamped.FootCenterY);

        // Reverse: foot → DIP using TARGET monitor DPI.
        return CoordinateTransform.FootCenterToWindowTopLeft(
            clamped, winWidthDip, winHeightDip, targetMonitor.DpiScaleX, targetMonitor.DpiScaleY);
    }

    /// <summary>
    /// Recover a window position using Core.RecoverPosition, then convert back
    /// to DIP using the TARGET monitor's DPI for cross-monitor consistency.
    ///
    /// Used by OnDisplayLayoutChanged (WM_DISPLAYCHANGE / WM_SETTINGCHANGE).
    /// </summary>
    /// <param name="monitors">Current monitor layout (injectable for tests).</param>
    /// <param name="leftDip">Window Left in DIP.</param>
    /// <param name="topDip">Window Top in DIP.</param>
    /// <param name="winWidthDip">Window width in DIP.</param>
    /// <param name="winHeightDip">Window height in DIP.</param>
    /// <param name="sourceDpiX">DPI scale X of the window's current monitor.</param>
    /// <param name="sourceDpiY">DPI scale Y of the window's current monitor.</param>
    /// <param name="savedDeviceName">Saved monitor device name (null for layout-change recovery).</param>
    /// <returns>Recovered (LeftDip, TopDip) using the target monitor's DPI.</returns>
    public static (double LeftDip, double TopDip) RecoverPosition(
        IReadOnlyList<MonitorInfo> monitors,
        double leftDip,
        double topDip,
        double winWidthDip,
        double winHeightDip,
        double sourceDpiX,
        double sourceDpiY,
        string? savedDeviceName)
    {
        if (monitors.Count == 0)
            return (leftDip, topDip);

        // Forward: DIP → foot physical pixels using source DPI.
        var foot = CoordinateTransform.WindowTopLeftToFootCenter(
            leftDip, topDip, winWidthDip, winHeightDip, sourceDpiX, sourceDpiY);

        // Core recover (uses target monitor DPI internally).
        var recovered = MonitorLocator.RecoverPosition(
            monitors, foot, savedDeviceName, winWidthDip, winHeightDip);

        // Find target monitor for the recovered position.
        var targetMonitor = MonitorLocator.FindMonitor(monitors, recovered.FootCenterX, recovered.FootCenterY)
                             ?? MonitorLocator.FindNearestMonitor(monitors, recovered.FootCenterX, recovered.FootCenterY);

        // Reverse: foot → DIP using TARGET monitor DPI.
        return CoordinateTransform.FootCenterToWindowTopLeft(
            recovered, winWidthDip, winHeightDip, targetMonitor.DpiScaleX, targetMonitor.DpiScaleY);
    }

    /// <summary>
    /// Recover to the primary monitor's work area center.
    /// Used by the manual Recover command.
    /// </summary>
    /// <param name="monitors">Current monitor layout (injectable for tests).</param>
    /// <param name="winWidthDip">Window width in DIP.</param>
    /// <param name="winHeightDip">Window height in DIP.</param>
    /// <returns>Recovered (LeftDip, TopDip) on the primary monitor.</returns>
    public static (double LeftDip, double TopDip) RecoverToPrimary(
        IReadOnlyList<MonitorInfo> monitors,
        double winWidthDip,
        double winHeightDip)
    {
        if (monitors.Count == 0)
            return (0, 0);

        var targetMonitor = MonitorLocator.FindPrimaryMonitor(monitors);
        var wa = targetMonitor.WorkArea.Bounds;

        var footX = wa.Left + wa.Width / 2.0;
        var footY = wa.Top + wa.Height / 2.0;
        var foot = new PetWorldPosition(footX, footY);

        var clamped = MonitorLocator.ClampToWorkArea(monitors, foot, winWidthDip, winHeightDip);

        return CoordinateTransform.FootCenterToWindowTopLeft(
            clamped, winWidthDip, winHeightDip, targetMonitor.DpiScaleX, targetMonitor.DpiScaleY);
    }
}
