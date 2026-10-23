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
using DesktopPet.Core.Input;

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
    private readonly PetCharacterDef _character;
    private readonly Dictionary<string, BitmapImage> _imageCache = [];
    private readonly string _assetDir;
    private string _lastDisplayedFrame = string.Empty;
    private readonly HashSet<string> _missingActionWarnings = [];

    private bool _isDragging;
    private System.Windows.Point _dragOffset;

    // P1-003: Petting detection state (R-02).
    // Head zone = upper 1/3 of the pet image (resource-driven zones deferred).
    // Petting: head press + small movement accumulated >= 1s → +5 affinity, 2s cooldown.
    // Drag: body press → existing drag logic.
    private bool _isPettingCandidate;
    private double _pettingStartMs;
    private double _pettingAccumulatedMs;
    private double _pettingCooldownUntilMs;
    private System.Windows.Point _pettingLastPos;
    private const double PettingRequiredMs = 1000.0;
    private const double PettingCooldownMs = 2000.0;
    private const double PettingMaxMovePerFramePx = 8.0; // small movement threshold in DIP
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

    // P1-002: Mouse sampler for speed/distance tracking (R-01).
    // Fed by GetCursorPos at ~30 Hz in OnUpdateTick.
    // Cleared on drag start, menu open, pause, and capture loss to avoid
    // consuming stale samples after an interruption.
    private readonly MouseSampler _mouseSampler = new();

    // P1-002: Last known DPI scale of the pet window's monitor.
    // Updated each tick via MonitorInterop.GetWindowDpiScale.
    // When it changes (e.g. window moved to a different-DPI monitor),
    // the sampler is cleared to avoid mixing samples from different DPI contexts.
    private double _lastDpiScale = 1.0;

    // P1-002: Hover and swipe detectors (Core pure logic).
    private readonly HoverDetector _hoverDetector = new();
    private readonly SwipeDetector _swipeDetector = new();

    // F-04: Test injection points for controlled mouse position, clock, and DPI.
    // When non-null, OnUpdateTick uses these instead of the Win32 defaults.
    // Production code leaves these null — no behavior change.
    internal Func<(int x, int y)>? TestGetCursorPos;
    internal Func<double>? TestGetElapsedMs;
    internal Func<double>? TestGetDpiScale;

    // P1-003: Test injection points for emotion objects (G-01/G-02 runtime tests).
    // When non-null, OnUpdateTick uses these instead of App.Current.
    // Production code leaves these null — OnUpdateTick falls back to App.Current.
    internal EmotionDecayAccumulator? TestEmotionAccumulator;
    internal CornerTracker? TestCornerTracking;
    internal GrievanceExpressionState? TestGrievanceExpression;

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

    // P1-001: Monotonic time source for AnimationPlayer.
    private static double _startTimeMs = -1;
    private static double GetElapsedMs()
    {
        if (_startTimeMs < 0)
            _startTimeMs = Environment.TickCount64;
        return Environment.TickCount64 - _startTimeMs;
    }

    private static string FindAssetDirectory()
    {
        // Look for assets/pets/default relative to the executable.
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
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
        // When a Once action (e.g. dodge) completes, return to idle.
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
        EndDrag();

        // P1-002: Clear sampler while menu is open.
        _mouseSampler.Clear();

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

            // P1-002: Clear sampler after menu closes so resume doesn't
            // consume stale samples from before the menu.
            _mouseSampler.Clear();

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
    /// P1-002: Also samples mouse position via GetCursorPos and feeds the
    /// MouseSampler. Sampling is skipped during drag, menu, and pause to
    /// avoid consuming stale or irrelevant positions.
    /// DPI is read from the window's monitor each tick; when it changes,
    /// the sampler is cleared to avoid mixing samples across DPI contexts.
    /// Hover and swipe detectors are fed after sampling; triggers are
    /// suppressed during drag, menu, and pause.
    /// </summary>
    private void OnUpdateTick(object? sender, EventArgs e)
    {
        var nowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();

        // P1-002: Update DPI scale from the window's actual monitor.
        // F-04: TestGetDpiScale injects a controlled DPI value for testing;
        // production reads from MonitorInterop. The change detection, config
        // update, and sampler clear are shared — only the DPI *source* differs.
        double currentDpi;
        if (TestGetDpiScale is not null)
            currentDpi = TestGetDpiScale.Invoke();
        else if (_hwnd != IntPtr.Zero)
            currentDpi = MonitorInterop.GetWindowDpiScale(_hwnd).ScaleX;
        else
            currentDpi = _lastDpiScale;

        if (Math.Abs(currentDpi - _lastDpiScale) > 0.001)
        {
            _lastDpiScale = currentDpi;
            _mouseSampler.Config.DpiScale = currentDpi;
            _swipeDetector.Config.DpiScale = currentDpi;
            _mouseSampler.Clear();
        }

        // P1-002: Sample mouse position for speed/distance tracking.
        // F-04: TestGetCursorPos injects a controlled position for testing.
        // P1-003: Also skip during petting candidate state.
        MouseSampleResult sampleResult = MouseSampleResult.Invalid;
        if (!_isDragging && !_isPettingCandidate && !_isContextMenuOpen && !_isAppPaused)
        {
            if (TestGetCursorPos is not null)
            {
                var (cx, cy) = TestGetCursorPos.Invoke();
                sampleResult = _mouseSampler.Sample(new MouseSample(cx, cy, nowMs));
            }
            else if (GetCursorPos(out var pt))
            {
                sampleResult = _mouseSampler.Sample(new MouseSample(pt.X, pt.Y, nowMs));
            }
        }

        // P1-002: Feed hover and swipe detectors when not in an interrupt state.
        // P1-003: Also suppress during petting candidate.
        if (!_isDragging && !_isPettingCandidate && !_isContextMenuOpen && !_isAppPaused && sampleResult.IsValid)
        {
            // Compute pet entity rectangle in physical pixels for distance checks.
            var petRectPx = GetPetRectPhysicalPx();

            // Hover detection.
            var hoverResult = _hoverDetector.Update(
                sampleResult.Latest.X, sampleResult.Latest.Y,
                petRectPx, nowMs, IsOverVisiblePet(sampleResult.Latest.X, sampleResult.Latest.Y));
            if (hoverResult == HoverTrigger.Triggered)
                _animPlayer.PlayAction("curious");

            // Swipe detection.
            var swipeResult = _swipeDetector.Update(
                sampleResult, petRectPx, nowMs);
            if (swipeResult == SwipeTrigger.Triggered)
                _animPlayer.PlayAction("dodge");
        }
        else
        {
            // Reset detectors during interrupts to avoid stale state.
            _hoverDetector.Reset();
            _swipeDetector.Reset();
        }

        // P1-003: Advance emotion decay every tick (G-01: use float accumulator).
        // Decay runs even during drag/menu/petting — only paused stops it.
        // The accumulator handles clock rollback internally (F-02).
        // Test injection: TestEmotionAccumulator bypasses App.Current for unit tests.
        EmotionDecayAccumulator? emotionAcc = TestEmotionAccumulator;
        CornerTracker? cornerTrack = TestCornerTracking;
        GrievanceExpressionState? grievanceExpr = TestGrievanceExpression;
        if (emotionAcc is null && System.Windows.Application.Current is App app)
        {
            emotionAcc = app.EmotionAccumulator;
            cornerTrack = app.CornerTracking;
            grievanceExpr = app.GrievanceExpression;
        }

        if (!_isAppPaused && emotionAcc is not null)
        {
            emotionAcc.AdvanceTo(DateTime.UtcNow);

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
                            if (System.Windows.Application.Current is App app2)
                                app2.MarkStateDirty();
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
                        if (System.Windows.Application.Current is App app3)
                            app3.MarkStateDirty();
                    }
                }
                else if (grievanceExpr.ShouldExpress(today)
                    && !_isDragging && !_isPettingCandidate && !_isContextMenuOpen)
                {
                    // Start expression: play "turn away" action (reuse curious for now).
                    // Expression will last 10 seconds, then resets grievance.
                    _isExpressingGrievance = true;
                    _grievanceExpressionStartMs = nowMs;
                    _animPlayer.PlayAction("curious");
                }
            }
        }

        UpdateFrame();
    }

    /// <summary>
    /// P1-002: Get the pet entity rectangle in physical screen pixels.
    /// Used by hover/swipe detectors for distance-to-edge calculations.
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

        // F-04: Apply frame offset to the Image render transform so that
        // non-zero OffsetX/Y in the frame definition compensate for content
        // shifts and keep the visual foot at a stable world position.
        // GetFootOffset returns (OffsetX, OffsetY) directly as the Transform.
        var (offsetX, offsetY) = _animPlayer.GetFootOffset();
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

        // F-03: Capture first, only enter interaction state if capture succeeds.
        PetCanvas.CaptureMouse();
        if (!PetCanvas.IsMouseCaptured)
            return;

        // P1-003: Determine if press is on head zone (petting) or body (drag).
        var localPos = e.GetPosition(PetImage);
        var isHeadPress = IsInHeadZone(localPos);

        if (isHeadPress)
        {
            // Start petting candidate — may convert to drag if movement exceeds threshold.
            _isPettingCandidate = true;
            _pettingStartMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
            _pettingAccumulatedMs = 0;
            _pettingLastPos = localPos;
            _mouseSampler.Clear();
            e.Handled = true;
            return;
        }

        // F-02: PointToScreen returns physical pixels; Window.Left/Top are DIP.
        // Convert screen pixel offset to DIP using the device transform.
        var screenPosPx = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice
                        ?? Matrix.Identity;
        var screenPosDip = transform.Transform(screenPosPx);

        _isDragging = true;
        _dragOffset = new System.Windows.Point(screenPosDip.X - Left, screenPosDip.Y - Top);

        // P1-002: Clear mouse sampler during drag to avoid stale samples.
        _mouseSampler.Clear();

        // P1-001: Switch to drag action when dragging starts.
        _animPlayer.PlayAction("drag");
        UpdateFrame();

        e.Handled = true;
    }

    private void PetCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPettingCandidate)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndPetting();
                return;
            }

            var localPos = e.GetPosition(PetImage);
            var dx = localPos.X - _pettingLastPos.X;
            var dy = localPos.Y - _pettingLastPos.Y;
            var moveDist = Math.Sqrt(dx * dx + dy * dy);

            if (moveDist <= PettingMaxMovePerFramePx)
            {
                // Small movement — accumulate petting time.
                var nowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
                _pettingAccumulatedMs += nowMs - _pettingStartMs;
                _pettingStartMs = nowMs;
            }
            else
            {
                // Large movement — convert to drag.
                ConvertPettingToDrag(e);
            }

            _pettingLastPos = localPos;
            e.Handled = true;
            return;
        }

        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            // F-03: If left button is no longer pressed but we're still dragging,
            // clean up via the unified end-drag path.
            if (_isDragging)
                EndDrag();
            return;
        }

        // F-02: Convert screen pixels to DIP before assigning to Left/Top.
        var screenPosPx = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice
                        ?? Matrix.Identity;
        var screenPosDip = transform.Transform(screenPosPx);

        Left = screenPosDip.X - _dragOffset.X;
        Top = screenPosDip.Y - _dragOffset.Y;
        e.Handled = true;
    }

    private void PetCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPettingCandidate)
        {
            EndPetting();
            e.Handled = true;
        }
        else if (_isDragging)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    /// <summary>
    /// F-03: Unified drag cleanup entry. Called on mouse up, capture loss,
    /// and detected stale drag state. Releases capture and resets flag.
    /// F-02: After drag ends, clamp the window to a visible work area.
    /// P0-004: Notify App to mark state dirty for debounced save.
    /// P1-001: Return to idle action after drag ends.
    /// </summary>
    private void EndDrag()
    {
        _isDragging = false;
        if (PetCanvas.IsMouseCaptured)
            PetCanvas.ReleaseMouseCapture();

        // P1-003: Drag interrupts grievance expression (R-02).
        // Clear pending, reset grievance to neutral, stop expression.
        if (_isExpressingGrievance)
        {
            _isExpressingGrievance = false;
            var grievanceExpr = TestGrievanceExpression;
            var emotionAcc = TestEmotionAccumulator;
            if (grievanceExpr is null && System.Windows.Application.Current is App appExpr)
            {
                grievanceExpr = appExpr.GrievanceExpression;
                emotionAcc = appExpr.EmotionAccumulator;
            }
            grievanceExpr?.Clear();
            emotionAcc?.ResetGrievanceAfterExpression();
            if (System.Windows.Application.Current is App appDirty)
                appDirty.MarkStateDirty();
        }

        // F-02: Constrain the pet to a visible work area after dragging.
        ClampWindowToWorkArea();

        // P1-003: Record corner drag if the pet was released in a corner zone.
        var cornerTrack = TestCornerTracking;
        if (cornerTrack is null && System.Windows.Application.Current is App app)
            cornerTrack = app.CornerTracking;
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
        if (System.Windows.Application.Current is App app2)
            app2.MarkStateDirty();

        // P1-001: Return to idle after drag ends.
        _animPlayer.PlayAction("idle");
        UpdateFrame();

        // P1-002: Clear sampler after drag ends so resume doesn't consume
        // stale samples from before the drag.
        _mouseSampler.Clear();
    }

    /// <summary>
    /// P1-003: End petting candidate state. If accumulated time >= 1s and
    /// cooldown has elapsed, trigger a pet increment (+5 affinity).
    /// </summary>
    private void EndPetting()
    {
        _isPettingCandidate = false;
        if (PetCanvas.IsMouseCaptured)
            PetCanvas.ReleaseMouseCapture();

        var nowMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();

        // Check if petting threshold was met and cooldown has elapsed.
        if (_pettingAccumulatedMs >= PettingRequiredMs && nowMs >= _pettingCooldownUntilMs)
        {
            if (System.Windows.Application.Current is App app
                && app.EmotionAccumulator is not null)
            {
                app.EmotionAccumulator.AddAffinity(5);
                _pettingCooldownUntilMs = nowMs + PettingCooldownMs;
                app.MarkStateDirty();
            }
        }

        _mouseSampler.Clear();
    }

    /// <summary>
    /// P1-003: Convert petting candidate to drag when movement exceeds threshold.
    /// Sets up drag offset from current mouse position.
    /// </summary>
    private void ConvertPettingToDrag(System.Windows.Input.MouseEventArgs e)
    {
        _isPettingCandidate = false;

        var screenPosPx = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice
                        ?? Matrix.Identity;
        var screenPosDip = transform.Transform(screenPosPx);

        _isDragging = true;
        _dragOffset = new System.Windows.Point(screenPosDip.X - Left, screenPosDip.Y - Top);
        _animPlayer.PlayAction("drag");
        UpdateFrame();
    }

    /// <summary>
    /// P1-003: Check if a local point (relative to PetImage) is in the head zone.
    /// Head zone = upper 1/3 of the image area.
    /// Returns false if the image source is unavailable.
    /// </summary>
    internal bool IsInHeadZone(Point localPoint)
    {
        double imgHeight = PetImage.ActualHeight > 0 ? PetImage.ActualHeight : PetImage.Height;
        if (imgHeight <= 0) return false;
        return localPoint.Y <= imgHeight * HeadZoneRatio;
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
    /// F-03: Handle capture loss (e.g. menu or another window stole capture).
    /// Cancel drag state so the pet doesn't follow the mouse without a grab.
    /// </summary>
    private void PetCanvas_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging)
            EndDrag();
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
        var monitors = MonitorInterop.EnumerateMonitors();
        if (monitors.Count == 0)
            return;

        var (winDpiX, winDpiY) = MonitorInterop.GetWindowDpiScale(_hwnd);

        var (leftDip, topDip) = WindowPositionAdapter.RecoverPosition(
            monitors, Left, Top, Width, Height, winDpiX, winDpiY, null);

        Left = leftDip;
        Top = topDip;
        if (System.Windows.Application.Current is App app)
            app.MarkStateDirty();
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
        if (System.Windows.Application.Current is App app)
            app.MarkStateDirty();
    }

    /// <summary>
    /// P0-004: Pause active behavior (animation timer) when user pauses via tray.
    /// P1-001 F-02: Also pause the animation player's time so resuming
    /// continues from the same frame, not from wall-clock elapsed.
    /// The window and menu remain operational; only the animation loop stops.
    /// </summary>
    public void PauseAnimation()
    {
        _isAppPaused = true;
        _updateTimer.Stop();
        _animPlayer.Pause();

        // P1-002: Clear sampler on pause to avoid stale samples on resume.
        _mouseSampler.Clear();
    }

    /// <summary>
    /// P0-004: Resume active behavior after pause.
    /// P1-001 F-02: Resume the animation player's time so the animation
    /// continues from where it was paused, without accumulated wall-clock gap.
    /// </summary>
    public void ResumeAnimation()
    {
        _isAppPaused = false;
        _animPlayer.Resume();
        _updateTimer.Start();

        // P1-002: Clear sampler on resume to start fresh sampling.
        _mouseSampler.Clear();
    }

    /// <summary>
    /// P1-001: Public accessor for the current action (for testing/diagnostics).
    /// </summary>
    public string CurrentAction => _animPlayer.CurrentActionId;

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
    /// P1-002: Public accessor for the mouse sampler (for testing/diagnostics).
    /// The sampler is fed at ~30 Hz during normal operation and cleared on
    /// drag, menu, pause, and capture loss.
    /// </summary>
    public MouseSampler MouseSampler => _mouseSampler;

    /// <summary>
    /// P1-002: Public accessor for the hover detector (for testing/diagnostics).
    /// </summary>
    public HoverDetector HoverDetector => _hoverDetector;

    /// <summary>
    /// P1-002: Public accessor for the swipe detector (for testing/diagnostics).
    /// </summary>
    public SwipeDetector SwipeDetector => _swipeDetector;

    /// <summary>
    /// P1-002: Public accessor for the drag state (for testing/diagnostics).
    /// True while the pet is being dragged; sampling and detection are skipped.
    /// </summary>
    public bool IsDragging => _isDragging;

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
    /// True while the native context menu is open; sampling and detection are skipped.
    /// </summary>
    public bool IsContextMenuOpen => _isContextMenuOpen;

    /// <summary>
    /// P1-002: Public accessor for the last DPI scale applied to the sampler
    /// and swipe detector (for testing/diagnostics).
    /// </summary>
    public double LastDpiScale => _lastDpiScale;

    protected override void OnClosed(EventArgs e)
    {
        // F-03: Clean up drag state on window close.
        if (_isDragging)
            EndDrag();
        // P1-003: Clean up petting state on window close.
        if (_isPettingCandidate)
            EndPetting();
        _updateTimer.Stop();
        base.OnClosed(e);
    }
}
