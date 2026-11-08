using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.App.Platform;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Emotion;
using DesktopPet.Core.Events;
using DesktopPet.Core.Geometry;
using DesktopPet.Core.Input;
using DesktopPet.Core.Physics;

// Avoid WPF/WinForms ambiguity: use explicit namespace for Color, Brushes, Point, MouseEventArgs.
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace DesktopPet.App;

/// <summary>
/// Transparent topmost pet window.
/// P1-001: Uses PNG frame assets loaded via AnimationPlayer instead of program-drawn shapes.
/// Supports body drag, click-through transparent areas, and no-focus interaction.
/// </summary>
public partial class MainWindow : System.Windows.Window
{
    // P1-001: Animation player and shared update timer.
    private readonly DispatcherTimer _updateTimer;
    private readonly AnimationPlayer _animPlayer;
    private readonly IdleBlinkScheduler? _idleBlink;
    private readonly DragReleaseFeedback? _dragRelease;
    private readonly CuriosityReaction _curiosity = new();
    private readonly PetCharacterDef _character;
    private readonly Dictionary<string, BitmapImage> _imageCache = [];
    private readonly string _assetDir;
    private string _lastDisplayedFrame = string.Empty;
    private readonly HashSet<string> _missingActionWarnings = [];

    private bool _isDragging;
    private System.Windows.Point _dragOffset;
    private Point _pointerPressScreenDip;

    // P2-003 F-01: Monotonic timestamp captured at pointer-down. Used as the
    // baseline sample time for StartDragging so that head-press→convert-to-drag
    // preserves the real press-down position and time, not the conversion
    // moment. Without this, the baseline and first Move share the same
    // timestamp and Core's same-time rejection drops the first displacement.
    private double _pointerPressTimeMs;

    // P2-003: Release speed sampler for throw/gentle-release detection.
    // Fed with foot-center physical pixels + monotonic ms during drag.
    // P2-004: Diagnostics capture enabled so the live observation host can
    // record actual samples, segment indices, and decision reason for
    // short-drag non-trigger analysis. Zero overhead when no one reads Diag.
    private readonly ReleaseSpeedSampler _speedSampler = new() { CaptureDiagnostics = true };
    private ReleaseResult _lastReleaseResult;

    // P2-004: Frozen snapshot of RecordSample attempts from the most recent
    // completed drag. Copied before _speedSampler.Reset() in EndDrag so the
    // observation host can read the full attempt log (accepted + rejected)
    // after release, even though Reset clears the live AttemptLog. While a
    // drag is in progress, LastSampleAttempts returns the live AttemptLog
    // instead so the host can observe attempts in real time.
    private IReadOnlyList<SampleAttempt> _lastSampleAttemptsSnapshot = Array.Empty<SampleAttempt>();

    // P2-003 S2: Physics simulator drives window motion after a throw.
    // Launch is called only on a normal release with ShouldThrow=true;
    // cancellation paths (menu, capture loss, closing) never Launch.
    // OnUpdateTick drives Update and writes Left/Top from the physics state.
    private readonly PhysicsSimulator _physicsSimulator = new();
    private double _lastPhysicsUpdateMs;
    private bool _isPhysicsActive;

    // P1-003: Petting detection state (R-02, D-013).
    // Head zone = upper 1/3 of the pet image (resource-driven zones deferred).
    // Head micro-moves remain a non-scoring gesture; no affinity or score is
    // accumulated. Every visible part can drag. Head micro-moves remain
    // petting only within the press-origin slop; conversion retains capture
    // and the initial anchor.
    private bool _isPettingCandidate;
    private System.Windows.Point _pettingLastPos;
    private const double PettingMaxMovePerFramePx = 8.0; // small movement threshold in DIP
    private const double HeadDragSlopDip = 8.0;
    private const double HeadZoneRatio = 1.0 / 3.0; // upper 1/3 is head

    // P1-003: Grievance expression state (R-02).
    // Expression lasts 10 seconds, then resets grievance to neutral.
    // Drag/petting/menu interrupts expression: clears pending, resets grievance.
    private bool _isExpressingGrievance;
    private double _grievanceExpressionStartMs;
    private const double GrievanceExpressionDurationMs = 10000.0;

    // F-02: Tracks whether the app is in a lifecycle-level pause (tray Pause).
    // When true, action switches from drag/menu must not implicitly resume
    // the animation player; the player stays paused until the user resumes.
    private bool _isAppPaused;

    // R-01: Only dwell/alpha-aware hover remains; fast-mouse reactions were removed.
    private readonly HoverDetector _hoverDetector = new();

    // Test injection points for controlled mouse position and clock.
    // When non-null, OnUpdateTick uses these instead of the Win32 defaults.
    // Production code leaves these null — no behavior change.
    internal Func<(int x, int y)>? TestGetCursorPos;
    internal Func<double>? TestGetElapsedMs;

    // G-03 R4: Test injection point for controlled UTC time source.
    // When non-null, OnUpdateTick uses this instead of DateTime.UtcNow
    // so tests can use a single consistent time base for both monotonic
    // clock (TestGetElapsedMs) and UTC (TestGetUtcNow), avoiding the
    // divergence between Thread.Sleep-driven real UTC and injected clockMs.
    internal Func<DateTime>? TestGetUtcNow;

    // P1-003: Test injection points for emotion objects (G-01/G-02 runtime tests).
    // When non-null, OnUpdateTick uses these instead of App.Current.
    // Production code leaves these null — OnUpdateTick falls back to App.Current.
    internal EmotionDecayAccumulator? TestEmotionAccumulator;
    internal CornerTracker? TestCornerTracking;
    internal GrievanceExpressionState? TestGrievanceExpression;

    // P1-004: Test injection points for rare-event objects.
    // When non-null, OnUpdateTick uses these instead of App.Current.
    internal EffectiveRuntimeTracker? TestRuntimeTracker;
    internal RareEventScheduler? TestRareEventScheduler;

    // PREP-002: Explicit state context supplied by App in production.
    // When non-null, OnUpdateTick and other methods read shared state objects
    // and invoke the save-dirty callback from here instead of reaching back
    // through Application.Current. Test injection fields (Test*) still take
    // highest priority; Application.Current is the final fallback for tests
    // that use the parameterless constructor without setting StateContext.
    internal PetStateContext? StateContext;

    // P2-003 S3 R5 F-08: Test injection point for monitor list.
    // When non-null, BuildWorkArea uses this instead of MonitorInterop.EnumerateMonitors().
    // Set to an empty list to simulate "no display available" without Width hacks.
    internal List<DesktopPet.Core.Geometry.MonitorInfo>? TestMonitorList;

    // F-08: Track the last-known rare-event plan state so we only call
    // MarkStateDirty when the plan actually changes (new plan created,
    // date changed, or Consumed flag flipped). This avoids marking dirty
    // every tick, which would prevent the 5-second debounce from ever
    // completing.
    private string? _lastRareEventPlanDate;
    private bool? _lastRareEventPlanConsumed;

    // P1-004: Probe (peek) visual state for rare-event ShouldFire.
    // When active, the pet window moves to the nearest screen edge and
    // plays the curious expression. After the duration elapses, the
    // window returns to its original position. Drag interrupts probe
    // without restoring the original position (drag takes over position).
    private bool _isProbing;
    private double _probeStartMs;
    private const double ProbeDurationMs = 3000.0;
    private double _probeOriginalLeft;
    private double _probeOriginalTop;

    // Win32 constants for no-activate window (F-01) and native popup menu.
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 0x0003;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_NULL = 0x0000;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;
    private const int TPM_NONOTIFY = 0x0080;

    // WM_DISPLAYCHANGE: fired when monitor layout changes (connect/disconnect/resolution).
    private const int WM_DISPLAYCHANGE = 0x007E;

