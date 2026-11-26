using System.Runtime.InteropServices;

namespace DesktopPet.App.Platform;

/// <summary>
/// P4-001: Samples the system-wide last input time using the Win32
/// <c>GetLastInputInfo</c> API and converts it to the same monotonic
/// millisecond domain as <see cref="MonotonicClock"/>.
/// <para>
/// <c>GetLastInputInfo</c> returns the tick count (ms) since system boot
/// from <c>GetTickCount</c>, which is a monotonic source. Both
/// <c>GetTickCount</c> and the last-input tick are 32-bit unsigned values
/// that wrap around every ~49.7 days.
/// </para>
/// <para>
/// F-03 R1: To handle uint wraparound correctly, we compute the input
/// age (elapsed since last input) entirely within the Windows tick-count
/// domain using unchecked arithmetic: <c>age = nowTick - lastInputTick</c>.
/// When <c>nowTick</c> has wrapped past <c>lastInputTick</c>, the
/// subtraction still yields the correct positive age due to uint modular
/// arithmetic. We then convert the age to the monotonic domain:
/// <c>lastInputMonotonic = monotonicNow - age</c>.
/// </para>
/// <para>
/// For testability, the actual P/Invoke calls are extracted into virtual
/// methods so tests can inject controlled values.
/// </para>
/// </summary>
public class InputIdleSampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    /// <summary>
    /// F-03 R1: Returns the monotonic timestamp (ms, same domain as
    /// <see cref="MonotonicClock"/>) of the last system-wide user input,
    /// or null if the value cannot be determined.
    /// <para>
    /// The input age is computed in the Windows tick-count domain
    /// (uint, wraps every ~49.7 days) using unchecked subtraction,
    /// then converted to the monotonic domain. This correctly handles
    /// uint wraparound: if <c>GetTickCount</c> has wrapped past the
    /// last-input tick, the age is still a correct positive value.
    /// </para>
    /// <para>
    /// Returns null if the Win32 API call fails or if the computed age
    /// is unreasonably large (exceeds 50 days), which would indicate an
    /// unreliable reading.
    /// </para>
    /// <para>
    /// F-03 R2: This overload reads <see cref="MonotonicClock"/> internally
    /// and may be slightly ahead or behind the caller's clock reading.
    /// Prefer <see cref="GetLastInputMs(double)"/> which accepts the
    /// caller's monotonic baseline so the input timestamp and the tick
    /// time use the same clock snapshot.
    /// </para>
    /// </summary>
    public double? GetLastInputMs()
    {
        return GetLastInputMs(MonotonicClock.Instance.GetElapsedMs());
    }

    /// <summary>
    /// F-03 R2: Returns the monotonic timestamp (ms) of the last
    /// system-wide user input, using the caller-supplied monotonic
    /// baseline. This ensures the input timestamp and the tick time
    /// are computed from the same clock snapshot, preventing a valid
    /// age=0 sample from being rejected as a "future" timestamp.
    /// <para>
    /// The input age is computed in the Windows tick-count domain
    /// (uint, wraps every ~49.7 days) using unchecked subtraction,
    /// then converted to the monotonic domain:
    /// <c>lastInputMonotonic = monotonicNow - age</c>.
    /// </para>
    /// <para>
    /// F-03 R2: The returned value may be negative when the last input
    /// occurred before the process started (age &gt; monotonicNow). This
    /// is a valid reading — the user was active before the app launched.
    /// The caller should NOT reject negative values as "future"; they
    /// represent input that predates the monotonic baseline.
    /// </para>
    /// <para>
    /// Returns null if the Win32 API call fails or if the computed age
    /// is unreasonably large (exceeds 49 days).
    /// </para>
    /// </summary>
    /// <param name="monotonicNow">The current monotonic time in
    /// milliseconds, supplied by the caller (typically the same value
    /// used for the session tick). This ensures the input timestamp and
    /// the tick time share the same clock snapshot.</param>
    public virtual double? GetLastInputMs(double monotonicNow)
    {
        var lastInputTick = GetLastInputTickMs();
        if (lastInputTick is null)
            return null;

        var nowTick = GetCurrentTickMs();
        if (nowTick is null)
            return null;

        // F-03 R1: Compute input age in the Windows tick-count domain.
        // unchecked uint subtraction handles wraparound: if nowTick has
        // wrapped past lastInputTick, the result is still the correct
        // positive elapsed time.
        uint ageMs;
        unchecked
        {
            ageMs = nowTick.Value - lastInputTick.Value;
        }

        // Sanity check: if the age exceeds 49 days, the reading is
        // unreliable. GetTickCount wraps at ~49.7 days (uint.MaxValue),
        // so an age close to that threshold likely indicates a stale
        // lastInputTick (e.g. from a previous boot session) or a
        // backwards-moving lastInput. Use ulong for the comparison
        // to avoid uint overflow in the threshold constant.
        const ulong MaxReliableAgeMs = 49UL * 24UL * 60UL * 60UL * 1000UL;
        if ((ulong)ageMs > MaxReliableAgeMs)
            return null;

        // F-03 R2: Convert the age to the monotonic domain using the
        // caller-supplied baseline. The result may be negative when
        // the input predates the process start (age > monotonicNow).
        // This is valid — the caller should accept it.
        return monotonicNow - ageMs;
    }

    /// <summary>
    /// Test override point: returns the last input time in GetTickCount
    /// milliseconds (uint domain). The default implementation calls the
    /// Win32 API. Tests override this to inject controlled idle values.
    /// </summary>
    internal virtual uint? GetLastInputTickMs()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii))
            return lii.dwTime;
        return null;
    }

    /// <summary>
    /// F-03 R1: Returns the current GetTickCount value (uint domain).
    /// Extracted as a virtual method so tests can inject controlled
    /// "current tick" values to simulate wraparound scenarios.
    /// </summary>
    internal virtual uint? GetCurrentTickMs()
    {
        return GetTickCount();
    }
}
