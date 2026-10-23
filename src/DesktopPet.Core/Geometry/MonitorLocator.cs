using System.Collections.Generic;
using System.Linq;

namespace DesktopPet.Core.Geometry;

/// <summary>
/// Finds which monitor a position falls on, and constrains positions to visible work areas.
/// Handles negative coordinates and gaps between monitors (virtual desktop holes).
/// </summary>
public static class MonitorLocator
{
    /// <summary>
    /// Find the monitor that contains the given physical pixel coordinate.
    /// Returns null if the point is in a gap between monitors (virtual desktop hole).
    /// </summary>
    public static MonitorInfo? FindMonitor(IReadOnlyList<MonitorInfo> monitors, double pixelX, double pixelY)
    {
        foreach (var m in monitors)
        {
            if (m.FullBounds.Contains((int)pixelX, (int)pixelY))
                return m;
        }
        return null;
    }

    /// <summary>
    /// Find the nearest monitor to a position that may be in a gap or off-screen.
    /// Uses center-to-center distance.
    /// </summary>
    public static MonitorInfo FindNearestMonitor(IReadOnlyList<MonitorInfo> monitors, double pixelX, double pixelY)
    {
        if (monitors.Count == 0)
            throw new System.ArgumentException("Monitor list must not be empty.", nameof(monitors));

        MonitorInfo nearest = monitors[0];
        double nearestDist = double.MaxValue;

        foreach (var m in monitors)
        {
            var cx = m.FullBounds.Left + m.FullBounds.Width / 2.0;
            var cy = m.FullBounds.Top + m.FullBounds.Height / 2.0;
            var dist = (cx - pixelX) * (cx - pixelX) + (cy - pixelY) * (cy - pixelY);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = m;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Clamp a pet's foot-center position so the pet window is fully visible within
    /// the nearest monitor's work area. If the position is in a gap, snaps to the
    /// nearest monitor first.
    /// </summary>
    /// <param name="monitors">All monitors on the virtual desktop.</param>
    /// <param name="foot">Pet's current foot-center position (physical pixels).</param>
    /// <param name="windowWidthDip">Window width in DIP.</param>
    /// <param name="windowHeightDip">Window height in DIP.</param>
    /// <returns>Clamped foot-center position that keeps the window in a visible work area.</returns>
    public static PetWorldPosition ClampToWorkArea(
        IReadOnlyList<MonitorInfo> monitors,
        PetWorldPosition foot,
        double windowWidthDip,
        double windowHeightDip)
    {
        if (monitors.Count == 0)
            throw new System.ArgumentException("Monitor list must not be empty.", nameof(monitors));

        // Find the monitor the pet is on, or the nearest one if in a gap.
        var monitor = FindMonitor(monitors, foot.FootCenterX, foot.FootCenterY)
                      ?? FindNearestMonitor(monitors, foot.FootCenterX, foot.FootCenterY);

        var wa = monitor.WorkArea.Bounds;
        var dpiX = monitor.DpiScaleX;
        var dpiY = monitor.DpiScaleY;

        // Window dimensions in physical pixels on this monitor
        var winW = windowWidthDip * dpiX;
        var winH = windowHeightDip * dpiY;

        // Foot center means: left = footX - winW/2, top = footY - winH
        // Keep window within work area: left >= wa.Left, right <= wa.Right
        var minFootX = wa.Left + winW / 2.0;
        var maxFootX = wa.Right - winW / 2.0;
        var minFootY = wa.Top + winH;
        var maxFootY = wa.Bottom;

        var clampedX = foot.FootCenterX;
        var clampedY = foot.FootCenterY;

        if (clampedX < minFootX) clampedX = minFootX;
        if (clampedX > maxFootX) clampedX = maxFootX;
        if (clampedY < minFootY) clampedY = minFootY;
        if (clampedY > maxFootY) clampedY = maxFootY;

        return new PetWorldPosition(clampedX, clampedY);
    }

    /// <summary>
    /// Find the primary monitor (origin at 0,0). Returns the first monitor
    /// with Left==0 && Top==0, or the first monitor if none matches.
    /// </summary>
    public static MonitorInfo FindPrimaryMonitor(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
            throw new System.ArgumentException("Monitor list must not be empty.", nameof(monitors));

        foreach (var m in monitors)
        {
            if (m.FullBounds.Left == 0 && m.FullBounds.Top == 0)
                return m;
        }

        return monitors[0];
    }

    /// <summary>
    /// Clamp a pet's foot-center position to a specific monitor's work area.
    /// </summary>
    private static PetWorldPosition ClampToMonitorWorkArea(
        MonitorInfo monitor,
        PetWorldPosition foot,
        double windowWidthDip,
        double windowHeightDip)
    {
        var wa = monitor.WorkArea.Bounds;
        var dpiX = monitor.DpiScaleX;
        var dpiY = monitor.DpiScaleY;

        var winW = windowWidthDip * dpiX;
        var winH = windowHeightDip * dpiY;

        var minFootX = wa.Left + winW / 2.0;
        var maxFootX = wa.Right - winW / 2.0;
        var minFootY = wa.Top + winH;
        var maxFootY = wa.Bottom;

        var clampedX = foot.FootCenterX;
        var clampedY = foot.FootCenterY;

        if (clampedX < minFootX) clampedX = minFootX;
        if (clampedX > maxFootX) clampedX = maxFootX;
        if (clampedY < minFootY) clampedY = minFootY;
        if (clampedY > maxFootY) clampedY = maxFootY;

        return new PetWorldPosition(clampedX, clampedY);
    }

    /// <summary>
    /// Recover a pet whose saved monitor may no longer be present (disconnected or layout changed).
    /// Per ARCHITECTURE §4: "标识失效时映射到当前主屏可见区域".
    /// </summary>
    /// <param name="currentMonitors">Currently connected monitors.</param>
    /// <param name="savedFoot">Saved foot-center position from archive.</param>
    /// <param name="savedDeviceName">Saved monitor device name (may be stale).</param>
    /// <param name="windowWidthDip">Window width in DIP.</param>
    /// <param name="windowHeightDip">Window height in DIP.</param>
    /// <returns>Recovered position on a visible monitor.</returns>
    public static PetWorldPosition RecoverPosition(
        IReadOnlyList<MonitorInfo> currentMonitors,
        PetWorldPosition savedFoot,
        string? savedDeviceName,
        double windowWidthDip,
        double windowHeightDip)
    {
        if (currentMonitors.Count == 0)
            throw new System.ArgumentException("Monitor list must not be empty.", nameof(currentMonitors));

        // If we have a saved device name, try to find that specific monitor.
        if (savedDeviceName is not null)
        {
            var savedMonitor = currentMonitors.FirstOrDefault(m => m.DeviceName == savedDeviceName);
            if (savedMonitor.DeviceName is not null)
            {
                // Monitor still exists: clamp to its work area directly.
                return ClampToMonitorWorkArea(savedMonitor, savedFoot, windowWidthDip, windowHeightDip);
            }
        }

        // Saved monitor is gone (or no saved name): map to the primary monitor
        // per ARCHITECTURE §4 "标识失效时映射到当前主屏可见区域".
        var primary = FindPrimaryMonitor(currentMonitors);
        return ClampToMonitorWorkArea(primary, savedFoot, windowWidthDip, windowHeightDip);
    }
}
