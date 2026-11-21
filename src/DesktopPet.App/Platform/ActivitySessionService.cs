using System.Windows.Threading;
using DesktopPet.Core.Events;
using Microsoft.Win32;

namespace DesktopPet.App.Platform;

/// <summary>
/// P4-001: App-level service that owns the global <see cref="ActivitySession"/>,
/// samples input idle, subscribes to system lifecycle events (lock/sleep/pause),
/// and drives the session tick on a timer.
/// <para>
/// This service is the single integration point between App-level sampling
/// and Core-level timing. It does NOT modify MainWindow.OnUpdateTick, does
/// NOT play any performance, and does NOT change the archive.
/// </para>
/// <para>
/// Lifecycle:
/// <list type="bullet">
/// <item><see cref="Start"/>: begins the sampling timer and starts the
/// session with the current monotonic baseline.</item>
/// <item><see cref="Stop"/>: stops the timer and releases system event
/// subscriptions. Called on app exit.</item>
/// <item><see cref="OnPauseToggled"/>: called by App when the global pause
/// state changes. Pauses suspend the session; resumes call
/// <see cref="ActivitySession.Resume"/>.</item>
/// <item><see cref="OnSessionSwitch"/>: called when the system session
/// switches (lock/unlock). Locking suspends; unlocking resumes.</item>
/// <item><see cref="OnPowerModeChanged"/>: called when the system power
/// mode changes (sleep/resume). Sleeping suspends; resuming resumes.</item>
/// </list>
/// </para>
/// <para>
/// F-02 R2: System events (SessionSwitch, PowerModeChanged) fire on a
/// system thread, not the Dispatcher thread. The public
/// <see cref="OnSessionSwitch"/> and <see cref="OnPowerModeChanged"/>
/// methods marshal to the Dispatcher thread via
/// <see cref="Dispatcher.BeginInvoke"/> so that lifecycle flag changes,
/// suspension updates, and timer ticks are all serialized on a single
/// thread. This prevents race conditions between system callbacks and
/// the timer tick.
/// </para>
/// <para>
/// The rest-reminder intent is exposed via <see cref="RestReminderRequested"/>
/// and the <see cref="ReminderFiredCount"/> counter for test verification.
/// </para>
/// </summary>
public sealed class ActivitySessionService : IDisposable
{
    private readonly ActivitySession _session;
    private readonly InputIdleSampler _idleSampler;
    private readonly DispatcherTimer _timer;
    private readonly MonotonicClock _clock;
    private readonly Dispatcher? _dispatcher;
    private bool _isPaused;
    private bool _isSystemLocked;
    private bool _isSleeping;
    private bool _isStopped;
    private bool _disposed;
    // P4-001: When true, the session needs to be resumed on the next tick
    // using the tick's time value (not the real clock). This decouples
    // lifecycle state changes from the clock domain, making the service
    // testable with controlled time via TickManually.
    private bool _pendingResume;

    /// <summary>
    /// Fires when the rest-reminder intent is triggered (accumulated active
    /// time reached the threshold). Fires at most once per session.
    /// App does NOT show a popup or play an animation in P4-001 — this
    /// event is the testable signal for downstream tasks (P4-002).
    /// </summary>
    public event EventHandler? RestReminderRequested;

    /// <summary>
    /// Number of times the rest-reminder intent has fired since the service
    /// was created. For test verification.
    /// </summary>
    public int ReminderFiredCount { get; private set; }

    /// <summary>
    /// The underlying Core session. Exposed for test verification of
    /// accumulated time and state.
    /// </summary>
    internal ActivitySession Session => _session;

    /// <summary>
    /// F-04 R1: Observable accumulated active time in minutes. For
    /// diagnostic isolation entry — allows external code to read the
    /// current accumulated value without accessing the internal session.
    /// </summary>
    public double AccumulatedActiveMinutes => _session.AccumulatedActiveMinutes;

    /// <summary>
    /// F-04 R1: Observable accumulated active time in milliseconds.
    /// For diagnostic isolation entry.
    /// </summary>
    public double AccumulatedActiveMs => _session.AccumulatedActiveMs;

    /// <summary>
    /// F-04 R1: Whether the rest-reminder has already fired in the
    /// current session. For diagnostic isolation entry.
    /// </summary>
    public bool HasReminderFired => _session.ReminderFired;

