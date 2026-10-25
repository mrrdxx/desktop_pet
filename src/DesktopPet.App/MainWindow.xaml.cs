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
    private readonly IdleBlinkScheduler? _idleBlink;
    private readonly CuriosityReaction _curiosity = new();
    private readonly PetCharacterDef _character;
    private readonly Dictionary<string, BitmapImage> _imageCache = [];
    private readonly string _assetDir;
    private string _lastDisplayedFrame = string.Empty;
    private readonly HashSet<string> _missingActionWarnings = [];

    private bool _isDragging;
    private System.Windows.Point _dragOffset;
    private Point _pointerPressScreenDip;

    // P1-003: Petting detection state (R-02).
    // Head zone = upper 1/3 of the pet image (resource-driven zones deferred).
    // Petting: head press + small movement accumulated >= 1s → +5 affinity, 2s cooldown.
    // Every visible part can drag. Head micro-moves remain petting only within
    // the press-origin slop; conversion retains capture and the initial anchor.
    private bool _isPettingCandidate;
    private double _pettingStartMs;
    private double _pettingAccumulatedMs;
    private double _pettingCooldownUntilMs;
    private System.Windows.Point _pettingLastPos;
    private const double PettingRequiredMs = 1000.0;
    private const double PettingCooldownMs = 2000.0;
    private const double PettingMaxMovePerFramePx = 8.0; // small movement threshold in DIP
    private const double HeadDragSlopDip = 8.0;
    private const double PettingMinMovePx = 0.5; // F-03: below this is zero-displacement (stationary)
    private const double PettingMaxInterEventMs = 500.0; // F-03: gap larger than this is a long pause, not continuous petting
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

        if (_animPlayer.CurrentActionId != "curious")
            _curiosity.Cancel();

        // Position alone drives hover. No speed history, proximity fright,
        // cooldown, or automatic dodge action remains (D-009).
        if (!_isDragging && !_isPettingCandidate && !_isContextMenuOpen && !_isAppPaused
            && !_isExpressingGrievance && TryGetMousePosition(out var mouseX, out var mouseY))
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
            _hoverDetector.Reset();
            if (!_isAppPaused) _curiosity.Cancel();
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
                        if (System.Windows.Application.Current is App app3)
                            app3.MarkStateDirty();

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
                        // Static aggrieved bear expression; turning-away motion is deferred.
                        // Expression will last 10 seconds, then resets grievance.
                        _isExpressingGrievance = true;
                        _grievanceExpressionStartMs = nowMs;
                        _animPlayer.PlayAction("grievance");
                    }
                }
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
        var blinkFrame = _idleBlink?.Update(TestGetElapsedMs?.Invoke() ?? GetElapsedMs(),
            _animPlayer.CurrentActionId == "idle" && !_isDragging && !_isPettingCandidate
            && !_isContextMenuOpen && !_isExpressingGrievance);
        if (blinkFrame is not null) frame = blinkFrame;

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
        if (_isDragging || _isPettingCandidate || !PetImageGeometry.Contains(PetImage, imagePoint))
            return false;

        bool isHeadPress = IsInHeadZone(imagePoint);
        _pointerPressScreenDip = screenDip;
        _dragOffset = new Point(screenDip.X - Left, screenDip.Y - Top);

        // F-04: Drag/petting start immediately interrupts grievance expression.
        // Per R-02, expression is interrupted when the user starts dragging;
        // this must happen at MouseLeftButtonDown, not at EndDrag.
        if (_isExpressingGrievance)
        {
            InterruptGrievanceExpression();
        }

        if (isHeadPress)
        {
            // Start petting candidate — may convert to drag if movement exceeds threshold.
            _isPettingCandidate = true;
            _pettingStartMs = TestGetElapsedMs?.Invoke() ?? GetElapsedMs();
            _pettingAccumulatedMs = 0;
            _pettingLastPos = imagePoint;
            _hoverDetector.Reset();
            return true;
        }

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
        _isDragging = true;
        _hoverDetector.Reset();
        _animPlayer.PlayAction("drag");
        UpdateFrame();
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
                ProcessPettingMove(imagePoint, nowMs);
                return;
            }
        }
        if (_isDragging)
        {
            // Preserve the anchor captured on mouse-down, including the first
            // conversion movement; do not require a second move to catch up.
            Left = screenDip.X - _dragOffset.X;
            Top = screenDip.Y - _dragOffset.Y;
        }
    }

    private void PetCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndCapturedPointerInteraction();
        e.Handled = true;
    }

    internal void EndCapturedPointerInteraction()
    {
        if (_isPettingCandidate)
        {
            EndPetting();
        }
        else if (_isDragging)
        {
            EndDrag();
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

        // F-04: Return to idle action after expression is interrupted.
        _animPlayer.PlayAction("idle");
        UpdateFrame();
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
        // F-04: Primary interrupt is at MouseLeftButtonDown; this is a safety net.
        InterruptGrievanceExpression();

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

        // Start a fresh hover dwell after dragging.
        _hoverDetector.Reset();
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
            // F-05: Support test injection via TestEmotionAccumulator.
            EmotionDecayAccumulator? emotionAcc = TestEmotionAccumulator;
            if (emotionAcc is null && System.Windows.Application.Current is App app)
                emotionAcc = app.EmotionAccumulator;

            if (emotionAcc is not null)
            {
                emotionAcc.AddAffinity(5);
                _pettingCooldownUntilMs = nowMs + PettingCooldownMs;
                if (System.Windows.Application.Current is App appDirty)
                    appDirty.MarkStateDirty();
            }
        }

        _hoverDetector.Reset();
    }

    /// <summary>
    /// P1-003: Convert petting candidate to drag when movement exceeds threshold.
    /// Retains the original mouse capture and down-position anchor.
    /// </summary>
    private void ConvertPettingToDrag()
    {
        _isPettingCandidate = false;
        _pettingAccumulatedMs = 0;
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
    /// Micro-stroke counting shared by the production gesture pipeline and tests.
    /// ProcessCapturedPointerMove first handles cumulative drag intent and capture
    /// continuity; this helper alone does not prove a head-to-drag transition.
    /// Returns false if the petting candidate was ended.
    /// </summary>
    internal bool ProcessPettingMove(Point localPos, double nowMs)
    {
        if (!_isPettingCandidate)
            return false;

        // F-03: If the cursor has left the head zone, end petting candidate.
        if (!IsInHeadZone(localPos))
        {
            EndPetting();
            return false;
        }

        var dx = localPos.X - _pettingLastPos.X;
        var dy = localPos.Y - _pettingLastPos.Y;
        var moveDist = Math.Sqrt(dx * dx + dy * dy);

        if (moveDist > PettingMaxMovePerFramePx)
        {
            // Large movement — would convert to drag in real handler.
            // In test mode we just end petting candidate without drag setup.
            _isPettingCandidate = false;
            if (PetCanvas.IsMouseCaptured)
                PetCanvas.ReleaseMouseCapture();
            _hoverDetector.Reset();
            return false;
        }

        // F-03: Only accumulate for effective small movements.
        if (moveDist >= PettingMinMovePx)
        {
            var interEventMs = nowMs - _pettingStartMs;

            if (interEventMs <= PettingMaxInterEventMs)
            {
                _pettingAccumulatedMs += interEventMs;
            }
            _pettingStartMs = nowMs;
        }

        _pettingLastPos = localPos;
        return true;
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
        _curiosity.Pause(_pauseStartMs);

        // Discard hover dwell at the pause boundary.
        _hoverDetector.Reset();
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

                // Compensate petting cooldown so it doesn't expire during pause.
                _pettingCooldownUntilMs += pauseDurationMs;

                // Compensate emotion decay: advance the accumulator's reference
                // time forward by the pause duration so that the next AdvanceTo
                // call doesn't count the paused interval as decay time.
                var emotionAcc = TestEmotionAccumulator;
                if (emotionAcc is null && System.Windows.Application.Current is App app)
                    emotionAcc = app.EmotionAccumulator;
                if (emotionAcc is not null)
                {
                    // Move the decay reference forward by the pause duration.
                    // This is NOT advancing decay — it's skipping the paused interval.
                    emotionAcc.SkipInterval(TimeSpan.FromMilliseconds(pauseDurationMs));
                }

                // Compensate corner tracker's time-based windows.
                var cornerTrack = TestCornerTracking;
                if (cornerTrack is null && System.Windows.Application.Current is App appCT)
                    cornerTrack = appCT.CornerTracking;
                cornerTrack?.SkipInterval(pauseDurationMs);
            }

            _hasPauseStartMs = false;
        }

        _isAppPaused = false;
        _animPlayer.Resume();
        _updateTimer.Start();

        // Resume starts a fresh hover dwell.
        _hoverDetector.Reset();
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
    /// P1-002: Public accessor for the hover detector (for testing/diagnostics).
    /// </summary>
    public HoverDetector HoverDetector => _hoverDetector;

    /// <summary>
    /// P1-002: Public accessor for the drag state (for testing/diagnostics).
    /// True while the pet is being dragged; hover detection is skipped.
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
    /// True while the native context menu is open; hover detection is skipped.
    /// </summary>
    public bool IsContextMenuOpen => _isContextMenuOpen;

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
