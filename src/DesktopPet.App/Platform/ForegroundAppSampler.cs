using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPet.App.Platform;

/// <summary>
/// P6-001: Resolves the normalized executable base name of the external
/// application currently in the foreground — the "app id" used by the
/// usage-stats collector.
/// <para>
/// Blacklist compliance (PRODUCT R-09 / ARCHITECTURE §7): only the window
/// handle / owner process / process image name are consulted. Window titles,
/// URLs, content and keys are never read. The result is a normalized base
/// name (lower-cased file name without extension); a full path or the PID
/// is never retained as long-term identity. Same-name programs merge into
/// one id (the Core collector guarantees this by keying on the returned
/// string only).
/// </para>
/// <para>
/// "No relevant external app" (returns null, <paramref name="resolutionFailed"/>
/// = false) happens when the foreground is our own pet, the desktop
/// surfaces (Progman/WorkerW) or no foreground window exists — nothing is
/// attributed and no coverage mismatch is counted. When the owner process
/// image name cannot be resolved, the interval is dropped and one coverage
/// mismatch is counted (<paramref name="resolutionFailed"/> = true) — the
/// single-policy "drop + count" contract.
/// </para>
/// <para>
/// The Win32 calls are extracted into virtual methods so tests can inject
/// controlled foreground values without touching real windows.
/// </para>
/// </summary>
public class ForegroundAppSampler
{
    // PROCESS_QUERY_LIMITED_INFORMATION: enough for QueryFullProcessImageName
    // on both same- and higher-integrity processes; no SeDebugPrivilege needed.
    private const uint ProcessQueryLimitedInformation = 0x1000;

    // The desktop root ("Progman") and its composition surface ("WorkerW")
    // are the foreground while the user is on the desktop. They are not
    // external applications — nothing is attributed to them.
    private const string DesktopRootClass = "Progman";
    private const string DesktopWorkerClass = "WorkerW";

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Our own process id; the pet itself is never a "used" app.</summary>
    internal virtual int OwnProcessId => Environment.ProcessId;

    /// <summary>Test override point: the current foreground window handle.</summary>
    internal virtual IntPtr CurrentForegroundWindow() => GetForegroundWindow();

    /// <summary>Test override point: owner process id of the foreground window
    /// (null when the lookup fails).</summary>
    internal virtual int? GetForegroundProcessId(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        uint pid = 0;
        if (GetWindowThreadProcessId(hwnd, out pid) == 0 || pid == 0)
            return null;
        return (int)pid;
    }

    /// <summary>Test override point: window class name ("" when unavailable).</summary>
    internal virtual string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Test override point: executable image path of a process
    /// (null when it cannot be resolved).</summary>
    internal virtual string? GetProcessImagePath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(handle, 0, sb, ref size))
                return null;
            return sb.ToString();
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Normalizes an executable image path to the stats app id: base file
    /// name without extension, lower-cased invariantly. Never a path, never
    /// a pid, never a display-name / window title.
    /// </summary>
    internal static string NormalizeAppId(string imagePath)
    {
        var name = Path.GetFileNameWithoutExtension(imagePath ?? "");
        return string.IsNullOrEmpty(name) ? "" : name.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Resolves the normalized foreground app id.
    /// </summary>
    /// <returns>The normalized base name, or null for "no relevant external
    /// app" (pet itself / desktop / no foreground).</returns>
    /// <param name="resolutionFailed">True when the foreground existed but
    /// could not be identified (single-policy drop + count).</param>
    public virtual string? TryGetForegroundAppId(out bool resolutionFailed)
    {
        resolutionFailed = false;

        var hwnd = CurrentForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null; // no foreground window — nothing to attribute

        var pid = GetForegroundProcessId(hwnd);
        if (pid is null)
        {
            // The window vanished between the two calls (or the owner
            // lookup failed). The interval cannot be identified — drop it.
            resolutionFailed = true;
            return null;
        }
        if (pid.Value == OwnProcessId)
            return null; // our own pet window — not an external app

        var className = GetWindowClassName(hwnd);
        if (className == DesktopRootClass || className == DesktopWorkerClass)
            return null; // desktop surfaces — not an external app

        var imagePath = GetProcessImagePath(pid.Value);
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            // Identification failed (protected process, pid exited, ...).
            // Single policy: drop the interval and count a coverage mismatch.
            resolutionFailed = true;
            return null;
        }

        var appId = NormalizeAppId(imagePath);
        if (appId.Length == 0)
        {
            resolutionFailed = true;
            return null;
        }
        return appId;
    }
}