    // WM_SETTINGCHANGE: fired when system parameters change (e.g. work area).
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int SPI_SETWORKAREA = 0x002F;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    public MainWindow()
    {
        InitializeComponent();
        Left = 100;
        Top = 100;

        // P1-001: Load character definition and initialize animation player.
        _assetDir = FindAssetDirectory();
        var character = LoadCharacter(_assetDir);
        _character = character;
        if (character.Actions.TryGetValue("blink", out var blink))
            _idleBlink = new IdleBlinkScheduler(blink);
        if (character.Actions.TryGetValue("release", out var release))
            _dragRelease = new DragReleaseFeedback(release);
        _animPlayer = new AnimationPlayer(character, GetElapsedMs);
        _animPlayer.OnActionMissing += OnActionMissing;
        _animPlayer.ActionCompleted += OnActionCompleted;

        // P1-001: Preload all PNG frames into cache.
        // F-03: Wrap in try/catch so bad PNGs don't crash the constructor.
        try
        {
            PreloadImages(character, _assetDir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Failed to preload images: {0}", ex.Message);
        }

        // P1-001: Display initial frame.
        UpdateFrame();

        // P1-001: Shared update timer at ~30 FPS (33ms).
        // This is the single animation update entry point per ARCHITECTURE §5.
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _updateTimer.Tick += OnUpdateTick;
        _updateTimer.Start();
    }

    // PREP-002: Centralised save-dirty notification. Replaces the scattered
    // "Application.Current is App app → app.MarkStateDirty()" pattern.
    // Priority: StateContext.MarkStateDirty (production, explicitly injected
    // by App) → Application.Current fallback (for tests that use the
    // parameterless constructor without StateContext).
    private void NotifyStateDirty()
    {
        if (StateContext?.MarkStateDirty is not null)
            StateContext.MarkStateDirty.Invoke();
        else if (System.Windows.Application.Current is App app)
            app.MarkStateDirty();
    }

    // PREP-002 F-01: Per-object three-tier resolution helpers.
    // Each state object is independently resolved: Test* field (test injection)
    // → StateContext member (production injection) → App.Current member
    // (parameterless-constructor fallback). No object's null/non-null status
    // controls another object's resolution path.
    private static T? Resolve<T>(T? test, T? context, Func<App, T?> getApp) where T : class
    {
        if (test is not null) return test;
        if (context is not null) return context;
        if (System.Windows.Application.Current is App app)
            return getApp(app);
        return null;
    }

    // P1-001: Monotonic time source for AnimationPlayer.
    // P2-004 F-01: Use high-resolution Stopwatch instead of Environment.TickCount64.
    // PREP-002: The actual clock logic now lives in the shared
    // MonotonicClock class (Platform/MonotonicClock.cs). MainWindow keeps a
    // thin local wrapper so that AnimationPlayer, OnUpdateTick and test
    // injection (TestGetElapsedMs) all read from the same shared instance.
    private static double GetElapsedMs() => MonotonicClock.Instance.GetElapsedMs();

    /// <summary>
    /// P1-004: Public accessor for the monotonic elapsed-milliseconds value.
    /// Used by App.OnPauseToggled to convert to monotonic minutes for
    /// EffectiveRuntimeTracker Start/EndActivePeriod calls.
    /// PREP-002: Retained for backward compatibility — tests in
    /// HighResolutionTimingTests call this method directly. Internally
    /// delegates to the shared MonotonicClock.
    /// </summary>
    public static double GetElapsedMsPublic() => GetElapsedMs();

    private static string FindAssetDirectory()
    {
        // Prefer the approved bear; retain the original prototype as fallback.
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, "assets", "pets", "bear"),
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "assets", "pets", "bear"),
            Path.Combine(exeDir, "..", "..", "..", "..", "assets", "pets", "bear"),
            Path.Combine(exeDir, "assets", "pets", "default"),
            Path.Combine(exeDir, "..", "..", "..", "..", "..", "assets", "pets", "default"),
            Path.Combine(exeDir, "..", "..", "..", "..", "assets", "pets", "default")
        };
        foreach (var c in candidates)
            if (File.Exists(Path.Combine(c, "character.json")))
                return c;
        return candidates[0]; // fallback, will produce load error
    }

    private static PetCharacterDef LoadCharacter(string assetDir)
    {
        var jsonPath = Path.Combine(assetDir, "character.json");
        if (!File.Exists(jsonPath))
        {
            System.Diagnostics.Trace.TraceError("Character JSON not found: {0}", jsonPath);
            return CreateBuiltinFallbackCharacter();
        }
        try
        {
            var json = File.ReadAllText(jsonPath);
            return CharacterLoader.Load(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Failed to load character: {0}", ex.Message);
            return CreateBuiltinFallbackCharacter();
        }
    }

    /// <summary>
    /// Minimal fallback character with a single idle frame if assets are missing.
    /// </summary>
    private static PetCharacterDef CreateBuiltinFallbackCharacter()
    {
        return new PetCharacterDef
        {
            Id = "fallback",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "idle_0.png", DurationMs = 1000 }]
                }
            }
        };
    }

    /// <summary>
    /// F-03: Generates a visible built-in fallback bitmap when all PNG resources
    /// are unavailable. Draws a simple colored silhouette so the pet window is
    /// not invisible. Not final art — just a diagnostic placeholder.
    /// </summary>
    private static BitmapSource CreateBuiltinFallbackBitmap()
    {
        const int size = 120;
        var dpi = 96.0;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Draw a simple body silhouette: rounded rectangle with eyes.
            var bodyRect = new Rect(20, 15, 80, 90);
            var bodyBrush = new SolidColorBrush(MediaColor.FromArgb(200, 100, 150, 220));
            dc.DrawRoundedRectangle(bodyBrush, null, bodyRect, 15, 15);

            // Eyes.
            var eyeBrush = MediaBrushes.White;
            dc.DrawEllipse(eyeBrush, null, new System.Windows.Point(45, 45), 8, 8);
            dc.DrawEllipse(eyeBrush, null, new System.Windows.Point(75, 45), 8, 8);
            dc.DrawEllipse(MediaBrushes.Black, null, new System.Windows.Point(45, 45), 3, 3);
            dc.DrawEllipse(MediaBrushes.Black, null, new System.Windows.Point(75, 45), 3, 3);
        }
        var bmp = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private void PreloadImages(PetCharacterDef character, string assetDir)
    {
        var baseDir = Path.GetFullPath(assetDir);
        // F-03: Ensure baseDir ends with separator for strict prefix comparison,
        // so sibling directories with a shared prefix (e.g. "pet" vs "pet-sibling")
        // are not accepted.
        if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
            baseDir += Path.DirectorySeparatorChar;

        foreach (var action in character.Actions.Values)
        {
            foreach (var frame in action.Frames)
            {
                var key = frame.ImagePath;
                if (_imageCache.ContainsKey(key))
                    continue;

                // F-03: Validate resolved path stays within the asset directory.
                var fullPath = Path.GetFullPath(Path.Combine(assetDir, frame.ImagePath));
                if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Trace.TraceWarning("Frame image path escapes asset directory: {0}", frame.ImagePath);
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(fullPath, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();

                        // F-03: Verify decoded PNG dimensions match JSON declaration.
                        if (bmp.PixelWidth != character.FrameWidth || bmp.PixelHeight != character.FrameHeight)
                        {
                            System.Diagnostics.Trace.TraceWarning(
                                "Frame image '{0}' decoded size {1}x{2} does not match declared {3}x{4}; skipping.",
                                fullPath, bmp.PixelWidth, bmp.PixelHeight,
                                character.FrameWidth, character.FrameHeight);
                            continue;
                        }

                        _imageCache[key] = bmp;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceWarning("Failed to load frame image '{0}': {1}", fullPath, ex.Message);
                    }
                }
                else
                {
                    System.Diagnostics.Trace.TraceWarning("Frame image not found: {0}", fullPath);
                }
            }
        }
    }

    private void OnActionMissing(string actionId)
    {
        // F-03: Deduplicate missing-action warnings per §8 "记录一次".
        if (_missingActionWarnings.Add(actionId))
            System.Diagnostics.Trace.TraceWarning("Action '{0}' not found, falling back to idle.", actionId);
    }

    private void OnActionCompleted(string actionId)
    {
        // When a registered Once expression completes, return to idle.
        if (actionId != "idle" && !_isDragging)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_animPlayer.CurrentActionId == actionId && _animPlayer.IsCompleted)
                    _animPlayer.PlayAction("idle");
            }));
        }
    }

    /// <summary>
    /// F-01: Apply WS_EX_NOACTIVATE after window handle is created,
    /// so clicking the pet never steals foreground from the user's active window.
    /// </summary>
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_LAYERED);
    }

    /// <summary>
    /// F-01: Intercept WM_MOUSEACTIVATE to return MA_NOACTIVATE,
    /// preventing activation on mouse click as a second-layer guard.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = System.Windows.Interop.HwndSource.FromHwnd(
            new System.Windows.Interop.WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);

        // P2-003 F-02: Hook WPF DpiChanged event so moving the window to a
        // different-DPI monitor clears stale sample coordinates. This covers
        // per-monitor DPI changes that do NOT trigger WM_DISPLAYCHANGE.
        DpiChanged += MainWindow_DpiChanged;
    }

    /// <summary>
    /// P2-003 F-02: WPF DpiChanged handler. Clears the drag sample context
    /// so old-DPI physical coordinates are never subtracted from new-DPI
    /// coordinates. Production and test share this path via NotifyDpiChanged.
    /// </summary>
    private void MainWindow_DpiChanged(object? sender, System.Windows.DpiChangedEventArgs e)
    {
        NotifyDpiChanged();
    }

    /// <summary>
    /// P2-003 F-02: Clear drag sample context on DPI change. Called by the
    /// WPF DpiChanged event handler in production and directly by tests.
    /// After clearing, a fresh baseline is needed before a throw can occur.
    /// </summary>
    internal void NotifyDpiChanged()
    {
        _speedSampler.ClearSampleContext();
    }

    private IntPtr _hwnd;
    private bool _isContextMenuOpen;
    private const uint MenuIdExit = 1;
    private const uint MenuIdRecover = 2;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE && !_isContextMenuOpen)
        {
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }

        // F-02: Handle monitor layout changes (connect/disconnect/resolution).
        // Re-enumerate monitors and clamp the pet to a visible work area.
        if (msg == WM_DISPLAYCHANGE)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(OnDisplayLayoutChanged));
            return IntPtr.Zero;
        }

        // F-02: Handle work area changes (e.g. taskbar height/position changed).
        // WM_SETTINGCHANGE with wParam==SPI_SETWORKAREA means the work area
        // changed without a display layout change. Re-clamp the pet to the
        // updated work area so it doesn't end up behind the taskbar.
        if (msg == WM_SETTINGCHANGE && wParam.ToInt32() == SPI_SETWORKAREA)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(OnDisplayLayoutChanged));
            return IntPtr.Zero;
        }

        // Keep a single native menu path. Only an explicit right-click may
        // temporarily activate the owner; ordinary pet clicks remain nonactivating.
        if (msg == WM_RBUTTONUP)
        {
            handled = true;

            // TrackPopupMenuEx needs screen coordinates. Read the cursor directly
            // instead of mixing WPF DIP/client coordinates with native pixels.
            if (GetCursorPos(out var point))
                ShowNativeContextMenu(point);

            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void ShowNativeContextMenu(POINT screenPoint)
    {
        if (_isContextMenuOpen)
            return;

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            System.Diagnostics.Trace.TraceWarning("CreatePopupMenu failed: {0}", Marshal.GetLastPInvokeError());
            return;
        }

        var previousForeground = GetForegroundWindow();
        int originalStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        uint command = 0;
        _isContextMenuOpen = true;
        EndDrag(isCancellation: true);

        // Reset hover when opening the menu.
        _hoverDetector.Reset();

        try
        {
            if (!AppendMenu(menu, 0, (UIntPtr)MenuIdRecover, "Recover"))
            {
                System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                return;
            }

            if (!AppendMenu(menu, 0, (UIntPtr)MenuIdExit, "Exit"))
            {
                System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                return;
            }

            Marshal.SetLastPInvokeError(0);
            int previousStyle = SetWindowLong(_hwnd, GWL_EXSTYLE, originalStyle & ~WS_EX_NOACTIVATE);
            if (previousStyle == 0 && Marshal.GetLastPInvokeError() != 0)
            {
                System.Diagnostics.Trace.TraceWarning("Cannot activate menu owner: {0}", Marshal.GetLastPInvokeError());
                return;
            }

            // Removing NOACTIVATE alone does not make the owner foreground.
            // Without this step, a native popup may ignore outside clicks.
            if (!SetForegroundWindow(_hwnd) && GetForegroundWindow() != _hwnd)
            {
                System.Diagnostics.Trace.TraceWarning("Windows refused foreground activation for the context menu.");
                return;
            }

            command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                screenPoint.X, screenPoint.Y, _hwnd, IntPtr.Zero);
        }
        finally
        {
            // Preserve unrelated style bits and restore normal no-activate input
            // even if menu creation/activation fails or the user cancels it.
            int currentStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE,
                (currentStyle & ~WS_EX_NOACTIVATE) | (originalStyle & WS_EX_NOACTIVATE));
            _isContextMenuOpen = false;
            DestroyMenu(menu);

            // Start a fresh hover dwell after the menu closes.
            _hoverDetector.Reset();

            // Complete the native popup lifecycle, including repeated invocations.
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            // Restore the prior app only if the menu owner still has foreground.
            // Never steal focus back from a window the user clicked while dismissing.
            if (GetForegroundWindow() == _hwnd && previousForeground != _hwnd &&
                previousForeground != IntPtr.Zero && IsWindow(previousForeground))
                SetForegroundWindow(previousForeground);
        }

        if (command == MenuIdExit)
            Dispatcher.BeginInvoke(new Action(Close));
        else if (command == MenuIdRecover)
            Dispatcher.BeginInvoke(new Action(RecoverPet));
    }

    /// <summary>
    /// P1-001: Shared update tick — the single animation update entry point.
    /// Called by the DispatcherTimer at ~30 FPS.
    /// Reads the current cursor position for alpha-aware hover. Drag, petting,
    /// menu, pause and grievance expression suppress and reset hover.
    /// </summary>
    private void OnUpdateTick(object? sender, EventArgs e)
    {
        var nowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();

        // P2-003 S2: Drive physics simulation after a throw release.
        // While physics is active, advance the simulator and write the
        // resulting foot-center position back to Left/Top. This owns the
        // window position during the throw — no other branch writes
        // Left/Top while physics is active.
        // P2-003 S3 R5 F-07: Also exclude _isPettingCandidate so physics
        // does not drive the window away from the cursor during a head press.
        // P2-003 S3 R6 F-05: Also exclude _isAppPaused so that even if tick
        // is called directly during pause, physics does not advance or write
        // position. Stopping the DispatcherTimer is not sufficient — the
        // method itself must guard against direct invocation.
        if (_isPhysicsActive && !_isDragging && !_isPettingCandidate && !_isAppPaused)
        {
            UpdatePhysics(nowMs);
        }

        if (_animPlayer.CurrentActionId != "curious")
            _curiosity.Cancel();

        // Position alone drives hover. No speed history, proximity fright,
        // cooldown, or automatic dodge action remains (D-009).
        // F-09: Probe owns its action (curious) for the full probe lifetime.
        // Low-priority hover/curiosity branches must not override or
        // prematurely end the probe expression. Grievance expression is
        // a higher-priority state that pre-empts both.
        if (!_isDragging && !_isPettingCandidate && !_isContextMenuOpen && !_isAppPaused
            && !_isExpressingGrievance && !_isProbing
            && TryGetMousePosition(out var mouseX, out var mouseY))
        {
            var petRectPx = GetPetRectPhysicalPx();
            bool isOverPet = IsOverVisiblePet(mouseX, mouseY);
            var hoverResult = _hoverDetector.Update(mouseX, mouseY, petRectPx, nowMs, isOverPet);
            if (hoverResult == HoverTrigger.Triggered && _animPlayer.CurrentActionId != "curious")
            {
                _animPlayer.PlayAction("curious");
                _curiosity.Start(nowMs);
            }
            if (_animPlayer.CurrentActionId == "curious")
            {
                // Adopt a curious frame left by an interruption/diagnostic too;
                // action ownership still prevents replacing another expression.
                if (!_curiosity.IsActive) _curiosity.Start(nowMs);
                if (_curiosity.ShouldEnd(nowMs, isOverPet))
                {
                    _animPlayer.PlayAction("idle");
                    _curiosity.Cancel();
                }
            }
        }
        else
        {
            // Reset detectors during interrupts to avoid stale state.
            // F-09: Probe owns its curious expression for the full lifetime;
            // do not cancel curiosity while probing.
            _hoverDetector.Reset();
            if (!_isAppPaused && !_isProbing) _curiosity.Cancel();
        }

        // P1-003: Advance emotion decay every tick (G-01: use float accumulator).
        // Decay runs even during drag/menu/petting — only paused stops it.
        // The accumulator handles clock rollback internally (F-02).
        // PREP-002 F-01: Each object independently resolved Test* → StateContext → App.
        EmotionDecayAccumulator? emotionAcc = Resolve(
            TestEmotionAccumulator, StateContext?.EmotionAccumulator, a => a.EmotionAccumulator);
        CornerTracker? cornerTrack = Resolve(
            TestCornerTracking, StateContext?.CornerTracking, a => a.CornerTracking);
        GrievanceExpressionState? grievanceExpr = Resolve(
            TestGrievanceExpression, StateContext?.GrievanceExpression, a => a.GrievanceExpression);

        if (!_isAppPaused && emotionAcc is not null)
        {
            // G-03 R4: Use injected UTC time source when available so tests
            // can use a single consistent time base for both monotonic and
            // UTC clocks. Production uses DateTime.UtcNow.
            var utcNow = TestGetUtcNow?.Invoke() ?? DateTime.UtcNow;
            emotionAcc.AdvanceTo(utcNow);

            // P1-003: Corner tracking — check stay and award grievance (G-02).
            if (cornerTrack is not null)
            {
                if (!_isDragging && !_isPettingCandidate && !_isContextMenuOpen)
                {
                    if (IsInCornerZone())
                    {
                        // Pet is in corner zone — start or continue stay.
                        if (!cornerTrack.IsStayActive)
                            cornerTrack.RecordStayStart(nowMs);

                        // Check if condition is met → award grievance.
                        if (cornerTrack.IsConditionMet(nowMs))
                        {
                            emotionAcc.AddGrievance(20);
                            cornerTrack.Clear();
                            NotifyStateDirty();
                        }
                    }
                    else
                    {
                        // Pet left corner zone — cancel stay.
                        cornerTrack.CancelStay();
                    }
                }
            }

            // P1-003: Check grievance expression on each tick.
            // Mark pending when grievance reaches threshold.
            // Expression fires on next local day when not in an interrupt state.
            // Expression lasts 10 seconds (R-02), then resets grievance to neutral.
            // Drag/petting/menu interrupts expression: clears pending + resets grievance.
            // F-04: Expression only starts on "first encounter" — mouse must be
            // near or over the pet (interaction range), not just anywhere on screen.
            if (grievanceExpr is not null)
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                grievanceExpr.TryMarkPending(today, emotionAcc.GrievanceInt >= EmotionState.GrievanceExpressionThreshold);

                if (_isExpressingGrievance)
                {
                    // Expression in progress — check if 10 seconds have elapsed.
                    if (nowMs - _grievanceExpressionStartMs >= GrievanceExpressionDurationMs)
                    {
                        // Expression complete — reset grievance to neutral.
                        _isExpressingGrievance = false;
                        grievanceExpr.Clear();
                        emotionAcc.ResetGrievanceAfterExpression();
                        NotifyStateDirty();

                        // F-04: Return to idle action after expression completes.
                        _animPlayer.PlayAction("idle");
                    }
                }
                else if (grievanceExpr.ShouldExpress(today)
                    && !_isDragging && !_isPettingCandidate && !_isContextMenuOpen)
                {
                    // F-04: Check "first encounter" — mouse must be in interaction range.
                    // In test mode (TestGetCursorPos set), use the injected position.
                    // In production, use GetCursorPos. If we can't determine position,
                    // err on the side of allowing expression (better to express than
                    // to silently swallow a pending grievance).
                    bool mouseInRange = true; // default: allow if we can't check
                    if (TestGetCursorPos is not null)
                    {
                        var (mx, my) = TestGetCursorPos.Invoke();
                        mouseInRange = IsMouseInInteractionRange(mx, my);
                    }
                    else if (GetCursorPos(out var cursorPt))
                    {
                        mouseInRange = IsMouseInInteractionRange(cursorPt.X, cursorPt.Y);
                    }

                    if (mouseInRange)
                    {
                        // F-09: Grievance is higher priority than probe.
                        // Set the grievance flag first so that EndProbe
                        // restores the original position WITHOUT switching
                        // to idle (grievance owns the action now).
                        _isExpressingGrievance = true;
                        _grievanceExpressionStartMs = nowMs;

                        // If a probe is active, safely end it — restore the
                        // original position (grievance should play at the
                        // pet's normal spot, not at the screen edge).
                        // EndProbe sees _isExpressingGrievance=true and
                        // skips the idle switch.
                        if (_isProbing)
                        {
                            EndProbe(restoreOriginal: true, nowMs);
                        }

                        // Static aggrieved bear expression; turning-away motion is deferred.
                        // Expression will last 10 seconds, then resets grievance.
                        _animPlayer.PlayAction("grievance");
                    }
                }
            }
        }

        // P1-004: Advance effective runtime tracker and evaluate rare-event
        // scheduler every tick. Only runs when not paused (same gate as emotion).
        // PREP-002 F-01: Each object independently resolved Test* → StateContext → App.
        // PREP-002 S3: Plan evaluation and runtime advancement extracted to
        // RareEventUpdateService. Probe start/timeout and save notification
        // remain here (position ownership + unified save entry).
        EffectiveRuntimeTracker? runtimeTracker = Resolve(
            TestRuntimeTracker, StateContext?.RuntimeTracker, a => a.RuntimeTracker);
        RareEventScheduler? rareScheduler = Resolve(
            TestRareEventScheduler, StateContext?.RareEventScheduler, a => a.RareEventScheduler);

        if (!_isAppPaused && runtimeTracker is not null && rareScheduler is not null)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");

            var rareResult = RareEventUpdateService.EvaluateTick(
                runtimeTracker,
                rareScheduler,
                nowMs,
                today,
                isDragging: _isDragging,
                isExpressingGrievance: _isExpressingGrievance,
                isPhysicsActive: _isPhysicsActive);

            // F-08: The service signals StateChanged for new plan or consumed.
            // Additionally compare against cached values to detect cross-day
            // drift, avoiding MarkStateDirty every tick (which would reset the
            // debounce timer indefinitely and prevent saving).
            bool stateChanged = rareResult.StateChanged
                || rareResult.PlanDate != _lastRareEventPlanDate
                || rareResult.PlanConsumed != _lastRareEventPlanConsumed;

            if (rareResult.ShouldFire)
            {
                // P1-004: Start probe (peek) visual if not already active and
                // not dragging. The pet moves to the nearest screen edge,
                // plays curious, and returns to original position after
                // ProbeDurationMs. Drag will interrupt probe.
                // F-09: Grievance expression is higher priority than probe.
                // If grievance is currently being expressed, do not start
                // the probe. Per R-03, probe does not start during non-idle
                // states. (Evaluate is skipped during grievance per R10, so
                // ShouldFire should not reach here while grievance is active,
                // but the guard is retained as a secondary safety check.)
                if (!_isProbing && !_isDragging && !_isExpressingGrievance && !_isPhysicsActive)
                {
                    StartProbe(nowMs);
                }
            }

            // P1-004: Check if an active probe has finished.
            if (_isProbing && !_isDragging)
            {
                double elapsed = nowMs - _probeStartMs;
                if (elapsed >= ProbeDurationMs)
                {
                    EndProbe(restoreOriginal: true, nowMs);
                }
            }

            if (stateChanged)
            {
                _lastRareEventPlanDate = rareResult.PlanDate;
                _lastRareEventPlanConsumed = rareResult.PlanConsumed;
                NotifyStateDirty();
            }
        }

        UpdateFrame();
    }

    /// <summary>
    /// P1-002: Get the pet entity rectangle in physical screen pixels.
    /// Used by hover and emotion interaction-range checks.
    /// </summary>
    private PetRectPx GetPetRectPhysicalPx()
    {
        var bounds = PetImageGeometry.OpaqueBounds(PetImage);
        if (bounds.IsEmpty) return new PetRectPx(Left, Top, 0, 0);
        Point Screen(Point local) => PresentationSource.FromVisual(this) is not null
            ? PetImage.PointToScreen(local)
            : PetImage.TransformToAncestor(PetCanvas).Transform(local) + new Vector(Left, Top);
        var first = Screen(bounds.TopLeft);
        var last = Screen(bounds.BottomRight);
        return new PetRectPx(first.X, first.Y, last.X - first.X, last.Y - first.Y);
    }

    private bool TryGetMousePosition(out int x, out int y)
    {
        if (TestGetCursorPos is not null)
        {
            (x, y) = TestGetCursorPos.Invoke();
            return true;
        }
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }
        x = y = 0;
        return false;
    }

    private bool IsOverVisiblePet(double screenX, double screenY)
    {
        var point = new System.Windows.Point(screenX, screenY);
        var local = PresentationSource.FromVisual(this) is not null
            ? PetImage.PointFromScreen(point)
            : PetImage.TransformToAncestor(PetCanvas).Inverse!.Transform(point - new Vector(Left, Top));
        return PetImageGeometry.Contains(PetImage, local);
    }

    /// <summary>
    /// P1-001: Query the AnimationPlayer for the current frame and update the Image control.
    /// Only reloads the image when the frame path actually changes.
    /// </summary>
    private void UpdateFrame()
    {
        var (_, frame) = _animPlayer.GetCurrentFrame();

        // Only replace idle's eye pixels; action identity remains idle, so
        // blinking cannot acquire movement/input ownership or restart hover.
        double expressionNowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        bool idleAvailable = _animPlayer.CurrentActionId == "idle" && !_isDragging && !_isPettingCandidate
            && !_isContextMenuOpen && !_isExpressingGrievance;
        var releaseFrame = _dragRelease?.Update(expressionNowMs, idleAvailable);
        var blinkFrame = _idleBlink?.Update(expressionNowMs, idleAvailable && releaseFrame is null);
        frame = releaseFrame ?? blinkFrame ?? frame;

        // F-04: Apply frame offset to the Image render transform so that
        // non-zero OffsetX/Y in the frame definition compensate for content
        // shifts and keep the visual foot at a stable world position.
        // GetFootOffset returns (OffsetX, OffsetY) directly as the Transform.
        var (offsetX, offsetY) = (frame.OffsetX, frame.OffsetY);
        FrameOffsetTransform.X = offsetX;
        FrameOffsetTransform.Y = offsetY;

        if (frame.ImagePath != _lastDisplayedFrame)
        {
            if (_imageCache.TryGetValue(frame.ImagePath, out var bmp))
            {
                PetImage.Source = bmp;
            }
            else
            {
                // F-03: Frame image missing — try idle's first frame as fallback.
                if (_animPlayer.CurrentActionId != "idle" &&
                    _character.Actions.TryGetValue("idle", out var idleAction) &&
                    idleAction.Frames.Count > 0 &&
                    _imageCache.TryGetValue(idleAction.Frames[0].ImagePath, out var idleBmp))
                {
                    PetImage.Source = idleBmp;
                }
                else if (PetImage.Source is null)
                {
                    // F-03: All resources unavailable (first start with missing/corrupt
                    // assets). Generate a visible built-in fallback bitmap so the pet
                    // is not an invisible transparent window. This is a simple colored
                    // rectangle, not final art.
                    PetImage.Source = CreateBuiltinFallbackBitmap();
                }
                // If Source was already set from a previous valid frame, keep it.
            }
            _lastDisplayedFrame = frame.ImagePath;
        }
    }

    private void PetCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging || _isPettingCandidate)
            return;

        var localPos = e.GetPosition(PetImage);
        if (!PetImageGeometry.Contains(PetImage, localPos))
            return;

        // F-03: Capture first, only enter interaction state if capture succeeds.
        PetCanvas.CaptureMouse();
        if (!PetCanvas.IsMouseCaptured)
            return;

        if (!BeginCapturedPointerInteraction(localPos, PointerScreenDip(e.GetPosition(this))))
            PetCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    // Shared production gesture entry: called only after the event wrapper
    // successfully captures the mouse. Coordinates are supplied once at the UI
    // boundary so tests can exercise the same head/body/anchor logic.
    internal bool BeginCapturedPointerInteraction(Point imagePoint, Point screenDip)
    {
        // F-10: Only a real drag takeover cancels the probe (no position
        // restore).  Transparent/invalid presses and head short-presses that
        // never convert to drag must NOT break the probe or abandon
        // restore-to-original.  Therefore the CancelProbeForDrag call is
        // deferred to StartDragging/ConvertPettingToDrag below — it is NOT
        // done unconditionally here.

        if (_isDragging || _isPettingCandidate || !PetImageGeometry.Contains(PetImage, imagePoint))
            return false;

        bool isHeadPress = IsInHeadZone(imagePoint);
        _dragRelease?.Cancel();
        _pointerPressScreenDip = screenDip;
        _dragOffset = new Point(screenDip.X - Left, screenDip.Y - Top);

        // P2-003 F-01: Record the press-down timestamp so StartDragging can
        // use the real press-down time as the baseline sample time. This
        // matters for head-press→convert-to-drag: the conversion happens
        // inside the first Move event, so without this the baseline and the
        // first Move sample would share the same timestamp and Core's
        // same-time rejection would drop the first displacement.
        _pointerPressTimeMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();

        // F-04: Drag/petting start immediately interrupts grievance expression.
        // Per R-02, expression is interrupted when the user starts dragging;
        // this must happen at MouseLeftButtonDown, not at EndDrag.
        if (_isExpressingGrievance)
        {
            InterruptGrievanceExpression();
        }

        if (isHeadPress)
        {
            // P2-003 S3 R5 F-07: Stop active physics when the user presses
            // the head zone. Without this, physics keeps driving the window
            // away from the cursor during a head press. ForceStop zeroes
            // velocity and sets IsResting; _isPhysicsActive is cleared so
            // OnUpdateTick stops driving physics. The head press keeps its
            // tolerance, original grab offset, capture, and short-press rules.
            if (_isPhysicsActive)
            {
                _isPhysicsActive = false;
                _physicsSimulator.ForceStop();
            }

            // Start petting candidate — may convert to drag if movement exceeds threshold.
            // F-10: A head short-press does NOT cancel the probe.  Only when
            // the head press converts to an actual drag (ConvertPettingToDrag)
            // does CancelProbeForDrag fire.  If the user releases without
            // exceeding the slop, EndPetting runs and the probe continues
            // its remaining lifetime, eventually restoring the original position.
            _isPettingCandidate = true;
            _pettingLastPos = imagePoint;
            _hoverDetector.Reset();
            UpdateFrame();
            return true;
        }

        // Body press → immediate drag → cancel probe now.
        StartDragging();
        return true;
    }

    private Point PointerScreenDip(Point windowPoint)
    {
        var screenPx = PointToScreen(windowPoint);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        return transform.Transform(screenPx);
    }

    private void StartDragging()
    {
        // F-10: Only a real drag takeover cancels the probe without restore.
        // This covers body-press-immediate-drag and head-press-convert-to-drag.
        CancelProbeForDrag();
        _dragRelease?.Cancel();
        // P2-003 S2: Stop any active physics when the user re-grabs the pet.
        _isPhysicsActive = false;
        _physicsSimulator.Reset();
        _isDragging = true;
        _hoverDetector.Reset();
        _animPlayer.PlayAction("drag");
        UpdateFrame();

        // P2-003 F-01: Clear any stale samples from a previous drag session,
        // then record the press-down position as the baseline sample so
        // a single fast Move event already has 2 samples (press + move)
        // and can produce a throw — no need for two Move events.
        //
        // Use _pointerPressTimeMs (set in BeginCapturedPointerInteraction) as
        // the baseline time. For body-press the press time ≈ now, so behavior
        // is unchanged. For head-press→convert-to-drag the press time is
        // earlier than the conversion Move, so the baseline sample and the
        // first Move sample have different timestamps and Core's same-time
        // rejection does not drop the first displacement.
        _speedSampler.Reset();
        RecordFootSample(_pointerPressTimeMs);
    }

    /// <summary>
    /// P2-003 F-01: Record the current foot-center position as a sampler
    /// sample. Shared by StartDragging (baseline) and EndDrag (stationary
    /// detection probe). Uses the same coordinate path as
    /// ProcessCapturedPointerMove so production and test share one pipeline.
    /// </summary>
    private void RecordFootSample(double nowMs, string source = "baseline")
    {
        var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
        var foot = CoordinateTransform.WindowTopLeftToFootCenter(
            Left, Top, Width, Height, dpiX, dpiY);
        _speedSampler.RecordSample(foot.FootCenterX, foot.FootCenterY, nowMs, source);
    }

    private void PetCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPettingCandidate && !_isDragging)
            return;
        ProcessCapturedPointerMove(e.GetPosition(PetImage), PointerScreenDip(e.GetPosition(this)),
            e.LeftButton == MouseButtonState.Pressed, TestGetElapsedMs?.Invoke() ?? GetElapsedMs());
        e.Handled = true;
    }

    internal void ProcessCapturedPointerMove(Point imagePoint, Point screenDip, bool leftPressed, double nowMs)
    {
        if (!leftPressed)
        {
            EndCapturedPointerInteraction();
            return;
        }
        if (_isPettingCandidate)
        {
            // Compare against the ORIGINAL press, not just the previous event:
            // a series of 2-DIP movements must eventually become a drag too.
            if ((screenDip - _pointerPressScreenDip).Length > HeadDragSlopDip
                || (imagePoint - _pettingLastPos).Length > PettingMaxMovePerFramePx
                || !IsInHeadZone(imagePoint))
            {
                ConvertPettingToDrag();
            }
            else
            {
                // D-013: Non-scoring gesture — only update last position for
                // drag-conversion slop checks. No score or cooldown accumulated.
                _pettingLastPos = imagePoint;
                return;
            }
        }
        if (_isDragging)
        {
            // Preserve the anchor captured on mouse-down, including the first
            // conversion movement; do not require a second move to catch up.
            Left = screenDip.X - _dragOffset.X;
            Top = screenDip.Y - _dragOffset.Y;

            // P2-003: Feed the release speed sampler with foot-center
            // physical pixels + monotonic ms for throw/gentle-release detection.
            var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
            var foot = CoordinateTransform.WindowTopLeftToFootCenter(
                Left, Top, Width, Height, dpiX, dpiY);
            _speedSampler.RecordSample(foot.FootCenterX, foot.FootCenterY, nowMs, "move");
        }
    }

    private void PetCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndCapturedPointerInteraction(isCancellation: false);
        e.Handled = true;
    }

    internal void EndCapturedPointerInteraction(bool isCancellation = false)
    {
        if (_isPettingCandidate)
        {
            EndPetting();
        }
        else if (_isDragging)
        {
            EndDrag(isCancellation);
        }
    }

    /// <summary>
    /// F-04: Interrupt grievance expression. Clears pending, resets grievance
    /// to neutral (50), stops expression flag, and returns to idle action.
    /// Called at drag start (MouseLeftButtonDown) and at EndDrag for safety.
    /// </summary>
    private void InterruptGrievanceExpression()
    {
        if (!_isExpressingGrievance)
            return;

        _isExpressingGrievance = false;

        // PREP-002 F-01: Each object independently resolved Test* → StateContext → App.
        var grievanceExpr = Resolve(
            TestGrievanceExpression, StateContext?.GrievanceExpression, a => a.GrievanceExpression);
        var emotionAcc = Resolve(
            TestEmotionAccumulator, StateContext?.EmotionAccumulator, a => a.EmotionAccumulator);
        grievanceExpr?.Clear();
        emotionAcc?.ResetGrievanceAfterExpression();
        NotifyStateDirty();

        // F-04: Return to idle action after expression is interrupted.
        _animPlayer.PlayAction("idle");
        UpdateFrame();
    }

    /// <summary>
    /// P1-004: Start the probe (peek) visual. Records the current window
    /// position, moves the pet to the nearest screen work-area edge with
    /// ~half the window visible, and plays the curious expression.
    /// Called from OnUpdateTick when ShouldFire is true.
    /// </summary>
    internal void StartProbe(double nowMs)
    {
        _isProbing = true;
        _probeStartMs = nowMs;
        _probeOriginalLeft = Left;
        _probeOriginalTop = Top;

        // Calculate nearest edge probe position.
        var (probeLeft, probeTop) = CalculateProbePosition();
        Left = probeLeft;
        Top = probeTop;

        // Play curious expression for the probe.
        _hoverDetector.Reset();
        _animPlayer.PlayAction("curious");
        UpdateFrame();
    }

    /// <summary>
    /// P1-004: End the probe. If restoreOriginal is true, restore the
    /// window to the position recorded at StartProbe. Called when the
    /// probe duration elapses (restoreOriginal=true) or when drag
    /// interrupts the probe (restoreOriginal=false — drag takes over
    /// position). Notifies save queue on restore.
    /// </summary>
    internal void EndProbe(bool restoreOriginal, double nowMs)
    {
        if (!_isProbing)
            return;

        _isProbing = false;

        if (restoreOriginal)
        {
            Left = _probeOriginalLeft;
            Top = _probeOriginalTop;
            NotifyStateDirty();
        }

        // Return to idle unless dragging has already taken over.
        // F-09: If grievance expression is in progress (higher priority),
        // do NOT switch to idle — the grievance expression owns the action
        // and must continue until its 10-second duration completes.
        if (!_isDragging && !_isExpressingGrievance)
        {
            _animPlayer.PlayAction("idle");
            UpdateFrame();
        }
    }

    /// <summary>
    /// P1-004: Cancel an active probe for drag takeover. Does NOT restore
    /// the original position — the drag anchor is calculated from the
    /// current (probe) position. F-10: Called only from StartDragging
    /// (body-immediate-drag and head-convert-to-drag), NOT from
    /// BeginCapturedPointerInteraction entry. Head short-press, micro-move
    /// within slop, and invalid/transparent presses do NOT call this.
    /// </summary>
    internal void CancelProbeForDrag()
    {
        if (_isProbing)
        {
            _isProbing = false;
            // Do NOT restore original position; drag owns position now.
        }
    }

    /// <summary>
    /// P1-004: Calculate the nearest screen edge position for the probe.
    /// Moves the window so that approximately half of it is visible at
    /// the nearest work-area edge (top/bottom/left/right of the current
    /// monitor's work area). Falls back to current position if monitor
    /// info is unavailable.
    /// </summary>
    private (double left, double top) CalculateProbePosition()
    {
        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return (Left, Top);

        // Find the monitor the pet is currently on (by center point in DIP).
        var centerDip = new Point(Left + Width / 2, Top + Height / 2);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var centerPx = transform.Transform(centerDip);
        var cx = (int)centerPx.X;
        var cy = (int)centerPx.Y;

        var monitor = monitors.FirstOrDefault(m => m.FullBounds.Contains(cx, cy));
        if (monitor.DeviceName is null)
            monitor = monitors.FirstOrDefault(m => m.WorkArea.Bounds.Contains(cx, cy));
        if (monitor.DeviceName is null)
            return (Left, Top);

        // Work area bounds in physical pixels — convert to DIP for window positioning.
        var wa = monitor.WorkArea.Bounds;
        var dpiScale = transform.M11 > 0 ? transform.M11 : 1.0; // DIP per pixel = 1 / (pixels per DIP)
        // transformToDevice gives pixels-per-DIP, so DIP = px / scale.
        // But wa is in physical pixels; we need DIP for Left/Top.
        // Actually WPF Left/Top are in DIP, and the transform from device
        // gives us the conversion. Use TransformFromDevice for px→DIP.
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var waLeftDip = fromDevice.Transform(new Point(wa.Left, 0)).X;
        var waTopDip = fromDevice.Transform(new Point(0, wa.Top)).Y;
        var waRightDip = fromDevice.Transform(new Point(wa.Right, 0)).X;
        var waBottomDip = fromDevice.Transform(new Point(0, wa.Bottom)).Y;

        double halfH = Height / 2;
        double halfW = Width / 2;

        // Current center in DIP.
        double curCenterX = Left + Width / 2;
        double curCenterY = Top + Height / 2;

        // Distances to each edge (DIP). Choose the nearest.
        double distTop = System.Math.Abs(curCenterY - waTopDip);
        double distBottom = System.Math.Abs(curCenterY - waBottomDip);
        double distLeft = System.Math.Abs(curCenterX - waLeftDip);
        double distRight = System.Math.Abs(curCenterX - waRightDip);

        double minDist = System.Math.Min(System.Math.Min(distTop, distBottom),
                                          System.Math.Min(distLeft, distRight));

        if (minDist == distTop)
        {
            // Probe from top edge: window center at waTopDip, half above the edge.
            return (Left, waTopDip - halfH);
        }
        else if (minDist == distBottom)
        {
            // Probe from bottom edge: window center at waBottomDip, half below.
            return (Left, waBottomDip - halfH);
        }
        else if (minDist == distLeft)
        {
            // Probe from left edge: window center at waLeftDip, half left of edge.
            return (waLeftDip - halfW, Top);
        }
        else
        {
            // Probe from right edge: window center at waRightDip, half right of edge.
            return (waRightDip - halfW, Top);
        }
    }

    /// <summary>
    /// F-03: Unified drag cleanup entry. Called on mouse up, capture loss,
    /// and detected stale drag state. Releases capture and resets flag.
    /// F-02: After drag ends, clamp the window to a visible work area.
    /// P0-004: Notify App to mark state dirty for debounced save.
    /// P1-001: Return to idle action after drag ends.
    /// P2-003 S2: isCancellation=true for capture loss / menu / closing paths —
    /// these do NOT evaluate throw or Launch physics. Only a normal mouse-up
    /// release (isCancellation=false) may Launch the physics simulator.
    /// </summary>
    private void EndDrag(bool isCancellation = false)
    {
        bool wasDragging = _isDragging;
        _isDragging = false;
        if (PetCanvas.IsMouseCaptured)
            PetCanvas.ReleaseMouseCapture();

        // P2-003: Evaluate release speed to determine throw vs gentle drop.
        // Cancellation paths (capture loss, menu, closing) skip evaluation
        // entirely — the pet stays at its current position with no physics.
        if (wasDragging && !isCancellation)
        {
            var releaseMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();

            // P2-003 F-01: If the user stopped moving and held the pet still
            // before releasing, there will be no MouseMove events during that
            // stationary period. Probe the current position at release time so
            // the sampler sees a real stationary sample pair (same position,
            // later time) and correctly detects the stop → gentle release.
            //
            // The probe is injected only when the gap between the last sample
            // and release exceeds half the sample window. This distinguishes a
            // real hold (e.g. 66ms of stillness) from a normal fast drag where
            // the release follows a few ms after the last Move — in the latter
            // case the motion samples are still fresh and must NOT be masked
            // by a same-position probe.
            if (_speedSampler.HasSamples)
            {
                var config = _speedSampler.Config;
                var gapMs = releaseMs - _speedSampler.NewestSampleTimeMs;
                if (gapMs > config.SampleWindowMs / 2.0)
                {
                    // P2-004: Mark for diagnostics so the snapshot records
                    // that a stationary probe was injected before evaluation.
                    _speedSampler.MarkStationaryProbeInjected();
                    RecordFootSample(releaseMs, "probe");
                }
            }

            _lastReleaseResult = _speedSampler.EvaluateRelease(releaseMs);
            // P2-004: Freeze the attempt log before Reset clears it, so the
            // observation host can read the complete accepted/rejected record
            // after release. This snapshot corresponds to _lastReleaseResult.
            _lastSampleAttemptsSnapshot = _speedSampler.AttemptLog.ToList();
            _speedSampler.Reset();

            // P2-003 S2: Launch physics if the release is a throw.
            // Use the current foot-center position (physical pixels) and the
            // release velocity (px/ms) to initialize the trajectory.
            if (_lastReleaseResult.ShouldThrow)
            {
                var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
                var foot = CoordinateTransform.WindowTopLeftToFootCenter(
                    Left, Top, Width, Height, dpiX, dpiY);
                _physicsSimulator.Launch(
                    foot.FootCenterX, foot.FootCenterY,
                    _lastReleaseResult.VelocityX, _lastReleaseResult.VelocityY);
                _isPhysicsActive = true;
                _lastPhysicsUpdateMs = releaseMs;
            }
        }
        else if (wasDragging && isCancellation)
        {
            // Cancellation: freeze attempt log for diagnostics, then clear
            // samples without evaluating or launching.
            _lastSampleAttemptsSnapshot = _speedSampler.AttemptLog.ToList();
            _speedSampler.Reset();
        }

        // P1-003: Drag interrupts grievance expression (R-02).
        // F-04: Primary interrupt is at MouseLeftButtonDown; this is a safety net.
        InterruptGrievanceExpression();

        // F-02: Constrain the pet to a visible work area after dragging.
        // P2-003 S2: Skip clamp when physics is active — the physics
        // simulator handles boundary collisions. Clamp only for gentle
        // release and cancellation paths.
        if (!_isPhysicsActive)
            ClampWindowToWorkArea();

        // P1-003: Record corner drag if the pet was released in a corner zone.
        // PREP-002 F-01: Independent resolution Test* → StateContext → App.
        var cornerTrack = Resolve(
            TestCornerTracking, StateContext?.CornerTracking, a => a.CornerTracking);
        if (cornerTrack is not null)
        {
            var nowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
            if (IsInCornerZone())
            {
                cornerTrack.RecordCornerDrag(nowMs);
            }
            else
            {
                // Left corner zone — cancel any pending stay.
                cornerTrack.CancelStay();
            }
        }

        // P0-004: Mark state dirty so position is persisted.
        NotifyStateDirty();

        // Input and position stop immediately. Only the face settles briefly.
        _animPlayer.PlayAction("idle");
        if (wasDragging && !_isContextMenuOpen)
            _dragRelease?.Start(TestGetElapsedMs?.Invoke() ?? GetElapsedMs());
        UpdateFrame();

        // Start a fresh hover dwell after dragging.
        _hoverDetector.Reset();
    }

    /// <summary>
    /// P2-003 S2: Advance the physics simulator and write the resulting
    /// foot-center position back to window Left/Top. Called from
    /// OnUpdateTick while _isPhysicsActive is true.
    ///
    /// When the simulator reports IsResting, physics is deactivated and
    /// the final position is marked dirty for persistence.
    /// </summary>
    private void UpdatePhysics(double nowMs)
    {
        var elapsedMs = nowMs - _lastPhysicsUpdateMs;
        _lastPhysicsUpdateMs = nowMs;

        var area = BuildWorkArea();
        var result = _physicsSimulator.Update(elapsedMs, area);

        if (result.Rejected)
        {
            // F-04: Rejected (invalid work area / invalid dt) must not leave
            // physics active indefinitely. Stop the simulation and reset so
            // subsequent ticks don't repeat the rejection and ClampWindowToWorkArea
            // resumes normal clamping. Do not write invalid coordinates to Left/Top.
            _isPhysicsActive = false;
            _physicsSimulator.Reset();
            return;
        }

        // Convert physics foot-center (physical px) → DIP Left/Top.
        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count > 0)
        {
            var monitor = MonitorLocator.FindMonitor(monitors, result.State.X, result.State.Y)
                          ?? MonitorLocator.FindNearestMonitor(monitors, result.State.X, result.State.Y);
            var (leftDip, topDip) = CoordinateTransform.FootCenterToWindowTopLeft(
                new PetWorldPosition(result.State.X, result.State.Y),
                Width, Height, monitor.DpiScaleX, monitor.DpiScaleY);
            Left = leftDip;
            Top = topDip;
        }

        if (result.IsResting)
        {
            _isPhysicsActive = false;
            _physicsSimulator.Reset();
            NotifyStateDirty();
        }
    }

    /// <summary>
    /// P2-003 S2: Build a WorkArea (physical pixels) for the monitor the
    /// pet's foot-center is currently on. The work area excludes the
    /// taskbar. Window dimensions are accounted for so the foot anchor
    /// (bottom-center) stays within bounds: minFootY = wa.Top + winHeight,
    /// maxFootY = wa.Bottom.
    /// </summary>
    private WorkArea BuildWorkArea()
    {
        var monitors = TestMonitorList ?? MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            // P2-003 S3: No available display — return an explicitly invalid
            // work area (Left > Right) so PhysicsSimulator.Update rejects it
            // and UpdatePhysics safely stops physics. Do NOT return (0,0,0,0)
            // which passes IsValid (0<=0) and would let physics run in a
            // zero-area box, potentially moving the window to the origin.
            return new WorkArea(1, 0, 0, 0);

        var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
        var foot = CoordinateTransform.WindowTopLeftToFootCenter(
            Left, Top, Width, Height, dpiX, dpiY);

        var monitor = MonitorLocator.FindMonitor(monitors, foot.FootCenterX, foot.FootCenterY)
                      ?? MonitorLocator.FindNearestMonitor(monitors, foot.FootCenterX, foot.FootCenterY);

        var wa = monitor.WorkArea.Bounds;
        var winW = Width * monitor.DpiScaleX;
        var winH = Height * monitor.DpiScaleY;

        // Foot-center bounds: the window must be fully visible.
        // Left edge: footX >= wa.Left + winW/2
        // Right edge: footX <= wa.Right - winW/2
        // Top edge: footY >= wa.Top + winH (foot is bottom-center)
        // Bottom edge: footY <= wa.Bottom
        return new WorkArea(
            wa.Left + winW / 2.0,
            wa.Top + winH,
            wa.Right - winW / 2.0,
            wa.Bottom);
    }

    /// <summary>
    /// P1-003: End petting candidate state. Per D-013, petting is a non-scoring
    /// gesture — no affinity increment, no cooldown. Only cleans up capture and
    /// resets hover.
    /// </summary>
    private void EndPetting()
    {
        _isPettingCandidate = false;
        if (PetCanvas.IsMouseCaptured)
            PetCanvas.ReleaseMouseCapture();

        _hoverDetector.Reset();
    }

    /// <summary>
    /// P1-003: Convert petting candidate to drag when movement exceeds threshold.
    /// Retains the original mouse capture and down-position anchor.
    /// </summary>
    private void ConvertPettingToDrag()
    {
        _isPettingCandidate = false;
        StartDragging();
    }

    /// <summary>
    /// P1-003: Check if a local point (relative to PetImage) is in the head zone.
    /// Head zone = upper 1/3 of the image area, bounded by [0, width] × [0, height/3].
    /// Points outside the image rectangle (negative coords, X beyond width, or
    /// Y beyond the head boundary) are NOT in the head zone.
    /// F-03 R5: Additionally requires the point to be on a non-transparent pixel
    /// of the current frame. A point inside the head rectangle but on a transparent
    /// area (e.g. above the character's head in the upper 1/3) does NOT count as
    /// head zone — this prevents accumulating petting time while the cursor is on
    /// empty space within the head rectangle.
    /// Returns false if the image source is unavailable.
    /// Region source: upper 1/3 of frameHeight per ARCHITECTURE §8; per-character
    /// interaction region fields are deferred to a later stage.
    /// </summary>
    internal bool IsInHeadZone(Point localPoint)
    {
        double imgHeight = PetImage.ActualHeight > 0 ? PetImage.ActualHeight : PetImage.Height;
        double imgWidth = PetImage.ActualWidth > 0 ? PetImage.ActualWidth : PetImage.Width;
        if (imgHeight <= 0 || imgWidth <= 0) return false;

        // Rectangular boundary check.
        if (!(localPoint.X >= 0
            && localPoint.X <= imgWidth
            && localPoint.Y >= 0
            && localPoint.Y <= imgHeight * HeadZoneRatio))
            return false;

        // F-03 R5: Also require the point to be on a visible (non-transparent)
        // pixel of the current frame. This prevents petting accumulation when
        // the cursor is on empty space within the head rectangle.
        return PetImageGeometry.Contains(PetImage, localPoint);
    }

    /// <summary>
    /// P1-003: Check if the pet window is currently in a screen corner zone.
    /// Corner zone = 15% of work area width/height at each of the four corners.
    /// Uses the work area of the monitor the pet is currently on.
    /// </summary>
    internal bool IsInCornerZone()
    {
        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0) return false;

        // Find the monitor the pet is on (by center point in physical pixels).
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var centerDip = new Point(Left + Width / 2, Top + Height / 2);
        var centerPx = transform.Transform(centerDip);
        var cx = (int)centerPx.X;
        var cy = (int)centerPx.Y;

        var monitor = monitors.FirstOrDefault(m => m.FullBounds.Contains(cx, cy));
        if (monitor.DeviceName is null)
            monitor = monitors.FirstOrDefault(m => m.WorkArea.Bounds.Contains(cx, cy));
        if (monitor.DeviceName is null)
            return false;

        var wa = monitor.WorkArea.Bounds;
        var cornerW = wa.Width * 0.15;
        var cornerH = wa.Height * 0.15;

        // Four corner rectangles.
        var topLeft = cx >= wa.Left && cx < wa.Left + cornerW
                   && cy >= wa.Top && cy < wa.Top + cornerH;
        var topRight = cx >= wa.Right - cornerW && cx < wa.Right
                     && cy >= wa.Top && cy < wa.Top + cornerH;
        var bottomLeft = cx >= wa.Left && cx < wa.Left + cornerW
                       && cy >= wa.Bottom - cornerH && cy < wa.Bottom;
        var bottomRight = cx >= wa.Right - cornerW && cx < wa.Right
                        && cy >= wa.Bottom - cornerH && cy < wa.Bottom;

        return topLeft || topRight || bottomLeft || bottomRight;
    }

    /// <summary>
    /// F-04: Check if the mouse is in the pet's interaction range.
    /// "First encounter" means the mouse has come near or over the pet entity.
    /// Interaction range = within 50 physical pixels of the pet's opaque bounds.
    /// In test mode without a PresentationSource, falls back to checking if
    /// the mouse is within the window's screen rectangle + margin.
    /// </summary>
    internal bool IsMouseInInteractionRange(int screenX, int screenY)
    {
        const int interactionMarginPx = 50;

        // Get pet rect in physical pixels.
        var petRect = GetPetRectPhysicalPx();
        if (petRect.Width <= 0 || petRect.Height <= 0)
        {
            // Fallback: use window bounds in physical pixels.
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var topLeftPx = transform.Transform(new Point(Left, Top));
            var bottomRightPx = transform.Transform(new Point(Left + Width, Top + Height));
            return screenX >= topLeftPx.X - interactionMarginPx
                && screenX <= bottomRightPx.X + interactionMarginPx
                && screenY >= topLeftPx.Y - interactionMarginPx
                && screenY <= bottomRightPx.Y + interactionMarginPx;
        }

        return screenX >= petRect.Left - interactionMarginPx
            && screenX <= petRect.Left + petRect.Width + interactionMarginPx
            && screenY >= petRect.Top - interactionMarginPx
            && screenY <= petRect.Top + petRect.Height + interactionMarginPx;
    }

    /// <summary>
    /// F-03: Handle capture loss (e.g. menu or another window stole capture).
    /// Cancel drag state so the pet doesn't follow the mouse without a grab.
    /// </summary>
    private void PetCanvas_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // P2-003 S2: Capture loss is a cancellation, not a normal release.
        // Do NOT Launch physics — the pet stays where it is.
        if (_isDragging)
            EndDrag(isCancellation: true);
        if (_isPettingCandidate)
            EndPetting();
    }

    /// <summary>
    /// F-02: Clamp the current window position to a visible work area.
    /// Delegates to WindowPositionAdapter which encapsulates the full
    /// DIP↔foot coordinate adaptation pipeline with target-monitor DPI
    /// for the reverse path (cross-monitor DPI consistency).
    /// </summary>
    private void ClampWindowToWorkArea()
    {
        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return;

        var (winDpiX, winDpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);

        var (leftDip, topDip) = WindowPositionAdapter.ClampToWorkArea(
            monitors, Left, Top, Width, Height, winDpiX, winDpiY);

        Left = leftDip;
        Top = topDip;
    }

    /// <summary>
    /// F-02: Handle monitor layout changes (connect/disconnect/resolution/work area).
    /// Re-enumerates monitors and recovers the pet to a visible work area.
    /// Delegates to WindowPositionAdapter with null saved device name so
    /// off-screen positions map to the primary monitor.
    /// </summary>
    private void OnDisplayLayoutChanged()
    {
        // P2-003: Clear drag sample context so old DPI coordinates are never
        // subtracted from new-DPI coordinates after a monitor change.
        _speedSampler.ClearSampleContext();

        // P2-003 S3: Stop any active physics — the work area has changed,
        // the trajectory's boundary assumptions are stale. ForceStop keeps
        // the current position; ClampWindowToWorkArea (below) will correct
        // it to the new valid area.
        if (_isPhysicsActive)
        {
            _physicsSimulator.ForceStop();
            _isPhysicsActive = false;
        }

        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return;

        var (winDpiX, winDpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);

        var (leftDip, topDip) = WindowPositionAdapter.RecoverPosition(
            monitors, Left, Top, Width, Height, winDpiX, winDpiY, null);

        Left = leftDip;
        Top = topDip;
        NotifyStateDirty();
    }

    /// <summary>
    /// P0-003: Recover the pet to a visible work area on the current primary monitor.
    /// Delegates to WindowPositionAdapter.RecoverToPrimary.
    /// P0-004: Made public for tray menu access.
    /// </summary>
    public void RecoverPet()
    {
        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return;

        var (leftDip, topDip) = WindowPositionAdapter.RecoverToPrimary(
            monitors, Width, Height);

        Left = leftDip;
        Top = topDip;
        NotifyStateDirty();
    }

    // G-03: Pause duration tracking. When paused, wall-clock time continues
    // to advance (GetElapsedMs is monotonic). On resume, we need to compensate
    // so that the pause duration is NOT counted toward emotion decay or
    // expression/corner-stay timers.
    private double _pauseStartMs;
    private bool _hasPauseStartMs;

    /// <summary>
    /// P0-004: Pause active behavior (animation timer) when user pauses via tray.
    /// P1-001 F-02: Also pause the animation player's time so resuming
    /// continues from the same frame, not from wall-clock elapsed.
    /// G-03: Record the pause start time so ResumeAnimation can compensate
    /// emotion decay and expression timers for the pause duration.
    /// </summary>
    public void PauseAnimation()
    {
        _isAppPaused = true;
        _updateTimer.Stop();
        _animPlayer.Pause();

        // G-03: Record pause start for compensation on resume.
        _pauseStartMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        _hasPauseStartMs = true;
        _idleBlink?.Pause(_pauseStartMs);
        _dragRelease?.Pause(_pauseStartMs);
        _curiosity.Pause(_pauseStartMs);

        // Discard hover dwell at the pause boundary.
        _hoverDetector.Reset();

        // P2-003: Clear drag sampling state at pause boundary so stale
        // samples don't produce a throw on resume.
        _speedSampler.ClearSampleContext();
        _speedSampler.Reset();

        // P2-003 S3 R5 F-05: Freeze physics on pause — NOT terminate.
        // The timer is already stopped above, so OnUpdateTick will not fire
        // and physics will not advance. We must NOT call ForceStop (which
        // zeroes velocity and sets IsResting=true) or clear _isPhysicsActive,
        // because ResumeAnimation must continue the original trajectory.
        // Only clear the time carry so stale accumulated time doesn't cause
        // a large jump on the first post-resume tick.
        _physicsSimulator.ClearTimeAccumulation();

        // P1-004: End the active period so paused time is not counted as
        // effective runtime for rare-event scheduling.
        // PREP-002 F-01: Independent resolution Test* → StateContext → App.
        EffectiveRuntimeTracker? rtPause = Resolve(
            TestRuntimeTracker, StateContext?.RuntimeTracker, a => a.RuntimeTracker);
        rtPause?.EndActivePeriod(_pauseStartMs / 60000.0);
    }

    /// <summary>
    /// P0-004: Resume active behavior after pause.
    /// P1-001 F-02: Resume the animation player's time so the animation
    /// continues from where it was paused, without accumulated wall-clock gap.
    /// G-03: Compensate emotion decay and expression timers for the pause
    /// duration so that paused time is NOT counted toward decay or expression.
    /// </summary>
    public void ResumeAnimation()
    {
        var expressionResumeMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        _idleBlink?.Resume(expressionResumeMs);
        _dragRelease?.Resume(expressionResumeMs);
        _curiosity.Resume(expressionResumeMs);
        // G-03: Calculate pause duration and compensate timers.
        if (_hasPauseStartMs)
        {
            var resumeMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
            var pauseDurationMs = resumeMs - _pauseStartMs;

            if (pauseDurationMs > 0)
            {
                // Compensate grievance expression start time so the expression
                // doesn't instantly complete after a long pause.
                if (_isExpressingGrievance)
                {
                    _grievanceExpressionStartMs += pauseDurationMs;
                }

                // P1-004: Compensate probe start time so the probe doesn't
                // instantly complete after a long pause.
                if (_isProbing)
                {
                    _probeStartMs += pauseDurationMs;
                }

                // Compensate emotion decay: advance the accumulator's reference
                // time forward by the pause duration so that the next AdvanceTo
                // call doesn't count the paused interval as decay time.
                // PREP-002 F-01: Independent resolution Test* → StateContext → App.
                var emotionAcc = Resolve(
                    TestEmotionAccumulator, StateContext?.EmotionAccumulator, a => a.EmotionAccumulator);
                if (emotionAcc is not null)
                {
                    // Move the decay reference forward by the pause duration.
                    // This is NOT advancing decay — it's skipping the paused interval.
                    emotionAcc.SkipInterval(TimeSpan.FromMilliseconds(pauseDurationMs));
                }

                // Compensate corner tracker's time-based windows.
                // PREP-002 F-01: Independent resolution Test* → StateContext → App.
                var cornerTrack = Resolve(
                    TestCornerTracking, StateContext?.CornerTracking, a => a.CornerTracking);
                cornerTrack?.SkipInterval(pauseDurationMs);
            }

            _hasPauseStartMs = false;
        }

        _isAppPaused = false;
        _animPlayer.Resume();
        _updateTimer.Start();

        // P2-003 S3 R5 F-05: Resume continues the original trajectory.
        // _isPhysicsActive was NOT cleared at pause (freeze, not terminate),
        // so if physics was active before pause it remains active now.
        // Reset the physics time reference to now and clear the carry so the
        // first post-resume tick uses a normal delta (no 5-second backlog).
        var resumePhysicsMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        _lastPhysicsUpdateMs = resumePhysicsMs;
        _physicsSimulator.ClearTimeAccumulation();

        // P1-004: Start a fresh active period on resume.
        // PREP-002 F-01: Independent resolution Test* → StateContext → App.
        var resumeMonotonicMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        EffectiveRuntimeTracker? rtResume = Resolve(
            TestRuntimeTracker, StateContext?.RuntimeTracker, a => a.RuntimeTracker);
        rtResume?.StartActivePeriod(resumeMonotonicMs / 60000.0);

        // Resume starts a fresh hover dwell.
        _hoverDetector.Reset();
    }

    /// <summary>
    /// P1-001: Public accessor for the current action (for testing/diagnostics).
    /// </summary>
    public string CurrentAction => _animPlayer.CurrentActionId;

    /// <summary>
    /// P1-004: Public accessor for the probe (peek) active state (for testing).
    /// </summary>
    public bool IsProbing => _isProbing;

    /// <summary>
    /// P1-004: Restore the original position if a probe is active, so that
    /// the saved state reflects the pre-probe position. Called by
    /// App.FlushFinalState before the final save. Does NOT end the probe
    /// animation — just restores the window position for save capture.
    /// P2-003 S3: Also stop active physics so the saved position is a
    /// legal rest position, not a mid-trajectory coordinate. ForceStop
    /// zeroes velocity at the current position, then ClampWindowToWorkArea
    /// ensures the position is within a valid work area.
    /// </summary>
    public void RestoreProbePositionForExit()
    {
        // P2-003 S3: Stop physics first so the saved position is stable.
        if (_isPhysicsActive)
        {
            _physicsSimulator.ForceStop();
            _isPhysicsActive = false;
            // Clamp to a valid work area so the exit position is legal.
            ClampWindowToWorkArea();
        }

        if (_isProbing)
        {
            Left = _probeOriginalLeft;
            Top = _probeOriginalTop;
        }
    }

    /// <summary>
    /// P1-001 F-02: Public accessor for the app-level pause state (for testing/diagnostics).
    /// True when the user has paused via tray; action switches during pause
    /// must not implicitly clear this state.
    /// </summary>
    public bool IsAppPaused => _isAppPaused;

    /// <summary>
    /// P1-001 F-02: Public accessor for the player-level pause state.
    /// Must stay consistent with <see cref="IsAppPaused"/>: when the app is
    /// paused, the player should also be paused, even after action switches.
    /// </summary>
    public bool IsPlayerPaused => _animPlayer.IsPaused;

    /// <summary>
    /// P1-001 F-04: Public accessor for the current frame offset applied to rendering.
    /// Returns the TranslateTransform values used by UpdateFrame.
    /// </summary>
    public (double x, double y) CurrentFrameOffset => (FrameOffsetTransform.X, FrameOffsetTransform.Y);

    /// <summary>
    /// P1-001 F-03: Public accessor for the number of successfully cached frame images.
    /// When zero, the built-in fallback bitmap is used.
    /// </summary>
    public int CachedImageCount => _imageCache.Count;

    /// <summary>
    /// P1-001 F-03: Public accessor for whether the Image control has a visible source.
    /// True when a cached PNG or the built-in fallback bitmap is displayed.
    /// </summary>
    public bool HasVisibleImage => PetImage.Source is not null;

    /// <summary>
    /// P1-002: Public accessor for the hover detector (for testing/diagnostics).
    /// </summary>
    public HoverDetector HoverDetector => _hoverDetector;

    /// <summary>
    /// P1-002: Public accessor for the drag state (for testing/diagnostics).
    /// True while the pet is being dragged; hover detection is skipped.
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// P2-003: Public accessor for the last release evaluation result.
    /// Valid after EndDrag; defaults to gentle release before first drag.
    /// </summary>
    public ReleaseResult LastReleaseResult => _lastReleaseResult;

    /// <summary>
    /// P2-004: Public accessor for the last release diagnostics snapshot.
    /// Null until the first drag completes or if diagnostics capture is disabled.
    /// Contains sample buffer, segment indices, decision reason, and config
    /// thresholds at the moment of EvaluateRelease — for offline analysis of
    /// short-drag non-trigger cases.
    /// </summary>
    public ReleaseDiagInfo? LastReleaseDiag => _lastReleaseResult.Diag;

    /// <summary>
    /// P2-004: Public accessor for the recent RecordSample attempt log.
    /// Each entry records the input (x, y, timeMs, source), the buffer state
    /// before the call, and whether the sample was accepted or rejected (with
    /// reason). For offline analysis of short-drag non-trigger cases at the
    /// input level — e.g. whether the first Move was rejected due to
    /// same-timestamp as the baseline.
    ///
    /// Lifecycle:
    /// - While dragging: returns the live AttemptLog (real-time observation).
    /// - After release (EndDrag → Reset): returns a frozen copy taken before
    ///   Reset, so the host can still read the complete attempt log of the
    ///   last drag. This snapshot corresponds to LastReleaseResult/LastReleaseDiag.
    /// - On the next drag start (StartDragging → Reset): the snapshot is
    ///   replaced by the new drag's live AttemptLog.
    /// </summary>
    public IReadOnlyList<SampleAttempt> LastSampleAttempts =>
        _isDragging ? _speedSampler.AttemptLog : _lastSampleAttemptsSnapshot;

    /// <summary>
    /// P2-003: Public accessor for the current sample count in the sampler.
    /// Useful for testing that samples are recorded during drag.
    /// </summary>
    public int DragSampleCount => _speedSampler.SampleCount;

    /// <summary>
    /// P2-003 S2: True while the physics simulator is actively driving
    /// window position (after a throw release, until the pet comes to rest).
    /// </summary>
    public bool IsPhysicsActive => _isPhysicsActive;

    /// <summary>
    /// P2-003 S2: True if the physics simulator has been launched and not
    /// yet Reset. Distinct from IsPhysicsActive: IsLaunched remains true
    /// even after resting, until Reset is called.
    /// </summary>
    public bool IsPhysicsLaunched => _physicsSimulator.IsLaunched;

    /// <summary>
    /// P1-003: Public accessor for the petting candidate state (for testing/diagnostics).
    /// True while the user is pressing the head zone and may be petting.
    /// </summary>
    public bool IsPettingCandidate => _isPettingCandidate;

    /// <summary>
    /// P1-003: Public accessor for the grievance expression state (for testing/diagnostics).
    /// True while the pet is expressing grievance (10-second duration, R-02).
    /// </summary>
    public bool IsExpressingGrievance => _isExpressingGrievance;

    /// <summary>
    /// P1-002: Public accessor for the context menu open state (for testing/diagnostics).
    /// True while the native context menu is open; hover detection is skipped.
    /// </summary>
    public bool IsContextMenuOpen => _isContextMenuOpen;

    protected override void OnClosed(EventArgs e)
    {
        // F-03: Clean up drag state on window close — cancellation, not a release.
        if (_isDragging)
            EndDrag(isCancellation: true);
        // P1-003: Clean up petting state on window close.
        if (_isPettingCandidate)
            EndPetting();
        // P2-003 S3: Stop active physics on close. FlushFinalState (called
        // by App.ShutdownMainWindow before OnClosed) already calls
        // RestoreProbePositionForExit which stops physics and clamps, but
        // OnClosed may also be reached via alt-F4 or other paths — ensure
        // physics is always stopped.
        if (_isPhysicsActive)
        {
            _physicsSimulator.ForceStop();
            _isPhysicsActive = false;
        }
        _updateTimer.Stop();
        base.OnClosed(e);
    }
}