    /// <summary>
    /// Whether the session is currently suspended due to any lifecycle
    /// condition (pause, lock, sleep, or explicit Stop).
    /// </summary>
    public bool IsSuspended => _isPaused || _isSystemLocked || _isSleeping || _isStopped;

    /// <summary>
    /// Creates the service with default dependencies. Tests can inject
    /// custom dependencies via the internal constructor.
    /// </summary>
    public ActivitySessionService()
        : this(new ActivitySession(), new InputIdleSampler(), MonotonicClock.Instance, Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// Internal constructor for test injection of a custom session,
    /// idle sampler, and clock.
    /// F-02 R2: The dispatcher parameter captures the current Dispatcher
    /// so system callbacks can marshal to the UI thread. Tests that use
    /// TickManually do not need a Dispatcher and can pass null.
    /// </summary>
    internal ActivitySessionService(
        ActivitySession session,
        InputIdleSampler idleSampler,
        MonotonicClock clock,
        Dispatcher? dispatcher = null)
    {
        _session = session;
        _idleSampler = idleSampler;
        _clock = clock;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// Starts the sampling timer and initialises the session with the
    /// current monotonic baseline. Call this after app startup is complete
    /// and the pet windows are shown.
    /// <para>
    /// F-02 R1: If <paramref name="isPaused"/> is true (the app was
    /// launched from a paused archive), the session is suspended
    /// immediately instead of resumed. This prevents the activity timer
    /// from running while the user has the app paused.
    /// </para>
    /// </summary>
    /// <param name="isPaused">Whether the app is currently in a paused
    /// state (e.g. restored from a paused archive). When true, the
    /// session starts suspended.</param>
    public void Start(bool isPaused = false)
    {
        if (_disposed) return;
        _isStopped = false;
        var nowMs = _clock.GetElapsedMs();
        if (isPaused)
        {
            // F-02 R1: App launched from a paused archive — do NOT
            // resume the session. Suspend immediately so the timer
            // does not accumulate while the user has the app paused.
            _isPaused = true;
            _session.Suspend();
        }
        else
        {
            _session.Resume(nowMs);
        }
        _timer.Start();
    }

    /// <summary>
    /// Stops the sampling timer and suspends the session. Does NOT dispose
    /// — call <see cref="Dispose"/> for full cleanup.
    /// </summary>
    public void Stop()
    {
        _isStopped = true;
        _timer.Stop();
        _session.Suspend();
    }

    /// <summary>
    /// Called by App when the global pause state changes.
    /// When paused, the session is suspended (stops accumulating).
    /// When resumed, the session resumes from the new monotonic baseline.
    /// </summary>
    public void OnPauseToggled(bool paused)
    {
        _isPaused = paused;
        UpdateSuspension();
    }

    /// <summary>
    /// Called when the system session switches (lock/unlock).
    /// <paramref name="reason"/> is the SessionSwitchReason value.
    /// SessionLock → suspend; SessionUnlock → resume.
    /// <para>
    /// F-02 R2: System events fire on a system thread. This method
    /// marshals to the Dispatcher thread (if available) so that
    /// lifecycle flag changes and suspension updates are serialized
    /// with timer ticks. When no Dispatcher is available (tests using
    /// TickManually), the method executes inline.
    /// </para>
    /// </summary>
    public void OnSessionSwitch(int reason)
    {
        if (_dispatcher is not null && _dispatcher.CheckAccess() == false)
        {
            _dispatcher.BeginInvoke(new Action(() => OnSessionSwitchCore(reason)));
            return;
        }
        OnSessionSwitchCore(reason);
    }

    private void OnSessionSwitchCore(int reason)
    {
        // SessionSwitchReason.SessionLock = 7, SessionUnlock = 8.
        if (reason == 7)
            _isSystemLocked = true;
        else if (reason == 8)
            _isSystemLocked = false;
        else
            return;
        UpdateSuspension();
    }

    /// <summary>
    /// Called when the system power mode changes.
    /// <paramref name="mode"/> is the PowerModes value.
    /// Suspend → suspend session; Resume → resume session.
    /// <para>
    /// F-02 R1: Uses the strongly-typed <see cref="PowerModes"/> enum
    /// instead of magic numbers. The actual .NET runtime values are
    /// Suspend=3, Resume=1 (not 4/5 as previously assumed).
    /// </para>
    /// <para>
    /// F-02 R2: System events fire on a system thread. This method
    /// marshals to the Dispatcher thread (if available) so that
    /// lifecycle flag changes and suspension updates are serialized
    /// with timer ticks. When no Dispatcher is available (tests using
    /// TickManually), the method executes inline.
    /// </para>
    /// </summary>
    public void OnPowerModeChanged(int mode)
    {
        if (_dispatcher is not null && _dispatcher.CheckAccess() == false)
        {
            _dispatcher.BeginInvoke(new Action(() => OnPowerModeChangedCore(mode)));
            return;
        }
        OnPowerModeChangedCore(mode);
    }

    private void OnPowerModeChangedCore(int mode)
    {
        // F-02 R1: Use strongly-typed PowerModes enum values instead of
        // magic numbers. The .NET runtime defines:
        //   PowerModes.Suspend = 3
        //   PowerModes.Resume   = 1
        //   PowerModes.StatusChange = 2
        // See: https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.powermodes
        if (mode == (int)PowerModes.Suspend)
            _isSleeping = true;
        else if (mode == (int)PowerModes.Resume)
            _isSleeping = false;
        else
            return;
        UpdateSuspension();
    }

    /// <summary>
    /// Drives one tick of the session manually. Used by tests to advance
    /// the session with controlled time and input values without relying
    /// on the DispatcherTimer.
    /// </summary>
    /// <param name="nowMs">Current monotonic time in milliseconds.</param>
    /// <param name="lastInputMs">Monotonic timestamp of the last user input,
    /// or null for "no input ever".</param>
    /// <returns>True if the rest-reminder intent fired on this tick.</returns>
    internal bool TickManually(double nowMs, double? lastInputMs)
    {
        // If a resume is pending (lifecycle flags cleared), resume the
        // session using this tick's time value before ticking.
        if (_pendingResume)
        {
            _pendingResume = false;
            _session.Resume(nowMs);
        }

        var fired = _session.Tick(nowMs, lastInputMs);
        if (fired)
        {
            ReminderFiredCount++;
            RestReminderRequested?.Invoke(this, EventArgs.Empty);
        }
        return fired;
    }

    /// <summary>
    /// Gets the last input timestamp from the idle sampler, for test
    /// verification of the sampling path.
    /// F-03 R2: Uses the parameterless overload which reads
    /// MonotonicClock internally. The production timer path uses
    /// <see cref="GetLastInputMs(double)"/> with the shared nowMs.
    /// </summary>
    internal double? GetLastInputMs() => _idleSampler.GetLastInputMs();

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsSuspended)
            return;

        // F-03 R2: Read the monotonic clock ONCE and use this same
        // snapshot for both the idle sampler and the session tick.
        // Previously the sampler read MonotonicClock internally,
        // producing a slightly different (newer) timestamp than the
        // tick's nowMs. When the user had a fresh age=0 input, the
        // sampler's lastInputMs ended up newer than nowMs and was
        // rejected as a "future" timestamp, causing valid zero-idle
        // samples to accumulate nothing.
        var nowMs = _clock.GetElapsedMs();

        // If a resume is pending, resume using the real clock time.
        if (_pendingResume)
        {
            _pendingResume = false;
            _session.Resume(nowMs);
        }

        // F-03 R2: Pass the same nowMs to the sampler so the input
        // timestamp and the tick time share one clock snapshot.
        var lastInputMs = _idleSampler.GetLastInputMs(nowMs);
        var fired = _session.Tick(nowMs, lastInputMs);
        if (fired)
        {
            ReminderFiredCount++;
            RestReminderRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Recalculates suspension state based on all lifecycle flags.
    /// If any flag is active, the session is suspended immediately.
    /// When all flags clear, a resume is deferred to the next tick
    /// (TickManually or OnTimerTick) so the session resumes with the
    /// correct time domain (test time vs real clock time).
    /// </summary>
    private void UpdateSuspension()
    {
        if (IsSuspended)
        {
            _session.Suspend();
            _pendingResume = false;
        }
        else
        {
            // Defer resume to the next tick to use the correct time domain.
            _pendingResume = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _session.Suspend();
    }
}
