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
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Social;
using DesktopPet.Core.Windows;

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

    // P5A-001: Window perch controller (lazy; only created when perching).
    private PetWindowPerch? _petPerch;

    // P5B-001: Window climb controller (lazy; only created when climbing).
    private PetWindowClimb? _petClimb;

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

    // P3-002 F-01 R3: Test-only read accessor for the social curiosity
    // ownership flag. Lets tests verify that Reject/ReturnToIdle only
    // releases social-owned curiosity, not independent hover curiosity.
    internal bool TestSocialOwnsCuriosity => _socialOwnsCuriosity;

    // G-03 R4: Test injection point for controlled UTC time source.
    // When non-null, OnUpdateTick uses this instead of DateTime.UtcNow
    // so tests can use a single consistent time base for both monotonic
    // clock (TestGetElapsedMs) and UTC (TestGetUtcNow), avoiding the
    // divergence between Thread.Sleep-driven real UTC and injected clockMs.
    internal Func<DateTime>? TestGetUtcNow;

    // P4-003: Test injection point for controlled LOCAL time source.
    // When non-null, the night sleepy tick uses this instead of
    // DateTime.Now so tests and isolation probes can cross the
    // 23:30 / midnight / 06:00 boundaries without touching the system
    // clock. Production leaves this null.
    internal Func<DateTime>? TestGetLocalNow;

    // 套件级默认本地时间（测试基础设施）：App 测试程序集初始化时固定为
    // 白天正午，使整个套件不受真实墙钟时间影响（夜间窗口 23:30–06:00 会
    // 把 idle 基线切到 sleep，旧测试在夜间全挂）。实例级 TestGetLocalNow
    // 优先级更高；生产保持 null（用 DateTime.Now）。
    internal static Func<DateTime>? TestSuiteLocalNowDefault;

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

    // P3-002: Test injection point for social coordinator.
    internal SocialCoordinator? TestSocialCoordinator;

    // P4-002 R2 F-03: Test accessor for the loaded character definition,
    // so tests can remove individual actions and verify the stretch+rest
    // resource requirement in CanStartRestReminder.
    internal PetCharacterDef TestCharacter => _character;

    // P3-004 R4 F-01: Test-only flag to simulate a window becoming
    // non-writable between coordinator.Tick (which returned chase deltas)
    // and the App-layer commit guard. When true, _isPhysicsActive is
    // temporarily set before the leaderCanWrite/followerCanWrite checks
    // so the commit refusal path is exercised. No production code sets this.
    internal bool TestBlockChaseCommit;

    // P3-002: Tracks the last non-idle activity timestamp for social idle detection.
    // Updated when drag ends, physics stops, probe ends, or grievance ends.
    private double _lastSocialActivityMs;

    // P3-002: True when a social session is currently active for this pet.
    // Set by the social tick evaluation in OnUpdateTick. When true, blocks
    // rare-event Evaluate (via isSocialActive parameter).
    private bool _isSocialActive;

    // P3-002 F-01 R3: Ownership flag — true only when the current curiosity
    // expression was started by a social session (Notice/Respond), not by
    // independent user hover. This lets Reject/ReturnToIdle/CancelSocialImmediately
    // release only social-owned curiosity without clearing a still-valid hover
    // curiosity. Cannot use _curiosity.IsActive or CurrentAction to determine
    // ownership because both are identical regardless of source.
    private bool _socialOwnsCuriosity;

    // P3-002: Tracks busy→idle transition to measure continuous idle time.
    private bool _wasSocialBusy = true;

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

    // P4-002: Rest-reminder performance state.
    // When active, the pet plays a stretch → rest sequence (~30 seconds).
    // Press (body) skips immediately; drag/petting/social/rare-event/pause/
    // lock/sleep/exit cancels without replay.
    private readonly RestReminderPerformance _restReminder = new();
    private string? _restReminderCurrentAction;

    /// <summary>
    /// P4-002: True when the rest-reminder performance is active (Stretching
    /// or Resting phase). Exposed for test verification.
    /// </summary>
    internal bool IsRestReminderActive => _restReminder.IsActive;

    /// <summary>
    /// P6-004（第一小步）：这只宠物当前是否“可接受展示”主动统计气泡。
    /// 拖动/抚摸/物理/委屈/探头/休息提醒/菜单/攀爬/社交/暂停任一进行中即
    /// 不可用；隐藏或最小化也不可用。只读判定，不改变任何交互状态。
    /// <para>
    /// P6-004 R1 F-01：社交占用同时读共享社交权威状态
    /// （<see cref="IsInSharedSocialSession"/>），与栖息写入判定同源。窗口 A
    /// 的 tick 驱动会话并移动 B 时，B 的本地 <c>_isSocialActive</c> 要等 B
    /// 自己的社交轮询才置位；只读共享会话可避免在会话进行中选中 B 展示气泡。
    /// 本地旗 <c>_isSocialActive</c> 仍保留（其余社交阶段/冷却判定）。
    /// </para>
    /// </summary>
    internal bool IsAvailableForStatsBubble
        => IsVisible
           && WindowState != System.Windows.WindowState.Minimized
           && !_isDragging
           && !_isPettingCandidate
           && !_isPhysicsActive
           && !_isExpressingGrievance
           && !_isProbing
           && !_restReminder.IsActive
           && !_isContextMenuOpen
           && !_isSocialActive
           && !IsInSharedSocialSession
           && !_isAppPaused
           && !(_petClimb?.IsActive ?? false)
           && !IsClimbAction(_animPlayer.CurrentActionId);

    /// <summary>
    /// P6-004（第一小步）：宠物窗口当前的矩形（DIP），供气泡放置在宠物外侧。
    /// </summary>
    internal Rect GetPetRectDipForStatsBubble() => new(Left, Top, Width, Height);

    /// <summary>
    /// P6-004（第一小步）：宠物所在显示器的工作区（DIP），供气泡夹取位置。
    /// 使用与位置恢复/物理相同的显示器枚举与 DPI 换算。
    /// </summary>
    internal Rect GetWorkAreaDipForStatsBubble()
    {
        var monitors = TestMonitorList ?? MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return SystemParameters.WorkArea;

        var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
        var foot = CoordinateTransform.WindowTopLeftToFootCenter(
            Left, Top, Width, Height, dpiX, dpiY);
        var monitor = MonitorLocator.FindMonitor(monitors, foot.FootCenterX, foot.FootCenterY)
                      ?? MonitorLocator.FindNearestMonitor(monitors, foot.FootCenterX, foot.FootCenterY);
        var wa = monitor.WorkArea.Bounds;
        return new Rect(
            wa.Left / monitor.DpiScaleX,
            wa.Top / monitor.DpiScaleY,
            wa.Width / monitor.DpiScaleX,
            wa.Height / monitor.DpiScaleY);
    }

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

    // P5A-003: async key state for the perch pick mode (production input
    // polling reads the real left button outside the pet window).
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

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

    // P3-001 F-08 R14: Personality-based visual identification badge.
    // The badge is a small program-drawn shape overlaid on the pet window
    // so two pets with identical bear assets can be visually distinguished.
    // Proactive = blue circle (bottom-right), SlowPaced = warm orange triangle.
    // The badge does not affect hit-testing, window size, foot points, or
    // drag/physics behavior — it is purely visual, drawn on BadgeCanvas
    // which has IsHitTestVisible=False.
    private Personality _personality = Personality.Proactive;

    /// <summary>
    /// P3-001 F-08 R14: Gets or sets the personality used for the visual
    /// identification badge. Must be set before the window is shown for
    /// the badge to appear on the first frame. App.CreatePetWindow sets
    /// this from the PetInstanceArchive before Show().
    /// </summary>
    public Personality Personality
    {
        get => _personality;
        set
        {
            _personality = value;
            ApplyPersonalityBadge();
        }
    }

    /// <summary>
    /// P3-001 F-08 R14: Draw the personality badge on BadgeCanvas.
    /// Proactive: a small blue circle in the bottom-right corner.
    /// SlowPaced: a small warm-orange triangle in the bottom-right corner.
    /// Both are semi-transparent (~80% opacity) and ~14 DIP in size,
    /// positioned at (98, 98)–(112, 112) so they don't obscure the bear's
    /// face or body center. The badge is purely decorative.
    /// </summary>
    private void ApplyPersonalityBadge()
    {
        BadgeCanvas.Children.Clear();

        const double badgeSize = 14.0;
        const double badgeLeft = 98.0;
        const double badgeTop = 98.0;

        if (_personality == Personality.SlowPaced)
        {
            // Warm orange triangle.
            var triangle = new System.Windows.Shapes.Polygon
            {
                Points = new PointCollection
                {
                    new(badgeLeft + badgeSize / 2, badgeTop),                         // top center
                    new(badgeLeft, badgeTop + badgeSize),                             // bottom left
                    new(badgeLeft + badgeSize, badgeTop + badgeSize),                 // bottom right
                },
                Fill = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xE8, 0x8B, 0x3A)),
                Stroke = new SolidColorBrush(MediaColor.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 0.75,
            };
            BadgeCanvas.Children.Add(triangle);
        }
        else
        {
            // Blue circle.
            var circle = new System.Windows.Shapes.Ellipse
            {
                Width = badgeSize,
                Height = badgeSize,
                Fill = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0x3A, 0x7C, 0xE8)),
                Stroke = new SolidColorBrush(MediaColor.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 0.75,
            };
            Canvas.SetLeft(circle, badgeLeft);
            Canvas.SetTop(circle, badgeTop);
            BadgeCanvas.Children.Add(circle);
        }
    }

    // PREP-002: Centralised save-dirty notification. Replaces the scattered
    // "Application.Current is App app → app.MarkStateDirty()" pattern.
    // Priority: StateContext.MarkStateDirty (production, explicitly injected
    // by App) → Application.Current fallback (for tests that use the
    // parameterless constructor without StateContext).
    private void NotifyStateDirty()
    {
        // P3-001-S2: Pass the PetId from StateContext so App can route
        // the save-dirty notification to the correct PetInstanceArchive.
        // Null PetId falls back to "save all pets" for backward compatibility.
        var petId = StateContext?.PetId;
        if (StateContext?.MarkStateDirty is not null)
            StateContext.MarkStateDirty.Invoke(petId);
        else if (System.Windows.Application.Current is App app)
            app.MarkStateDirty(petId);
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
                {
                    // P5B-003 S2：climb_land 播完经此路径自动回 idle（不经
                    // EndClimbAction）——若 ScaleX 仍为 -1 会把 idle 帧镜像
                    // 显示，必须随回 idle 一起复位。
                    if (IsClimbAction(actionId))
                        ClimbMirrorTransform.ScaleX = 1;
                    _animPlayer.PlayAction("idle");
                }
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
    // P6-001 R8 F-06: internal so the diagnostic probe can drive the same
    // production menu-lifecycle seam for the real Exit regression (same
    // precedent as MenuIdPerchOnWindow below).
    internal const uint MenuIdExit = 1;
    private const uint MenuIdRecover = 2;
    private const uint MenuIdAddPet = 3;
    private const uint MenuIdRemovePet = 4;
    // P4-002 R2 M-12: Operable Pause/Resume entry in the window
    // right-click menu. The isolated probe has no tray icon, so without
    // this item the preview offers no pause entry at all. Production
    // keeps the tray entry; this is a redundant, same-path affordance
    // (both call App.OnPauseToggled).
    private const uint MenuIdPauseToggle = 5;
    // P5A-003: formal perch entry points in the right-click menu. Internal
    // so the diagnostic probe can drive the same menu-lifecycle seam.
    internal const uint MenuIdPerchOnWindow = 6;
    internal const uint MenuIdLeavePerch = 7;
    // P5B-002: formal climb entry point in the right-click menu (parallel
    // entry to Perch on Window; same pick flow, climb route on click).
    internal const uint MenuIdClimbOnWindow = 8;
    // P6-001: local usage statistics entries — toggle (dynamic label like
    // PauseToggle) and clear. Internal so tests can drive the same
    // menu-lifecycle seam.
    internal const uint MenuIdStatsToggle = 9;
    internal const uint MenuIdStatsClear = 10;
    // P6-004（第一小步）：只读统计查看入口（区分“已记录统计”与“有样本支撑
    // 的时间偏好”）。Internal so the diagnostic probe can drive the same
    // menu-lifecycle seam.
    internal const uint MenuIdStatsView = 11;

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

    // R9 F-05: Shared menu lifecycle steps. Opening the menu cancels an
    // in-progress drag (right-click during left-drag) and resets hover;
    // EndDrag itself only resets the animation when a drag was actually
    // in progress, so a menu opened during a rest-reminder performance
    // leaves the stretch/rest animation untouched.
    private void EnterContextMenuMode()
    {
        // P5A-003: opening the menu cancels an in-progress perch pick.
        EndPerchPick("cancelled");
        _isContextMenuOpen = true;
        EndDrag(isCancellation: true);
        _hoverDetector.Reset();
    }

    private void ExitContextMenuMode()
    {
        _isContextMenuOpen = false;
        _hoverDetector.Reset();
    }

    /// <summary>
    /// R9 F-05: Test seam for the context-menu lifecycle without the
    /// blocking native TrackPopupMenuEx call. Reproduces the production
    /// open → (menu shown) → close → command-dispatch sequence:
    /// EnterContextMenuMode / ExitContextMenuMode / HandleContextMenuCommand.
    /// </summary>
    internal void TestOpenContextMenuLifecycle(int command, Action? onMenuShown = null)
    {
        if (_isContextMenuOpen)
            return;

        EnterContextMenuMode();
        try
        {
            onMenuShown?.Invoke();
        }
        finally
        {
            ExitContextMenuMode();
        }

        HandleContextMenuCommand((uint)command);
    }

    /// <summary>
    /// P5A-003 R1 F-02: Test seam for the REAL native context menu.
    /// Opens the production ShowNativeContextMenu (TrackPopupMenuEx popup,
    /// same menu build + foreground handling + command dispatch) at the
    /// given screen point in physical pixels. Blocks until an item is
    /// selected or the menu is dismissed; the driver injects real mouse
    /// input to make the selection. Only the injection is synthetic —
    /// menu display and command dispatch are the production path.
    /// </summary>
    internal void TestShowNativeContextMenu(int screenX, int screenY)
        => ShowNativeContextMenu(new POINT { X = screenX, Y = screenY });

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
        EnterContextMenuMode();

        try
        {
            if (!AppendMenu(menu, 0, (UIntPtr)MenuIdRecover, "Recover"))
            {
                System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                return;
            }

            // P3-001-S3: Add second pet.
            if (!AppendMenu(menu, 0, (UIntPtr)MenuIdAddPet, "Add Pet"))
            {
                System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                return;
            }
            // F-05 R9: Remove Pet only available for the second (non-original) pet.
            if (System.Windows.Application.Current is App app)
            {
                var currentPetId = StateContext?.PetId;
                var state = app.TestState;
                if (state?.Pets is not null && state.Pets.Count > 0 &&
                    state.Pets[0] is { } firstPet &&
                    firstPet.PetId != currentPetId)
                {
                    if (!AppendMenu(menu, 0, (UIntPtr)MenuIdRemovePet, "Remove Pet"))
                    {
                        System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                        return;
                    }
                }
            }

            // P5A-003: Leave Perch only while a perch binding is active
            // (no lazy creation side effects from the Perch property).
            if (_petPerch?.IsActive == true)
            {
                if (!AppendMenu(menu, 0, (UIntPtr)MenuIdLeavePerch, "Leave Perch"))
                {
                    System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                    return;
                }
            }

            // P5A-003: Perch on Window — enter the pick mode; the next left
            // click on an eligible external window selects it explicitly.
            if (!AppendMenu(menu, 0, (UIntPtr)MenuIdPerchOnWindow, "Perch on Window"))
            {
                System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                return;
            }

            // P5B-002: Climb on Window — same pick flow as Perch on Window;
            // the left click on an eligible external window starts the climb
            // route (parallel entry; Perch on Window behavior unchanged).
            if (!AppendMenu(menu, 0, (UIntPtr)MenuIdClimbOnWindow, "Climb on Window"))
            {
                System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                return;
            }

            // P4-002 R2 M-12: Pause/Resume toggle (label reflects the
            // current global pause state). The handler goes through the
            // same App.OnPauseToggled path as the tray menu entry.
            if (System.Windows.Application.Current is App currentApp)
            {
                var pauseLabel = currentApp.IsGloballyPaused ? "Resume" : "Pause";
                if (!AppendMenu(menu, 0, (UIntPtr)MenuIdPauseToggle, pauseLabel))
                {
                    System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                    return;
                }
            }

            // P6-001: Statistics toggle (label mirrors the persisted toggle;
            // same path as the settings, default OFF) and Clear entry.
            if (System.Windows.Application.Current is App statsApp)
            {
                var statsLabel = statsApp.IsUsageStatsEnabled ? "Statistics: On" : "Statistics: Off";
                if (!AppendMenu(menu, 0, (UIntPtr)MenuIdStatsToggle, statsLabel))
                {
                    System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                    return;
                }
                if (!AppendMenu(menu, 0, (UIntPtr)MenuIdStatsClear, "Clear Statistics"))
                {
                    System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                    return;
                }
                // P6-004（第一小步）：只读查看入口。统计关闭时仍可打开，
                // 窗口会如实显示未启用与已有记录。
                if (!AppendMenu(menu, 0, (UIntPtr)MenuIdStatsView, "Usage Statistics"))
                {
                    System.Diagnostics.Trace.TraceWarning("AppendMenu failed: {0}", Marshal.GetLastPInvokeError());
                    return;
                }
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
            ExitContextMenuMode();
            DestroyMenu(menu);

            // Complete the native popup lifecycle, including repeated invocations.
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            // Restore the prior app only if the menu owner still has foreground.
            // Never steal focus back from a window the user clicked while dismissing.
            if (GetForegroundWindow() == _hwnd && previousForeground != _hwnd &&
                previousForeground != IntPtr.Zero && IsWindow(previousForeground))
                SetForegroundWindow(previousForeground);
        }

        HandleContextMenuCommand(command);
    }

    /// <summary>
    /// R9 F-05: Dispatch a context-menu command through the same paths as
    /// the production right-click menu. Shared by ShowNativeContextMenu and
    /// the TestOpenContextMenuLifecycle seam. Commands are dispatched via
    /// Dispatcher.BeginInvoke (async, like the native menu return path).
    /// </summary>
    private void HandleContextMenuCommand(uint command)
    {
        if (command == MenuIdExit)
            // F-05 R9: Exit is always a global app exit, not a per-pet removal.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current is App app)
                    app.ShutdownMainWindow();
            }));
        else if (command == MenuIdPauseToggle)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current is App app)
                    app.OnPauseToggled(null, !app.IsGloballyPaused);
            }));
        else if (command == MenuIdRecover)
            Dispatcher.BeginInvoke(new Action(RecoverPet));
        else if (command == MenuIdStatsToggle)
            // P6-001: toggle statistics collection through the settings +
            // service path (default-off; the toggle persists).
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current is App app)
                    app.SetUsageStatsEnabled(!app.IsUsageStatsEnabled);
            }));
        else if (command == MenuIdStatsClear)
            // P6-001: clear all collected statistics (toggle and settings
            // are preserved).
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current is App app)
                    app.ClearUsageStats();
            }));
        else if (command == MenuIdStatsView)
            // P6-004（第一小步）：open the read-only statistics viewer
            // (recorded statistics vs. sample-supported time preferences).
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current is App app)
                    app.ShowUsageStatsViewer();
            }));
        else if (command == MenuIdAddPet)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current is App app)
                    app.AddPet();
            }));
        else if (command == MenuIdRemovePet)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current is App app)
                {
                    var petId = StateContext?.PetId;
                    if (!string.IsNullOrEmpty(petId))
                        app.RemovePet(petId);
                }
            }));
        else if (command == MenuIdPerchOnWindow)
            // P5A-003: enter the explicit window-pick mode.
            Dispatcher.BeginInvoke(new Action(StartPerchPick));
        else if (command == MenuIdClimbOnWindow)
            // P5B-002: enter the explicit window-pick mode for climbing.
            Dispatcher.BeginInvoke(new Action(StartClimbPick));
        else if (command == MenuIdLeavePerch)
            // P5A-003: leave the current perch; the pet stays where it is.
            Dispatcher.BeginInvoke(new Action(() => _petPerch?.Detach("user-left")));
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
        // P4-003: Night sleep suppresses hover/curiosity. While the pet is
        // sleeping (CurrentActionId == "sleep"), the idle-talking hover
        // branch must not fire and must reset its detectors; sleep is the
        // new idle baseline at night. Higher-priority states (drag,
        // grievance, probe, rest reminder) still switch the action away
        // from "sleep" first, so this check never blocks them.
        // R7 F-04: hover 是低优先级表达，不得覆盖攀爬表现——攀爬路线
        // 进行中（_petClimb.IsActive）或 climb_* 动作呈现中（含到达后
        // land 播完前）都不触发好奇。
        if (!_isDragging && !_isPettingCandidate && !_isContextMenuOpen && !_isAppPaused
            && !_isExpressingGrievance && !_isProbing
            && !_restReminder.IsActive
            && _animPlayer.CurrentActionId != "sleep"
            && !(_petClimb?.IsActive ?? false)
            && !IsClimbAction(_animPlayer.CurrentActionId)
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

            // F-04 R1: Read the AUTHORITATIVE shared social state from the
            // global coordinator BEFORE rare-event Evaluate, not the local
            // _isSocialActive flag which is only updated by EvaluateSocialTick
            // (which runs AFTER this block). When window A starts a social
            // session, window B's _isSocialActive is still false until B's
            // own EvaluateSocialTick runs — but B's rare-event block fires
            // first and could consume the plan. Reading coordinator.IsSocialActive
            // ensures all windows see the shared session state immediately.
            var sharedSocialCoordinator = Resolve(
                TestSocialCoordinator, StateContext?.SocialCoordinator, a => a.SocialCoordinator);
            var authoritativeSocialActive = sharedSocialCoordinator?.IsSocialActive ?? false;

            var rareResult = RareEventUpdateService.EvaluateTick(
                runtimeTracker,
                rareScheduler,
                nowMs,
                today,
                isDragging: _isDragging,
                isExpressingGrievance: _isExpressingGrievance,
                isPhysicsActive: _isPhysicsActive,
                isSocialActive: authoritativeSocialActive,
                isRestReminderActive: _restReminder.IsActive,
                isSleeping: _animPlayer.CurrentActionId == "sleep");

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
                if (!_isProbing && !_isDragging && !_isExpressingGrievance && !_isPhysicsActive
                    && _animPlayer.CurrentActionId != "sleep")
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

        // P3-002: Social coordinator — mutual-notice between pets.
        // Runs after rare events (social blocks rare-event Evaluate) and before
        // UpdateFrame. The coordinator is global (shared across windows); each
        // window's tick feeds both pets' states. Deduplication in the coordinator
        // ensures only one evaluation per nowMs.
        EvaluateSocialTick(nowMs);

        // P4-002: Drive the rest-reminder performance.
        // When active, tick the state machine and switch actions as needed.
        // The performance is cancelled by drag/petting/social/rare-event
        // via their entry points (Skip/Cancel). Pause/lock/sleep cancel via
        // CancelRestReminder from App.OnPauseToggled and system event handlers.
        if (_restReminder.IsActive)
        {
            var reminderAction = _restReminder.Tick(nowMs);
            if (reminderAction is null)
            {
                // Performance completed (naturally or skipped/cancelled).
                // Return to idle if we were showing a rest-reminder action.
                if (_restReminderCurrentAction is not null &&
                    _animPlayer.CurrentActionId == _restReminderCurrentAction &&
                    !_isDragging && !_isProbing && !_isExpressingGrievance)
                {
                    _animPlayer.PlayAction("idle");
                }
                _restReminderCurrentAction = null;
                _restReminder.Reset();
            }
            else if (reminderAction != _restReminderCurrentAction)
            {
                // Phase transition (stretch → rest).
                _restReminderCurrentAction = reminderAction;
                _animPlayer.PlayAction(reminderAction);
            }
        }

        // P4-003: Night sleepy tick — the single re-evaluation entry point.
        // Local time 23:30 (inclusive) → 06:00 (exclusive) is the night
        // window (NightSchedule). "sleep" is a loop action that replaces
        // idle as the free baseline at night. Priority: drag/petting >
        // pause (timer stopped, no tick) > grievance > probe > rest
        // reminder > social (may interrupt; ends back at idle) > sleep >
        // idle. All interrupt paths end at PlayAction("idle"), so the
        // next tick naturally re-enters sleep after an interaction ends;
        // after resume/pause/lock the first tick re-judges the boundary
        // by the then-current local time.
        // R12 F-07：攀爬路线活跃期间（IsClimbOwned）sleep 接线绝不重入——
        // 起攀后当前动作是 climb_* 天然不满足 idle 条件，此处的显式排除
        // 保证"被更高优先级表达覆盖、释放回 idle 而路线仍活跃"的瞬时窗口
        // 也不会被夜间重判覆盖，下一 tick 由攀爬控制器补播阶段动作。
        if (!_isAppPaused)
        {
            var localNow = TestGetLocalNow?.Invoke() ?? TestSuiteLocalNowDefault?.Invoke() ?? DateTime.Now;
            bool isNight = NightSchedule.IsNight(TimeOnly.FromDateTime(localNow));
            bool isSleeping = _animPlayer.CurrentActionId == "sleep";

            if (isNight && !isSleeping
                && _animPlayer.CurrentActionId == "idle"
                && !IsClimbOwned
                && !_isDragging && !_isPettingCandidate && !_isPhysicsActive
                && !_isExpressingGrievance && !_isProbing
                && !_restReminder.IsActive && !_isContextMenuOpen)
            {
                _animPlayer.PlayAction("sleep");
            }
            else if (!isNight && isSleeping
                && !_isDragging && !_isPettingCandidate)
            {
                _animPlayer.PlayAction("idle");
            }
        }

        UpdatePerchWobble(nowMs);
        UpdatePerchPick(nowMs);
        _petClimb?.Tick(nowMs);
        UpdateFrame();
    }

    /// <summary>
    /// P3-002: Evaluate the social coordinator tick for mutual-notice.
    /// Collects this pet's and the other pet's social state, calls the
    /// global SocialCoordinator, and applies expression results.
    /// Does NOT write position — social notice only switches expression.
    /// </summary>
    private void EvaluateSocialTick(double nowMs)
    {
        var coordinator = Resolve(
            TestSocialCoordinator, StateContext?.SocialCoordinator, a => a.SocialCoordinator);

        if (coordinator is null)
        {
            _isSocialActive = false;
            return;
        }

        // Build this pet's social state.
        var myPetId = StateContext?.PetId ?? "bear-1";
        var myState = BuildPetSocialState(myPetId, nowMs);

        // Find the other pet's window.
        MainWindow? otherWindow = null;
        PetSocialState? otherState = null;
        if (System.Windows.Application.Current is App app)
        {
            foreach (var w in app.AllPetWindows)
            {
                var otherPetId = w.StateContext?.PetId;
                if (otherPetId is null || otherPetId == myPetId)
                    continue;
                otherWindow = w;
                otherState = w.BuildPetSocialState(otherPetId, nowMs);
                break;
            }
        }

        var result = coordinator.Tick(nowMs, myState, otherState);
        _isSocialActive = result.IsSocialActive;

        // F-01 R1: Apply expression results based on this window's role in
        // the session (initiator vs target). The coordinator returns both
        // InitiatorExpression and TargetExpression — we must dispatch the
        // correct one to each window, not just unconditionally Start/Cancel
        // curiosity on whichever window happens to call Tick first.
        //
        // Map expressions to visible behavior using the existing curiosity
        // system (no new art required). Notice/Respond → curious look;
        // Reject/ReturnToIdle → cancel curious and return to idle.
        //
        // We must NOT cancel curiosity that was independently started by the
        // user's hover interaction. Only cancel when the social session is
        // the one that started it (SessionStarted or active session with this
        // pet as participant).
        var myExpression = ResolveExpressionForPet(myPetId, result);

        if (myExpression is not null)
        {
            ApplySocialExpression(myExpression.Value, nowMs);
        }

        // F-01 R1: Also apply the expression to the OTHER window so both pets
        // show their respective roles (initiator notices, target responds/rejects).
        // This ensures the target pet's window gets its expression even if its
        // own Tick call is deduplicated (same nowMs).
        if (otherWindow is not null)
        {
            var otherExpression = ResolveExpressionForPet(otherWindow.StateContext?.PetId ?? "", result);
            if (otherExpression is not null)
            {
                otherWindow.ApplySocialExpression(otherExpression.Value, nowMs);
            }
        }

        // P3-003: Apply seeking step — move the seeker's window Left along
        // the bottom edge. Only the seeker window writes position; the target
        // stays put. Position write is guarded: no drag, no physics, no
        // petting, no pause, no probe (coordinator already checks these but
        // we double-guard at the write site per ARCHITECTURE single-writer rule).
        if (result.SeekStepDelta is not null && result.SeekerId is not null)
        {
            var seekerWindow = result.SeekerId == myPetId
                ? this
                : (otherWindow?.StateContext?.PetId == result.SeekerId ? otherWindow : null);

            // P5A-001 R3 F-02：seek 步进经共享所有权判定写入——与栖息
            // 共用 HasCorePositionConflict 互斥核心；栖息绑定持有时让出。
            if (seekerWindow is not null && seekerWindow.IsSocialPositionWriteAllowed)
            {
                var delta = result.SeekStepDelta.Value;
                if (delta != 0)
                {
                    seekerWindow.Left += delta;
                    seekerWindow.NotifyStateDirty();
                }
            }
        }

        // P3-004: Apply chase step — move BOTH the leader's and follower's
        // windows along the bottom edge. Both windows write position
        // simultaneously. P3-004 R2 F-01: Before applying either delta,
        // verify BOTH windows can accept the write (not dragging, not in
        // physics, not petting, not paused, not probing). If either window
        // cannot accept, BOTH skip — no single-sided writes. The coordinator
        // will cancel on the next tick via CheckInterrupt.
        if (result.ChaseLeaderDelta is not null && result.ChaseLeaderId is not null &&
            result.ChaseFollowerDelta is not null && result.ChaseFollowerId is not null)
        {
            var leaderWindow = result.ChaseLeaderId == myPetId
                ? this
                : (otherWindow?.StateContext?.PetId == result.ChaseLeaderId ? otherWindow : null);

            var followerWindow = result.ChaseFollowerId == myPetId
                ? this
                : (otherWindow?.StateContext?.PetId == result.ChaseFollowerId ? otherWindow : null);

            // P3-004 R2 F-01: Check BOTH windows before writing either.
            // If either cannot accept, neither writes — paired commit.
            // P3-004 R4 F-01: TestBlockChaseCommit simulates a window
            // becoming non-writable after Tick returned chase deltas.
            if (TestBlockChaseCommit) _isPhysicsActive = true;
            // P5A-001 R3 F-02：chase paired-commit 经共享所有权判定——
            // leader/follower 任一不可写（互斥核心冲突或栖息持有）则双方跳过。
            var leaderCanWrite = leaderWindow is not null && leaderWindow.IsSocialPositionWriteAllowed;

            var followerCanWrite = followerWindow is not null && followerWindow.IsSocialPositionWriteAllowed;

            if (leaderCanWrite && followerCanWrite)
            {
                var leaderDelta = result.ChaseLeaderDelta.Value;
                if (leaderDelta != 0)
                {
                    leaderWindow!.Left += leaderDelta;
                    leaderWindow.NotifyStateDirty();
                }

                var followerDelta = result.ChaseFollowerDelta.Value;
                if (followerDelta != 0)
                {
                    followerWindow!.Left += followerDelta;
                    followerWindow.NotifyStateDirty();
                }
            }
            else
            {
                // P3-004 R4 F-01: If either window cannot accept the write,
                // both pets keep their current position and the chase session
                // ends immediately. Reuse CancelSocialImmediately so that
                // both windows' social state (_isSocialActive) and social-
                // owned curiosity expression are cleaned up synchronously,
                // while independent hover curiosity is preserved.
                CancelSocialImmediately(CancelReason.Paused, nowMs);
            }
        }

        // Track idle start: when transitioning from busy to idle, record
        // the current time so MinIdleMs can measure continuous idle.
        var isBusy = _isDragging || _isPettingCandidate || _isPhysicsActive ||
                     _isExpressingGrievance || _isProbing || _isAppPaused ||
                     _restReminder.IsActive;
        if (isBusy)
        {
            _wasSocialBusy = true;
        }
        else if (_wasSocialBusy)
        {
            // Just transitioned from busy to idle — record the moment.
            _lastSocialActivityMs = nowMs;
            _wasSocialBusy = false;
        }
    }

    /// <summary>
    /// F-01 R1: Resolve which expression applies to a specific pet based on
    /// its role in the current session (initiator or target).
    /// </summary>
    private SocialExpression? ResolveExpressionForPet(string petId, SocialTickResult result)
    {
        if (!result.IsSocialActive && !result.SessionEnded)
            return null;

        // Session just ended — both pets return to idle.
        if (result.SessionEnded)
            return SocialExpression.ReturnToIdle;

        // Active session — dispatch by role.
        var session = result.Session;
        if (session.InitiatorId == petId)
            return result.InitiatorExpression;
        if (session.TargetId == petId)
            return result.TargetExpression;

        return null;
    }

    /// <summary>
    /// F-01 R1: Apply a social expression to this window's visible behavior.
    /// Uses the existing curiosity system (Start/Cancel) to show a curious
    /// look for Notice/Respond, and returns to idle for Reject/ReturnToIdle.
    /// Does NOT write position. Does NOT cancel user-triggered hover curiosity
    /// unless the social session is ending.
    /// </summary>
    internal void ApplySocialExpression(SocialExpression expression, double nowMs)
    {
        switch (expression)
        {
            case SocialExpression.Notice:
            case SocialExpression.Respond:
                // F-01 R2: Show curious look via the actual animation channel,
                // not just the curiosity timer. Only switch if not busy with
                // higher-priority user interaction, grievance, or probe
                // (probe already plays "curious" and owns position).
                if (!_isProbing && !_isExpressingGrievance && !_isDragging && !_isPettingCandidate)
                {
                    // F-01 R4: If there is already an active curiosity that is
                    // NOT owned by social (i.e. independent hover triggered it),
                    // social borrows the existing display — do NOT re-Start
                    // (which would reset the timer/期限), do NOT set social
                    // ownership, do NOT PlayAction (already "curious"). The
                    // original hover's ShouldEnd/leave rule continues to apply
                    // and will clean up at the original deadline. Only when
                    // social actually starts a NEW curiosity does it take
                    // ownership and become responsible for releasing it.
                    if (_curiosity.IsActive && !_socialOwnsCuriosity)
                    {
                        // Independent hover owns the current curiosity.
                        // Social borrows the visible "curious" display but
                        // leaves the timer and ownership untouched.
                        break;
                    }

                    _curiosity.Start(nowMs);
                    // F-01 R3: Mark that this curiosity is owned by the social
                    // session, not by independent hover. Only social-owned
                    // curiosity is released on Reject/ReturnToIdle.
                    _socialOwnsCuriosity = true;
                    // Avoid restarting the same animation every tick — only
                    // switch when the current action is not already "curious".
                    if (_animPlayer.CurrentActionId != "curious")
                    {
                        _animPlayer.PlayAction("curious");
                        UpdateFrame();
                    }
                }
                break;

            case SocialExpression.Reject:
            case SocialExpression.ReturnToIdle:
                // F-01 R3: End social-triggered curiosity and restore the
                // actual animation to idle — but ONLY when the social session
                // owns the current curiosity. If the curiosity was started
                // independently by user hover (_socialOwnsCuriosity == false),
                // do NOT cancel it; the hover's own ShouldEnd rule still
                // applies and will clean up normally. This prevents Reject
                // from clearing a still-valid independent hover expression.
                if (_socialOwnsCuriosity)
                {
                    _curiosity.Cancel();
                    _socialOwnsCuriosity = false;
                    // Restore idle animation only if we were showing "curious"
                    // (i.e. social was the owner). Don't override drag, probe,
                    // or grievance which have their own action ownership.
                    if (_animPlayer.CurrentActionId == "curious" &&
                        !_isDragging && !_isProbing && !_isExpressingGrievance)
                    {
                        _animPlayer.PlayAction("idle");
                        UpdateFrame();
                    }
                }
                break;
        }
    }

    /// <summary>
    /// F-02 R1: Immediately cancel any active social session from an event
    /// entry point (drag start, head press, pause, recover, remove, physics).
    /// This does NOT depend on the next OnUpdateTick — the timer may be
    /// stopped (pause) or the coordinator may not get another tick for this
    /// window. ForceCancel on the shared coordinator ensures both pets are
    /// released, and both windows' expressions are cleared synchronously.
    /// 
    /// Called from production event entry points: StartDragging,
    /// BeginCapturedPointerInteraction (head press), PauseAnimation,
    /// RecoverPet, and App.OnPauseToggled/OnRecoverRequested/RemovePet.
    /// </summary>
    internal void CancelSocialImmediately(CancelReason reason, double nowMs)
    {
        var coordinator = Resolve(
            TestSocialCoordinator, StateContext?.SocialCoordinator, a => a.SocialCoordinator);
        if (coordinator is null)
            return;

        // Only cancel if there's an active session — ForceCancel returns Idle
        // when no session is active, which is harmless but we avoid unnecessary
        // cooldown starts.
        if (!coordinator.IsSocialActive)
            return;

        coordinator.ForceCancel(reason, nowMs);

        // Clear this window's social expression immediately.
        _isSocialActive = false;
        // F-01 R3: Only cancel curiosity if the social session owns it.
        // If the current curiosity was started by independent user hover
        // (_socialOwnsCuriosity == false), leave it intact — the hover's
        // own ShouldEnd rule will clean up normally.
        if (_socialOwnsCuriosity)
        {
            _curiosity.Cancel();
            _socialOwnsCuriosity = false;
            // F-01 R2: Restore the actual animation to idle so the visible
            // expression matches the cancelled state. Only restore if social
            // was showing "curious" — don't override drag, probe, or grievance.
            if (_animPlayer.CurrentActionId == "curious" &&
                !_isDragging && !_isProbing && !_isExpressingGrievance)
            {
                _animPlayer.PlayAction("idle");
                UpdateFrame();
            }
        }

        // F-02 R1: Also clear the OTHER window's expression so both pets
        // are released synchronously, not waiting for the other's tick.
        if (System.Windows.Application.Current is App app)
        {
            var myPetId = StateContext?.PetId ?? "bear-1";
            foreach (var w in app.AllPetWindows)
            {
                var otherPetId = w.StateContext?.PetId;
                if (otherPetId is null || otherPetId == myPetId)
                    continue;
                w._isSocialActive = false;
                // F-01 R3: Only cancel the other window's curiosity if the
                // social session owns it. Don't clear independent hover curiosity.
                if (w._socialOwnsCuriosity)
                {
                    w._curiosity.Cancel();
                    w._socialOwnsCuriosity = false;
                    // F-01 R2: Restore other window's animation to idle too.
                    if (w._animPlayer.CurrentActionId == "curious" &&
                        !w._isDragging && !w._isProbing && !w._isExpressingGrievance)
                    {
                        w._animPlayer.PlayAction("idle");
                        w.UpdateFrame();
                    }
                }
                break;
            }
        }
    }

    /// <summary>
    /// P3-002: Build the social state snapshot for this window's pet.
    /// Center is in DIP screen coordinates (Left + Width/2, Top + Height/2).
    /// When busy, lastActivityMs = nowMs so idle detection requires the
    /// busy state to clear and MinIdleMs to elapse.
    /// P3-003: Also reports MonitorId, BottomEdgeY, and IsOnBottomEdge
    /// for seeking reachability checks.
    /// </summary>
    internal PetSocialState BuildPetSocialState(string petId, double nowMs)
    {
        var personality = Personality;
        // P3-004 R3 F-01: Use the same effective geometry for both center
        // and width. When the window has not been laid out yet (ActualWidth
        // is 0, e.g. never shown), fall back to the configured Width/Height
        // so that clearance checks use the real window extent instead of 0.
        var effectiveWidth = ActualWidth > 0 ? ActualWidth : Width;
        var effectiveHeight = ActualHeight > 0 ? ActualHeight : Height;
        var centerX = Left + effectiveWidth / 2.0;
        var centerY = Top + effectiveHeight / 2.0;

        var isBusy = _isDragging || _isPettingCandidate || _isPhysicsActive ||
                     _isExpressingGrievance || _isProbing || _isAppPaused ||
                     _restReminder.IsActive;

        // P3-003: Determine monitor and bottom-edge status.
        string? monitorId = null;
        double bottomEdgeY = 0;
        var isOnBottomEdge = false;
        // P3-004: Work area bounds in DIP for chase clearance checks.
        double? workAreaLeft = null;
        double? workAreaRight = null;

        if (!_isPhysicsActive && !_isDragging)
        {
            var monitors = TestMonitorList ?? MonitorInterop.EnumerateMonitors();
            if (monitors.Count > 0)
            {
                var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
                var foot = CoordinateTransform.WindowTopLeftToFootCenter(
                    Left, Top, Width, Height, dpiX, dpiY);
                // P3-003 R1 F-03: Use FindMonitor only — NOT FindNearestMonitor.
                // If the pet is off-screen or in a gap, monitorId stays null,
                // which makes SameMonitor return false and prevents seeking
                // an unreachable target. FindNearestMonitor is only for
                // position recovery (ClampToWorkArea), not for proving
                // reachability.
                var monitor = MonitorLocator.FindMonitor(monitors, foot.FootCenterX, foot.FootCenterY);
                if (monitor is not null)
                {
                    var mon = monitor.Value;
                    monitorId = mon.DeviceName;

                    // Bottom edge Y in DIP: convert work area bottom from physical px to DIP.
                    var waBottomPx = mon.WorkArea.Bounds.Bottom;
                    bottomEdgeY = waBottomPx / dpiY - effectiveHeight / 2.0;

                    // P3-004: Work area left/right in DIP for chase clearance.
                    workAreaLeft = mon.WorkArea.Bounds.Left / dpiX;
                    workAreaRight = mon.WorkArea.Bounds.Right / dpiX;

                    // Pet is on the bottom edge if its center Y is within tolerance
                    // of the bottom-edge center Y (i.e. foot is at or near wa.Bottom).
                    var footBottomDiffPx = Math.Abs(waBottomPx - foot.FootCenterY);
                    // Tolerance: 2 DIP in physical pixels.
                    isOnBottomEdge = footBottomDiffPx <= 2 * dpiY;
                }
            }
        }

        return new PetSocialState(
            petId,
            personality,
            centerX,
            centerY,
            isDragging: _isDragging,
            isPettingCandidate: _isPettingCandidate,
            isPhysicsActive: _isPhysicsActive,
            isExpressingGrievance: _isExpressingGrievance,
            isProbing: _isProbing,
            isPaused: _isAppPaused,
            lastActivityMs: isBusy ? nowMs : _lastSocialActivityMs,
            monitorId: monitorId,
            bottomEdgeY: bottomEdgeY,
            isOnBottomEdge: isOnBottomEdge,
            workAreaLeft: workAreaLeft,
            workAreaRight: workAreaRight,
            windowWidth: effectiveWidth);
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

        // R2 F-01：晃动旋转中心=帧偏移补偿后的素材脚点
        // （footAnchor + 本帧偏移）。与帧偏移在同一次 UpdateFrame 更新——
        // 晃动中切换动作帧（如 idle→curious，offsetY 0→9）时中心随新帧
        // 偏移同步，素材脚点经完整变换（帧偏移+旋转）后位置不变，
        // 与只应用帧偏移时一致。窗口位置不受影响。
        WobbleTransform.CenterX = _character.FootAnchorX + offsetX;
        WobbleTransform.CenterY = _character.FootAnchorY + offsetY;

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

        // P4-003 R1 F-01: A valid press on the pet must interrupt the night
        // sleep animation — the accepted gesture plays while the screen still
        // shows sleeping frames otherwise. Break the sleep action here so the
        // interaction is visible immediately; after release (EndPetting /
        // EndDrag both return to idle) the night wiring in OnUpdateTick
        // re-evaluates against the current local time and re-enters sleep if
        // it is still night. This does not touch the probe contract (F-10):
        // during a probe the current action is "curious", never "sleep".
        if (string.Equals(_animPlayer.CurrentActionId, "sleep", StringComparison.Ordinal))
        {
            _animPlayer.PlayAction("idle");
        }

        // P4-002: User pressed the pet — skip the rest-reminder performance.
        // The press enters the normal drag/petting flow. Per R-06:
        // "按下小熊实体可立即跳过，并照常进入拖动或抚摸。"
        SkipRestReminder();

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
            // F-02 R1: Head press immediately cancels social session — don't
            // wait for the next tick to detect petting candidate.
            CancelSocialImmediately(CancelReason.PettingCandidate, _pointerPressTimeMs);
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
        // F-02 R1: Immediately cancel any active social session when the user
        // starts dragging — don't wait for the next tick.
        var dragNowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        CancelSocialImmediately(CancelReason.DragStarted, dragNowMs);

        // P4-002: Cancel the rest-reminder performance on drag.
        _restReminder.Cancel();
        _restReminderCurrentAction = null;
        _restReminder.Reset();

        // F-10: Only a real drag takeover cancels the probe without restore.
        // This covers body-press-immediate-drag and head-press-convert-to-drag.
        CancelProbeForDrag();
        // P5A-001: Drag preempts the window perch immediately — hand back
        // position control, detach without restoring, and blacklist the
        // cancelled handle from auto-rebind (no pull-back after release).
        _petPerch?.CancelForDrag();
        // P5B-001: Drag preempts an in-progress window climb immediately —
        // end the route without restoring (drag anchor = current position).
        _petClimb?.CancelForDrag();
        // P5A-002: Drag takeover ends the perch wobble immediately (no
        // visual residue even if perch state diverges).
        EndPerchWobble();
        // P5A-003: A drag also cancels an in-progress perch pick (the pet
        // itself is being moved; the pick no longer has position ownership).
        EndPerchPick("cancelled");
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

    // ---------- P5A-001：窗口栖息（受守卫的位置写入） ----------

    /// <summary>栖息控制器（懒创建；仅 UI 线程访问）。</summary>
    internal PetWindowPerch Perch => _petPerch ??= new PetWindowPerch(this);

    /// <summary>P5B-001：攀爬控制器（懒创建；仅 UI 线程访问）。</summary>
    internal PetWindowClimb Climb => _petClimb ??= new PetWindowClimb(this);

    /// <summary>
    /// R8 F-03：栖息实际脱离（目标失效/遮挡/主动脱离/暂停/拖动——生产
    /// PetWindowPerch.OnTick 轮询 Cleared/no-landing/occluded 与
    /// Detach/DetachForPause/CancelForDrag 均收敛到 DetachInternal 收口）
    /// 时，经此释放仍由攀爬持有的落位表现（到达交接后 climb_land 在播）。
    /// 经 <c>_petClimb?</c> 调用避免纯栖息场景懒创建攀爬实例；攀爬自身
    /// 已清理时 no-op（IsPresentingClimb 守卫）。
    /// </summary>
    internal void ReleaseClimbForPerchDetach() => _petClimb?.CancelForPerchDetached();

    /// <summary>当前是否暂停（栖息轮询脱离判定用）。</summary>
    internal bool IsAppPausedNow => _isAppPaused;

    /// <summary>当前是否正在拖动。</summary>
    internal bool IsDraggingNow => _isDragging;

    /// <summary>
    /// 栖息位置写入是否被允许：拖动、抚摸（按压候选）、暂停、物理活动、
    /// 探头、社交会话期间不写位置（ARCHITECTURE §5 单一写入：
    /// 拖动/物理/社交/栖息互斥）。P5A-001 R1 F-02：栖息初选/跟随/实际
    /// 提交都必须通过这一受守卫边界；无法提交时栖息不得报告落位。
    /// </summary>
    /// <summary>
    /// 位置互斥核心旗（P5A-001 R3 F-02：栖息与社交共用的所有权判定）。
    /// 拖动/抚摸/暂停/物理/探头/攀爬路线任一为真时，除持有者外的位移
    /// 一律不得写位置。P5B-001：攀爬期间栖息初选/跟随与社交位移均让出。
    /// </summary>
    private bool HasCorePositionConflict => _isDragging || _isPettingCandidate || _isAppPaused
        || _isPhysicsActive || _isProbing || IsClimbOwned;

    /// <summary>栖息绑定是否持有位置（社交位移写入判定用；不触发懒创建）。</summary>
    private bool IsPerchOwned => _petPerch?.IsActive ?? false;

    /// <summary>P5B-001：攀爬路线是否持有位置（不触发懒创建）。</summary>
    private bool IsClimbOwned => _petClimb?.IsActive ?? false;

    /// <summary>
    /// P5A-001 R4 F-02：本宠物是否处于共享社交会话中（读协调器的
    /// 权威会话状态，及时生效）。窗口 A 的 tick 驱动追逐并实际移动
    /// 窗口 B 时，B 的本地 <c>_isSocialActive</c> 要等 B 自己的
    /// EvaluateSocialTick 才置位——栖息写入必须读共享会话，不能依赖
    /// 本地旗，否则追逐进行中 B 的栖息轮询/初选仍会写位置（双写入者）。
    /// 会话结束后立即返回 false，栖息权限恢复。
    /// </summary>
    private bool IsInSharedSocialSession
    {
        get
        {
            var coordinator = Resolve(
                TestSocialCoordinator, StateContext?.SocialCoordinator, a => a.SocialCoordinator);
            if (coordinator?.CurrentSession is { } session && session.IsActive)
            {
                var myPetId = StateContext?.PetId ?? "bear-1";
                return session.InitiatorId == myPetId || session.TargetId == myPetId;
            }
            return false;
        }
    }

    internal bool IsPerchWriteAllowed => !HasCorePositionConflict && !_isSocialActive
        && !IsInSharedSocialSession;

    /// <summary>
    /// P5B-001：攀爬自身的位置写入判定。与 <see cref="IsPerchWriteAllowed"/>
    /// 共用互斥核心，但不包含攀爬旗（否则攀爬自我阻塞）。
    /// P5B-001 R1 F-01：起攀（首分支）还拒绝已持有位置的栖息绑定与本地/
    /// 共享社交占用——已栖息或社交活跃时新起攀必须 RejectedBusy，不得
    /// 绑定与攀爬并存；第二分支（攀爬已持有）保持让位于拖动/抚摸/暂停/
    /// 物理/探头/社交（攀爬完成才交接栖息，两者不并发）。
    /// </summary>
    internal bool IsClimbWriteAllowed => (!HasCorePositionConflict && !IsPerchOwned
        && !_isSocialActive && !IsInSharedSocialSession)
        || (IsClimbOwned
            && !_isDragging && !_isPettingCandidate && !_isAppPaused
            && !_isPhysicsActive && !_isProbing && !_isSocialActive
            && !IsInSharedSocialSession);

    /// <summary>
    /// P5A-001 R3 F-02：社交位移（seek/chase 步进）的位置写入判定。
    /// 与栖息 <see cref="IsPerchWriteAllowed"/> 共用同一互斥核心
    /// （<see cref="HasCorePositionConflict"/>）；此外栖息绑定持有时
    /// 社交位移不得写位置——栖息与社交互斥，先到者持有位置，另一方让出
    /// （社交期间栖息经 _isSocialActive 让出，反向同理）。
    /// </summary>
    internal bool IsSocialPositionWriteAllowed => !HasCorePositionConflict && !IsPerchOwned;

    // ---------- P5A-002：栖息跟随晃动反馈（纯视觉，不写位置） ----------

    private const double PerchWobbleMaxAngleDeg = 10.0;
    private const double PerchWobblePeriodMs = 260.0;
    private const double PerchWobbleDecayMs = 220.0;
    private const double PerchWobbleEndAngleDeg = 0.4;
    private const double PerchWobbleMaxDurationMs = 1200.0;

    /// <summary>晃动起始时刻（-1=未晃动）。目标持续移动时每次成功跟随
    /// 都重置，衰减包络保证停止移动后短暂回稳。</summary>
    private double _perchWobbleStartMs = -1.0;

    /// <summary>当前是否正在栖息晃动（探针/测试观测）。</summary>
    internal bool IsPerchWobbling => _perchWobbleStartMs >= 0;

    /// <summary>当前晃动角度（度；0=无残留）。</summary>
    internal double PerchWobbleAngleDeg => WobbleTransform.Angle;

    /// <summary>
    /// P5A-002：栖息跟随成功（follow Landed）——启动一次短暂晃动。
    /// 只改画面（WobbleTransform 绕素材脚点旋转），不改窗口位置；
    /// 落位/跟随/脱离逻辑不变。
    /// R1-2：正在晃动时保留相位，不重置起始时刻——每次跟随都从
    /// 正弦零点重启会让小熊反复只向一侧摆；连续跟随期间按原相位
    /// 继续振荡（角度有正有负），停止跟随后衰减包络内自动回稳。
    /// </summary>
    internal void NotifyPerchFollowLanded()
    {
        if (_perchWobbleStartMs >= 0)
            return; // 正在晃动：保留相位（R1-2）
        _perchWobbleStartMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
    }

    /// <summary>
    /// P5A-002：立即结束晃动并清除角度（拖动接管/暂停/脱离时调用），
    /// 保证打断后画面无残留。
    /// </summary>
    internal void EndPerchWobble()
    {
        _perchWobbleStartMs = -1.0;
        WobbleTransform.Angle = 0;
    }

    /// <summary>
    /// P5A-002：按衰减振荡推进晃动画面。幅度低于阈值或超时后自动回稳
    /// （角度归零）。未晃动时零开销。
    /// </summary>
    private void UpdatePerchWobble(double nowMs)
    {
        if (_perchWobbleStartMs < 0)
            return;
        var t = nowMs - _perchWobbleStartMs;
        if (t < 0)
        {
            EndPerchWobble(); // 时钟回退（暂停补偿等）：安全结束
            return;
        }
        var amplitude = PerchWobbleMaxAngleDeg * Math.Exp(-t / PerchWobbleDecayMs);
        if (t >= PerchWobbleMaxDurationMs || amplitude < PerchWobbleEndAngleDeg)
        {
            EndPerchWobble();
            return;
        }
        // R2 F-01：旋转中心（帧偏移补偿后的素材脚点）由 UpdateFrame 与
        // 帧偏移同一次更新（帧切换时保持同步），此处不再设置。
        WobbleTransform.Angle = amplitude * Math.Sin(2.0 * Math.PI * t / PerchWobblePeriodMs);
    }

    // ---------- P5A-003：窗口栖息正式入口（拾取模式 + 主动离开） ----------

    private const double PerchPickTimeoutMs = 15000.0;
    private const int PerchPickVkLeftButton = 0x01; // VK_LBUTTON

    /// <summary>
    /// P5B-002：拾取意图——点击目标后的动作（栖息落位或攀爬起攀）。
    /// 两种意图共用同一拾取状态机（超时、边沿触发按下/释放、点自身
    /// 取消、资格校验、乱开菜单/拖动/暂停取消全部共享），只在点击合格
    /// 目标后的分派不同，避免复制一份状态机造成行为漂移。
    /// </summary>
    private enum WindowPickIntent { Perch, Climb }

    private WindowPickIntent _pickIntent = WindowPickIntent.Perch;

    private bool _perchPickActive;
    private bool _perchPickAwaitRelease;
    private double _perchPickStartMs = -1.0;
    private readonly Dictionary<uint, string> _perchPickPidNames = new();

    /// <summary>当前是否处于拾取模式（探针/测试观测；两种意图共用）。</summary>
    internal bool IsPerchPickActive => _perchPickActive;

    /// <summary>P5B-002：当前拾取是否为攀爬意图（探针/测试观测）。</summary>
    internal bool IsClimbPickActive => _perchPickActive && _pickIntent == WindowPickIntent.Climb;

    /// <summary>拾取是否仍在等待初始左键释放（边沿触发；探针/测试观测）。</summary>
    internal bool IsPerchPickAwaitingRelease => _perchPickAwaitRelease;

    /// <summary>最近一次拾取轮询观察到的左键按下状态（探针/测试观测）。</summary>
    internal bool LastPerchPickLeftDown { get; private set; }

    /// <summary>
    /// 最近一次拾取流程的终态/当前状态（探针/测试观测）：
    /// none / active / selected / rejected-busy / rejected-ineligible /
    /// rejected-landing / rejected-too-far / rejected-occluded /
    /// rejected-active / started / cancelled-self / cancelled-timeout /
    /// cancelled。
    /// 「started」表示攀爬意图起攀成功（栖意图选中为 selected）；
    /// rejected-* 均为拒绝且不移动。
    /// </summary>
    internal string LastPerchPickStatus { get; private set; } = "none";

    /// <summary>
    /// P5A-003 测试缝：覆盖拾取输入读取（返回 (左键按下, X, Y) 物理
    /// 像素）。仅测试使用；生产轮询读真实 GetAsyncKeyState/GetCursorPos。
    /// </summary>
    internal Func<(bool LeftDown, int X, int Y)>? TestPerchPickInput;

    /// <summary>
    /// P5A-003 测试缝：覆盖点选拾取的目标快照采集（返回 null 等价于
    /// 该点没有可解析的根窗口）。仅测试使用。
    /// </summary>
    internal Func<int, int, ExternalWindowSnapshot?>? TestPerchPickSnapshotSource;

    /// <summary>P5A-003 测试缝：覆盖拾取超时（毫秒）；null=生产 15s。</summary>
    internal double? TestPerchPickTimeoutMs;

    /// <summary>
    /// P5A-003：进入栖息拾取模式（点击目标后落位）。位置所有权被占用
    /// （拖动/抚摸/暂停/物理/探头/社交）时不进入并记 rejected-busy；
    /// 已激活时重复进入为无操作。
    /// </summary>
    internal void StartPerchPick() => StartPick(WindowPickIntent.Perch);

    /// <summary>
    /// P5B-002：进入攀爬拾取模式（点击目标后按既有攀爬路线起攀）。
    /// 与 <see cref="StartPerchPick"/> 共用同一拾取状态机；起攀距离与
    /// 顶部落位资格由 <see cref="PetWindowClimb.TryStart"/> 沿既有守卫
    /// 校验（拒绝不移动）。处于栖息绑定中时经攀爬守卫拒绝起攀。
    /// </summary>
    internal void StartClimbPick() => StartPick(WindowPickIntent.Climb);

    private void StartPick(WindowPickIntent intent)
    {
        if (_perchPickActive)
            return;
        if (!IsPerchWriteAllowed)
        {
            LastPerchPickStatus = "rejected-busy";
            return;
        }
        _pickIntent = intent;
        _perchPickActive = true;
        // 边沿触发：进入拾取时若左键正处于按下状态（如菜单点击残留或
        // 用户按住左键进入），必须先观察到释放，再等待新的按下。否则
        // 拾取会在首个轮询帧立即把"已按下的键"当作一次新点击，误选
        // 光标下方的无关窗口（P5A-003 M-16 驱动首轮实测缺陷）。
        _perchPickAwaitRelease = true;
        _perchPickStartMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        LastPerchPickStatus = "active";
    }

    /// <summary>结束拾取模式并记录终态。</summary>
    private void EndPerchPick(string status)
    {
        if (!_perchPickActive)
            return;
        _perchPickActive = false;
        _perchPickAwaitRelease = false;
        _perchPickStartMs = -1.0;
        LastPerchPickStatus = status;
    }

    /// <summary>
    /// P5A-003：拾取轮询（OnUpdateTick ~30 FPS）。左键按下时按光标点
    /// 解析目标根窗口：点中宠物自身取消；外部窗口先资格校验再
    /// <see cref="PetWindowPerch.TrySelectExplicit"/>（落位/跟随/脱离
    /// 语义全部复用 P5A-001，不改）。任何终态都退出拾取模式。
    /// </summary>
    private void UpdatePerchPick(double nowMs)
    {
        if (!_perchPickActive)
            return;

        // 开菜单/拖动/暂停等打断由各自入口取消；此处兜底位置所有权
        // 与暂停（挂钟由 TestGetElapsedMs 注入时可测）。
        if (!IsPerchWriteAllowed || IsAppPausedNow)
        {
            EndPerchPick("cancelled");
            return;
        }

        var timeoutMs = TestPerchPickTimeoutMs ?? PerchPickTimeoutMs;
        if (nowMs - _perchPickStartMs >= timeoutMs)
        {
            EndPerchPick("cancelled-timeout");
            return;
        }

        (bool leftDown, int x, int y) input;
        if (TestPerchPickInput is not null)
            input = TestPerchPickInput();
        else
        {
            var leftDown = (GetAsyncKeyState(PerchPickVkLeftButton) & 0x8000) != 0;
            LastPerchPickLeftDown = leftDown;
            if (!leftDown)
            {
                // 观察到释放：清除边沿触发等待标记（与测试缝路径同语义；
                // 否则生产路径永远不清标记，真实点击全部被吞，15s 超时
                // ——M-16 实测缺陷：awaitRelease=True 且 leftDown=False）。
                _perchPickAwaitRelease = false;
                return; // 等待下一次按下
            }
            if (!GetCursorPos(out var cursorPt))
                return;
            input = (true, cursorPt.X, cursorPt.Y);
        }
        if (!input.leftDown)
        {
            // 觢察到释放：清除边沿触发的等待标记（此后等待新的按下）。
            _perchPickAwaitRelease = false;
            return;
        }
        if (_perchPickAwaitRelease)
            return; // 进入拾取时键仍被按住：忽略，等释放后的新按下。

        // 点中宠物自身：取消拾取（不选自己）。
        var petRect = GetPetRectPhysicalPx();
        if (input.x >= petRect.Left && input.x <= petRect.Left + petRect.Width
            && input.y >= petRect.Top && input.y <= petRect.Top + petRect.Height)
        {
            EndPerchPick("cancelled-self");
            return;
        }

        var snapshot = TestPerchPickSnapshotSource is not null
            ? TestPerchPickSnapshotSource(input.x, input.y)
            : WindowPerchInterop.SnapshotWindowFromPoint(
                input.x, input.y, _hwnd, ResolvePerchPickProcessName);
        if (snapshot is null)
        {
            EndPerchPick("rejected-ineligible");
            return;
        }

        var selfPid = (uint)Environment.ProcessId;
        if (ExternalWindowEligibility.Evaluate(snapshot, selfPid) != ExternalWindowExclusion.None)
        {
            EndPerchPick("rejected-ineligible");
            return;
        }

        // 点击合格目标：按进入时的意图分派。
        if (_pickIntent == WindowPickIntent.Climb)
        {
            // P5B-002：攀爬意图 → 沿既有控制器起攀（资格/距离/落位/
            // 遮挡拒绝全部由 TryStart 沿既有守卫校验，拒绝不移动）。
            var result = Climb.TryStart(snapshot, selfPid, nowMs);
            EndPerchPick(result switch
            {
                PetClimbStartResult.Started => "started",
                PetClimbStartResult.RejectedBusy => "rejected-busy",
                PetClimbStartResult.RejectedTooFar => "rejected-too-far",
                PetClimbStartResult.RejectedNoLanding => "rejected-landing",
                PetClimbStartResult.RejectedOccluded => "rejected-occluded",
                PetClimbStartResult.RejectedIneligible => "rejected-ineligible",
                PetClimbStartResult.RejectedAlreadyActive => "rejected-active",
                _ => "rejected",
            });
        }
        else
        {
            // P5A-003：显式选择——落位/重选/旧绑定保留语义全部由
            // P5A-001 已验收的 TrySelectExplicit 决定，本入口不改任何
            // 栖息行为。
            EndPerchPick(Perch.TrySelectExplicit(snapshot, selfPid) ? "selected" : "rejected-landing");
        }
    }

    private string ResolvePerchPickProcessName(uint pid)
    {
        if (_perchPickPidNames.TryGetValue(pid, out var cached))
            return cached;
        string name;
        try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; }
        catch { name = ""; }
        _perchPickPidNames[pid] = name;
        return name;
    }

    /// <summary>
    /// P5A-001 R1 F-01：宠物窗口的完整物理尺寸（当前显示器 DPI 下，
    /// 虚拟桌面物理像素）。供落位可见性校验与落脚区遮挡采样使用。
    /// </summary>
    internal (double WidthPx, double HeightPx) GetPetPhysicalSizePx()
    {
        var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
        return (Width * dpiX, Height * dpiY);
    }

    /// <summary>
    /// P5A-001：按脚底中心世界坐标（虚拟桌面物理像素）设置窗口位置。
    /// 受守卫位置写入（拖动/抚摸/暂停/物理/探头/社交互斥）；被更高
    /// 优先级行为占用时返回 false 且不移动。栖息位置不写入存档
    /// （不 NotifyStateDirty）。
    /// P5B-001 R1 F-01：所有权感知——攀爬步进通过 <paramref name="fromClimb"/>
    /// 标注为攀爬自身写入（攀爬持有位置，不阻塞自己）；非攀爬写入
    /// （栖息初选/跟随、测试直接调用）在攀爬活跃时拒绝——攀爬持有时
    /// 非攀爬位移不得提交。
    /// </summary>
    internal bool ApplyPerchFoot(PetWorldPosition foot, bool fromClimb = false)
    {
        // R4 F-02：栖息位移除互斥核心旗与本地社交旗外，还必须读共享
        // 社交会话（及时生效）——被追逐移动的窗口即使尚未 tick 也拒绝。
        if (_isDragging || _isPettingCandidate || _isAppPaused
            || _isPhysicsActive || _isProbing || _isSocialActive
            || IsInSharedSocialSession || (!fromClimb && IsClimbOwned))
            return false;
        var (dpiX, dpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);
        var (leftDip, topDip) = CoordinateTransform.FootCenterToWindowTopLeft(
            foot, Width, Height, dpiX, dpiY);
        Left = leftDip;
        Top = topDip;
        return true;
    }

    // ---------- P5B-001：窗口有限攀爬（隔离预览） ----------

    /// <summary>
    /// P5B-003 S2：播放攀爬阶段动作并设置朝向镜像。
    /// 仅当当前动作是空闲底态（idle 或夜间 sleep）或 climb_* 时播放，
    /// 避免覆盖更高优先级表达（好奇/委屈/拖动等）。
    /// R12 F-07：夜间 sleep 是低优先级空闲底态——成功起攀取得路线所有权
    /// 时必须让位给 climb_side；拒绝/取消路径不经本方法，因此不会因此
    /// 唤醒睡眠。<paramref name="mirrorLeft"/> 为 true 表示目标方向在
    /// 宠物右侧（素材默认朝左伸爪，需 ScaleX=-1）。
    /// R7 F-04：返回是否实际播放成功——被守卫拒绝或动作未注册（缺失）
    /// 时不播放、不改镜像，返回 false；调用方只在 true 时记录缓存，
    /// 且缺失回退不会把 idle 帧镜像显示。
    /// </summary>
    internal bool PlayClimbAction(string actionId, bool mirrorLeft)
    {
        var current = _animPlayer.CurrentActionId;
        if (current != "idle" && current != "sleep" && !IsClimbAction(current))
            return false;
        // F-04：动作未注册（素材缺失）不播放、不改镜像——保持当前 idle
        // 表现（AnimationPlayer 的 missing 兜底不参与攀爬专用调用）。
        if (!_character.Actions.ContainsKey(actionId))
            return false;
        ClimbMirrorTransform.ScaleX = mirrorLeft ? -1 : 1;
        _animPlayer.PlayAction(actionId);
        // 防御性复核：播放后当前动作必须是 climb_*（PlayAction 同步切
        // 换；此处兜底播放器与素材不一致的极端情况），否则复位镜像。
        if (!IsClimbAction(_animPlayer.CurrentActionId))
        {
            ClimbMirrorTransform.ScaleX = 1;
            return false;
        }
        return true;
    }

    /// <summary>P5B-003 S2：结束攀爬表现。默认立即回 idle 并复位镜像；
    /// keepLand=true（到达分支已交栖息）时不动，让 land once 自然播完。
    /// R7 F-03：任何清理路径都无条件复位镜像防残影；仅当前确为 climb_*
    /// 时才回 idle（不误清更高优先级表达）。</summary>
    internal void EndClimbAction(bool keepLand = false)
    {
        if (keepLand)
            return;
        ClimbMirrorTransform.ScaleX = 1;
        if (!IsClimbAction(_animPlayer.CurrentActionId))
            return;
        _animPlayer.PlayAction("idle");
    }

    /// <summary>P5B-003 S2：攀爬专用动作标识判定。</summary>
    private static bool IsClimbAction(string actionId)
        => actionId == "climb_side" || actionId == "climb_edge" || actionId == "climb_land";

    /// <summary>
    /// P5B-001：把宠物钳回可见工作区（路线失败/取消时回到可见区域）。
    /// 委托既有 <see cref="ClampWindowToWorkArea"/>（同一 DIP↔脚点
    /// 适配管线，目标显示器 DPI 一致）。
    /// </summary>
    internal void ClampWindowToVisibleWorkArea() => ClampWindowToWorkArea();

    /// <summary>
    /// P4-002: Check whether this window can start a rest-reminder performance.
    /// Returns true only when the pet is idle, visible, and not busy with
    /// any higher-priority interaction or state.
    /// </summary>
    internal bool CanStartRestReminder()
    {
        // R1 F-03: Only visible windows can receive a rest-reminder.
        // CreatePetWindow does not call Show(); windows that haven't been
        // shown (e.g. in isolated tests or before startup) must be skipped.
        if (!IsVisible)
            return false;

        // R2 F-03: Check the AUTHORITATIVE shared social state from the
        // global coordinator, not only this window's _isSocialActive
        // flag. The local flag is only updated by EvaluateSocialTick,
        // so it lags behind a session started by another window's tick.
        // Reading the coordinator session here blocks a candidate that
        // has already joined a social session between two ticks.
        var coordinator = Resolve(
            TestSocialCoordinator, StateContext?.SocialCoordinator, a => a.SocialCoordinator);
        if (coordinator?.CurrentSession is { } session && session.IsActive)
        {
            var myPetId = StateContext?.PetId ?? "bear-1";
            if (session.InitiatorId == myPetId || session.TargetId == myPetId)
                return false;
        }

        if (_isDragging || _isPettingCandidate || _isPhysicsActive ||
            _isExpressingGrievance || _isProbing || _isAppPaused ||
            _isSocialActive || _isContextMenuOpen)
            return false;

        // Don't start if a performance is already active.
        if (_restReminder.IsActive)
            return false;

        // R2 F-03: Require BOTH the stretch (opening signal) and the
        // rest (quiet pose) actions. Missing either one would consume a
        // reminder on an incomplete, unrecognizable performance, so the
        // candidate is skipped entirely.
        if (!_character.Actions.ContainsKey("stretch") ||
            !_character.Actions.ContainsKey("rest"))
            return false;

        return true;
    }

    /// <summary>
    /// P4-002: Start the rest-reminder performance. Plays the stretch action
    /// first (clear opening signal), then transitions to the rest action
    /// (quiet resting pose) for the remaining duration (~30 seconds total).
    /// Called by App.OnRestReminderRequested when this window is selected.
    /// </summary>
    internal void StartRestReminder(double nowMs)
    {
        // Calculate the stretch action's total duration from the character def.
        double stretchDurationMs = 0;
        if (_character.Actions.TryGetValue("stretch", out var stretchAction))
        {
            foreach (var f in stretchAction.Frames)
                stretchDurationMs += f.DurationMs;
        }

        _restReminder.Start(nowMs, stretchDurationMs);

        // Play the initial action.
        var action = _restReminder.Tick(nowMs);
        if (action is not null)
        {
            _restReminderCurrentAction = action;
            _animPlayer.PlayAction(action);
            _hoverDetector.Reset();
            UpdateFrame();
        }
        else
        {
            // No valid action to play — reset and skip.
            _restReminder.Reset();
        }
    }

    /// <summary>
    /// P4-002: Cancel any active rest-reminder performance. Called on pause,
    /// lock screen, sleep, exit, recover, and window removal. The performance
    /// ends without replay (R-06).
    /// </summary>
    internal void CancelRestReminder()
    {
        if (_restReminder.IsActive)
        {
            _restReminder.LifecycleCancel();
            // Return to idle if we were showing a rest-reminder action.
            if (_restReminderCurrentAction is not null &&
                _animPlayer.CurrentActionId == _restReminderCurrentAction &&
                !_isDragging && !_isProbing && !_isExpressingGrievance)
            {
                _animPlayer.PlayAction("idle");
                UpdateFrame();
            }
            _restReminderCurrentAction = null;
            _restReminder.Reset();
        }
    }

    /// <summary>
    /// P4-002: Skip the rest-reminder performance (user pressed the pet body).
    /// The performance ends immediately; the caller proceeds with the normal
    /// drag/petting interaction flow.
    /// R1 F-02: Also release the animation player from stretch/rest so it
    /// returns to idle. If the caller starts drag or petting, that branch
    /// will set its own action (overwriting idle). If the user releases
    /// without converting (head short-press → EndPetting), the player is
    /// already on idle — no stale stretch/rest frame remains.
    /// </summary>
    private void SkipRestReminder()
    {
        if (_restReminder.IsActive)
        {
            _restReminder.Skip();
            _restReminderCurrentAction = null;
            _restReminder.Reset();
            // R1 F-02: Release the animation player from stretch/rest.
            // The caller (BeginCapturedPointerInteraction) will overwrite
            // this with drag/petting action if it proceeds. If not (head
            // short-press release), idle is the correct state.
            if (!_isDragging && !_isProbing && !_isExpressingGrievance)
            {
                _animPlayer.PlayAction("idle");
                UpdateFrame();
            }
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

        // P3-003 R1 F-01: Notify the social coordinator that this pet was
        // released from a user drag. This is the ONLY trigger for seeking —
        // spontaneous idle cannot start a seeking session. Only call on
        // normal release (not cancellation paths like menu/capture loss).
        if (wasDragging && !isCancellation)
        {
            var coordinator = Resolve(
                TestSocialCoordinator, StateContext?.SocialCoordinator, a => a.SocialCoordinator);
            if (coordinator is not null)
            {
                var releaseMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
                coordinator.NotifyPetReleased(
                    StateContext?.PetId ?? "bear-1",
                    releaseMs,
                    wasThrown: _lastReleaseResult.ShouldThrow);
            }
        }

        // Input and position stop immediately. Only the face settles briefly.
        // R9 F-05: Only a real drag ending resets the animation. Opening the
        // context menu without an in-progress drag (EndDrag is also the
        // menu-open cancellation entry) must not overwrite an active
        // rest-reminder performance — the reminder stays active, so the
        // stretch/rest animation must stay on screen until it ends.
        if (wasDragging)
        {
            _animPlayer.PlayAction("idle");
            if (!_isContextMenuOpen)
                _dragRelease?.Start(TestGetElapsedMs?.Invoke() ?? GetElapsedMs());
        }
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
        // P3-003 R1 F-01: If the simulator was never launched (e.g. test
        // sets _isPhysicsActive=true directly without a real throw), do
        // nothing — don't write Left/Top from a zero-initialized state.
        // In production, EndDrag always Launches before setting the flag.
        if (!_physicsSimulator.IsLaunched)
            return;

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
        // P3-003 R3 F-01: Use TestMonitorList when available (same as
        // BuildWorkArea) so test-injected monitors with controlled DPI
        // are used for coordinate conversion, keeping physics and
        // position recovery consistent in the test environment.
        var monitors = TestMonitorList ?? MonitorInterop.EnumerateMonitors();
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
        var monitors = TestMonitorList ?? MonitorInterop.EnumerateMonitors();
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
        // P5A-001 R2 F-02: Recover takes position ownership back —
        // release any window-perch binding so the recovered pet is
        // not re-dragged when the old target moves again.
        _petPerch?.Detach("recovered");

        // P5B-001 R1 F-01: Recover must also cancel an in-progress climb
        // route; otherwise the route stays active and the next Tick can
        // drag the pet back toward the old target.
        _petClimb?.CancelForRecover();

        // P5B-002 R2 F-01: Recover must synchronously end a pending
        // perch/climb pick (selection not yet made). The tray entry calls
        // RecoverPet directly, bypassing EnterContextMenuMode, so without
        // this the pick stays active and a later ordinary click can still
        // start the interrupted target selection. Mirrors PauseAnimation.
        EndPerchPick("cancelled");

        // F-02 R1: Immediately cancel any active social session on recover.
        var recoverNowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
        CancelSocialImmediately(CancelReason.RecoverRequested, recoverNowMs);

        // P4-002 R1 F-02: Cancel the rest-reminder performance on recover.
        // Use CancelRestReminder (not bare Cancel) so the animation player
        // is released from stretch/rest and returns to idle.
        CancelRestReminder();

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
        // F-02 R1: Immediately cancel any active social session on pause.
        // The timer is about to stop, so no further tick will fire to cancel
        // the session through the normal coordinator.Tick path.
        CancelSocialImmediately(CancelReason.Paused,
            TestGetElapsedMs?.Invoke() ?? GetElapsedMs());

        // P4-002: Cancel the rest-reminder performance on pause.
        // Per R-06: "暂停...时结束正在播放的提醒，恢复后不续播。"
        _restReminder.LifecycleCancel();
        if (_restReminderCurrentAction is not null &&
            _animPlayer.CurrentActionId == _restReminderCurrentAction)
        {
            _restReminderCurrentAction = null;
            // Don't PlayAction("idle") here — the player is about to be paused.
            // The CancelRestReminder from App.OnPauseToggled handles idle switch.
        }
        _restReminder.Reset();

        // P5A-001: Pause detaches the window perch (position unchanged);
        // resume does not re-bind the old target.
        _petPerch?.DetachForPause();

        // P5B-001: Pause ends an in-progress window climb route (resume
        // does not continue the old route).
        _petClimb?.CancelForPause();

        // P5A-003 R1 F-01: Pause must synchronously end the perch pick
        // mode. The update timer is about to stop, so the pause fallback
        // inside UpdatePerchPick can never run; without this the pending
        // pick survives the pause and may consume a click after resume.
        EndPerchPick("cancelled");

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
    /// P3-002 R1: Internal accessor for curiosity active state (for testing).
    /// Used to verify that social expressions are actually applied to the
    /// correct window's visible behavior, not just coordinator state.
    /// </summary>
    internal bool IsCuriosityActive => _curiosity.IsActive;

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
        // P5A-001 R1 F-03：栖息中退出时，先停栖息轮询并解除绑定，
        // 把窗口移回栖息前的位置，保证存档坐标是休息位而非临时栖息位。
        _petPerch?.RestorePrePerchPositionForExit();

        // P5B-001：攀爬中退出时，结束路线并把窗口移回爬行前位置，
        // 保证存档坐标是休息位而非路线中的临时位置。
        _petClimb?.CancelForExit();

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
    /// P3-003 R1 F-01: Test-only setter to simulate physics being active
    /// (e.g. after a throw) without going through the full drag→throw chain.
    /// </summary>
    internal void TestSetPhysicsActive(bool value) => _isPhysicsActive = value;

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

    /// <summary>
    /// P3-002: Public accessor for the social-active flag (for testing/diagnostics).
    /// True while a mutual-notice social session is active for this pet.
    /// When true, rare-event Evaluate is blocked (via isSocialActive parameter).
    /// </summary>
    public bool IsSocialActive => _isSocialActive;

    /// <summary>
    /// P3-001-S2 R6 F-04: Test-only entry to drive a single OnUpdateTick
    /// without relying on the DispatcherTimer. This lets tests verify
    /// physics tick behavior with injected clock values.
    /// </summary>
    internal void OnUpdateTickTest()
    {
        OnUpdateTick(null, EventArgs.Empty);
    }

    /// <summary>
    /// P3-003 R2 F-01: Test-only entry to simulate a production drag-release
    /// through the real EndDrag code path. Sets _isDragging=true (as
    /// StartDragging would), optionally moves the window to a new position
    /// (as MouseMove would), then calls EndDrag(isCancellation=false) which
    /// is the production release path that calls NotifyPetReleased.
    /// This does NOT use TestSetPhysicsActive — it goes through the real
    /// EndDrag → EvaluateRelease → NotifyPetReleased chain.
    /// </summary>
    internal void TestSimulateDragRelease(double newLeft, double newTop)
    {
        _isDragging = true;
        Left = newLeft;
        Top = newTop;
        EndDrag(isCancellation: false);
    }

    /// <summary>
    /// P3-003 R2 F-01: Test-only entry to simulate a CANCELLED drag
    /// (menu/capture loss/closing). Calls EndDrag(isCancellation: true)
    /// which must NOT call NotifyPetReleased — no seek candidate is
    /// established from a cancelled release.
    /// </summary>
    internal void TestSimulateDragCancel()
    {
        _isDragging = true;
        EndDrag(isCancellation: true);
    }

    /// <summary>
    /// P3-001-S2 R7 F-04: Test-only accessor to verify that the update
    /// timer has been stopped after window close. The timer is started
    /// in the constructor and stopped in OnClosed; tests need to confirm
    /// that TestCleanup actually stops it for unshown windows.
    /// </summary>
    internal bool IsUpdateTimerEnabled => _updateTimer.IsEnabled;

    protected override void OnClosed(EventArgs e)
    {
        // F-03: Clean up drag state on window close — cancellation, not a release.
        if (_isDragging)
            EndDrag(isCancellation: true);
        // P1-003: Clean up petting state on window close.
        if (_isPettingCandidate)
            EndPetting();
        // P5A-001 R1 F-03：关闭窗口时停止栖息轮询并解除目标绑定，
        // 防止已关闭宠物的栖息计时器继续读外部目标或写位置。
        _petPerch?.Detach("closed");
        // P5B-001：关闭窗口时结束攀爬路线（防已关闭宠物的攀爬继续
        // 读外部目标或写位置）。
        _petClimb?.CancelForExit();
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
