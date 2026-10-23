using System.Reflection;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using DesktopPet.Core.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// F-04: App-layer integration tests that drive the actual OnUpdateTick
/// entry point via internal test-injection delegates (TestGetCursorPos,
/// TestGetElapsedMs, TestGetDpiScale). Production code leaves these null;
/// tests set them to controlled values so the real update logic runs with
/// deterministic mouse positions, a monotonic clock, and a configurable DPI.
///
/// These tests verify the wiring from OnUpdateTick through MouseSampler,
/// HoverDetector, SwipeDetector, and AnimationPlayer — not real mouse
/// behavior (which requires a live desktop session). Real mouse feel is
/// explicitly marked as unverified.
///
/// Uses STATestMethod because WPF UI components require an STA thread.
/// </summary>
[TestClass]
public class MouseInteractionIntegrationTests
{
    // Pet window defaults: Left=100, Top=100, Width=120, Height=120.
    // In test mode (no PresentationSource), GetPetRectPhysicalPx uses
    // Matrix.Identity, so the pet rect is (100,100)-(220,220) in physical px.
    private const double PetLeft = 100;
    private const double PetTop = 100;
    private const double PetSize = 120;
    private const double PetCenterX = PetLeft + PetSize / 2; // 160
    private const double PetCenterY = PetTop + PetSize / 2; // 160

    /// <summary>
    /// Create MainWindow. If construction fails (missing assets, no WPF context),
    /// Assert.Fail — per F-04 requirement: do not silently skip.
    /// </summary>
    private static MainWindow CreateMainWindowOrFail()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            Assert.Fail($"MainWindow construction failed — test environment requires character assets and a WPF STA context. Error: {ex.Message}");
            throw; // unreachable, satisfies compiler
        }
    }

    /// <summary>
    /// Invoke the private OnUpdateTick method via reflection.
    /// F-04: If the method does not exist, Assert.Fail — do not silently skip.
    /// </summary>
    private static void InvokeTick(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("OnUpdateTick",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null)
            Assert.Fail("OnUpdateTick method not found via reflection — test harness is broken.");
        method.Invoke(window, [null!, EventArgs.Empty]);
    }

    /// <summary>
    /// Set a private field via reflection. F-04: Fail if the field is missing.
    /// </summary>
    private static void SetField<T>(object obj, string name, T value)
    {
        var field = typeof(MainWindow).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail($"Field '{name}' not found via reflection — test harness is broken.");
        field!.SetValue(obj, value);
    }

    /// <summary>
    /// Get the private _animPlayer field via reflection. F-04: Fail if missing.
    /// Used to explicitly switch to idle so tests can observe a NEW trigger
    /// transition (idle→curious/dodge) rather than reading a stale action.
    /// </summary>
    private static AnimationPlayer GetAnimPlayer(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_animPlayer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is null)
            Assert.Fail("_animPlayer field not found via reflection — test harness is broken.");
        return (AnimationPlayer)field!.GetValue(window)!;
    }

    // ════════════════════════════════════════════════════════════════
    // 1. Hover → curious: dwell triggers once, leave+re-enter re-triggers
    // ════════════════════════════════════════════════════════════════

    [STATestMethod]
    public void F04_Hover_DwellTriggersCurious_OnceOnly_LeaveReenterRetriggers()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            // Controlled clock starting at 0 ms, advancing 33 ms per tick.
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            window.TestGetDpiScale = () => 1.0;

            // Tick 0: first sample (sampler needs 2 samples, so this just stores).
            InvokeTick(window);
            clockMs += 33;

            // Tick 1..N: cursor stays at pet center. Dwell threshold = 1200 ms.
            // At 33 ms/tick, need ~37 ticks to reach 1200 ms.
            // But the first tick already started dwell timing at clockMs=0.
            // Trigger should fire when (timeMs - dwellStartMs) >= 1200.
            var firstTriggerTick = -1;
            for (int i = 0; i < 50; i++)
            {
                InvokeTick(window);
                if (window.CurrentAction == "curious" && firstTriggerTick < 0)
                    firstTriggerTick = i;
                clockMs += 33;
            }

            // Must have triggered curious at least once.
            Assert.IsTrue(firstTriggerTick >= 0,
                $"Hover dwell must trigger 'curious' at least once. Action={window.CurrentAction}");
            Assert.IsTrue(window.HoverDetector.HasTriggered,
                "HoverDetector.HasTriggered must be true after dwell trigger");

            // ── Prove continued dwelling does NOT re-trigger ──
            // Switch back to idle explicitly, then keep dwelling. If the hover
            // detector re-fires, CurrentAction will switch from idle to curious.
            // If it doesn't (correct behavior), it stays idle.
            var animPlayer = GetAnimPlayer(window);
            animPlayer.PlayAction("idle");

            for (int i = 0; i < 10; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }
            Assert.AreEqual("idle", window.CurrentAction,
                "Continued dwelling after first trigger must NOT re-trigger curious (HasTriggered guard prevents re-fire)");
            Assert.IsTrue(window.HoverDetector.HasTriggered,
                "HasTriggered must remain true during continued dwell");

            // ── Leave for >500 ms, then re-enter and re-trigger ──
            // Move cursor far outside the pet rect.
            window.TestGetCursorPos = () => (500, 500);
            for (int i = 0; i < 20; i++) // ~660 ms outside
            {
                InvokeTick(window);
                clockMs += 33;
            }
            Assert.IsTrue(window.HoverDetector.HasTriggered,
                "HasTriggered must remain true during leave period (session not yet reset)");

            // Re-enter the pet rect. First tick back starts a new dwell session
            // (leave was >500ms so the old session is reset).
            // Clear sampler to avoid the re-entry jump (from 500,500 to pet
            // center) being interpreted as a fast swipe segment — the old
            // samples are from outside the pet and should not form a segment
            // with the re-entry position.
            window.MouseSampler.Clear();
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);

            // Explicitly set to idle so we can observe a NEW trigger transition.
            animPlayer.PlayAction("idle");

            // Tick once to start the new dwell session.
            InvokeTick(window);
            clockMs += 33;

            // Before reaching the dwell threshold, action must stay idle.
            // Dwell starts at the re-entry tick; need 1200 ms more.
            // At 33 ms/tick, that's ~37 ticks. Check the first 30 ticks (990 ms).
            for (int i = 0; i < 30; i++)
            {
                InvokeTick(window);
                Assert.AreEqual("idle", window.CurrentAction,
                    $"Action must stay 'idle' before dwell threshold is reached (tick {i}, elapsed ~{i * 33} ms)");
                clockMs += 33;
            }

            // After the threshold (1200+ ms), the action must switch to curious.
            var retriggered = false;
            for (int i = 0; i < 20; i++)
            {
                InvokeTick(window);
                if (window.CurrentAction == "curious")
                {
                    retriggered = true;
                    break;
                }
                clockMs += 33;
            }
            Assert.IsTrue(retriggered,
                "Hover must re-trigger 'curious' after leaving for >500 ms, re-entering, and dwelling past threshold. " +
                "The transition from idle→curious proves a new trigger event, not a stale action display.");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 2a. Near fast swipe → dodge
    // ════════════════════════════════════════════════════════════════

    [STATestMethod]
    public void F04_Swipe_NearFastSegment_TriggersDodge()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetDpiScale = () => 1.0;

            // Pet rect: (100,100)-(220,220). Edge at x=100 (left edge).
            // We need a fast segment whose PeakSegmentEnd is within 80 eq px
            // of the pet edge. So the fast segment should end near x≈100-180.
            //
            // Sequence: start far, then do a fast horizontal sweep that ends
            // near the pet's left edge.
            //
            // Tick 0: position at (400, 160) — far right, same Y as pet center.
            // Tick 1: position at (170, 160) — near pet left edge (70 px from edge at 100).
            //   Segment: 230 px in 33 ms → 230/0.033 ≈ 6970 px/s (>> 1200 threshold).
            //   PeakSegmentEnd = (170, 160). Distance to edge = |170-100| = 70 px < 80. ✓

            // Tick 0: first sample (stored, no speed yet).
            window.TestGetCursorPos = () => (400, 160);
            InvokeTick(window);
            clockMs += 33;

            // Tick 1: fast sweep to (170, 160) — 230 px in 33 ms ≈ 6970 px/s.
            // PeakSegmentEnd = (170, 160). Distance to left edge (100) = 70 px < 80. ✓
            window.TestGetCursorPos = () => (170, 160);
            InvokeTick(window);
            clockMs += 33;

            // At this point the sampler has 2 samples, speed is computed.
            // The swipe detector should see EquivalentRecentSegmentSpeedPxPerSec ≈ 6970
            // and PeakSegmentEnd at (170,160) with distance 70 px < 80.
            Assert.AreEqual("dodge", window.CurrentAction,
                $"Near fast swipe must trigger 'dodge'. Speed should be ~6970 eq px/s, distance ~70 px. Action={window.CurrentAction}");
            Assert.IsTrue(window.SwipeDetector.IsInCooldown,
                "SwipeDetector must be in cooldown after triggering dodge");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 2b. Far fast swipe → no dodge (speed high but distance too large)
    // ════════════════════════════════════════════════════════════════

    [STATestMethod]
    public void F04_Swipe_FarFastSegment_DoesNotTrigger()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetDpiScale = () => 1.0;

            // Fast sweep that ends far from the pet.
            // Pet rect: (100,100)-(220,220). Left edge at x=100.
            // Sweep from (600,160) to (400,160): 200 px in 33 ms ≈ 6061 px/s.
            // PeakSegmentEnd = (400, 160). Distance to left edge = 300 px >> 80. ✗
            window.TestGetCursorPos = () => (600, 160);
            InvokeTick(window);
            clockMs += 33;

            window.TestGetCursorPos = () => (400, 160);
            InvokeTick(window);
            clockMs += 33;

            Assert.AreNotEqual("dodge", window.CurrentAction,
                "Far fast swipe (PeakSegmentEnd 300 px from edge) must NOT trigger dodge");
            Assert.IsFalse(window.SwipeDetector.IsInCooldown,
                "SwipeDetector must not be in cooldown when distance check fails");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 2c. Cooldown: second near fast swipe within 3s does not re-trigger
    // ════════════════════════════════════════════════════════════════

    [STATestMethod]
    public void F04_Swipe_CooldownPreventsRetrigger()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetDpiScale = () => 1.0;

            // ── First swipe: trigger dodge ──
            window.TestGetCursorPos = () => (400, 160);
            InvokeTick(window);
            clockMs += 33;

            window.TestGetCursorPos = () => (170, 160);
            InvokeTick(window);
            clockMs += 33;

            Assert.AreEqual("dodge", window.CurrentAction,
                "First near fast swipe must trigger dodge");
            Assert.IsTrue(window.SwipeDetector.IsInCooldown,
                "Must be in cooldown after first trigger");

            // ── Switch back to idle so a new trigger is observable ──
            // If the detector re-fires during cooldown, CurrentAction will
            // switch from idle to dodge. If it doesn't (correct behavior),
            // it stays idle — proving no new trigger event occurred.
            var animPlayer = GetAnimPlayer(window);
            animPlayer.PlayAction("idle");
            Assert.AreEqual("idle", window.CurrentAction,
                "Must be idle before cooldown re-trigger attempt");

            // ── Second swipe attempt within cooldown (only ~66 ms later) ──
            // Move away first so we can build a new fast segment.
            window.TestGetCursorPos = () => (400, 160);
            InvokeTick(window);
            clockMs += 33;

            window.TestGetCursorPos = () => (170, 160);
            InvokeTick(window);
            clockMs += 33;

            // Still in cooldown — must NOT re-trigger. The transition from
            // idle→dodge would prove a new trigger; staying idle proves none.
            Assert.AreEqual("idle", window.CurrentAction,
                "Second swipe within cooldown must NOT re-trigger dodge — " +
                "action stays idle (no new trigger event), not stale dodge");
            Assert.IsTrue(window.SwipeDetector.IsInCooldown,
                "Must still be in cooldown — second swipe within 3s must not create a new trigger");

            // ── After cooldown expires (3000 ms), a new near fast swipe can trigger ──
            // Advance clock past cooldown. Need to clear old samples first by
            // having a gap > MaxSampleGapMs (200 ms).
            clockMs += 3100; // Total elapsed since first trigger > 3000 ms.

            // Switch to idle again so the new trigger is observable.
            animPlayer.PlayAction("idle");
            Assert.AreEqual("idle", window.CurrentAction,
                "Must be idle before post-cooldown re-trigger attempt");

            // Start fresh: first sample far away.
            window.TestGetCursorPos = () => (400, 160);
            InvokeTick(window);
            clockMs += 33;

            // Fast sweep near pet.
            window.TestGetCursorPos = () => (170, 160);
            InvokeTick(window);
            clockMs += 33;

            // Cooldown should have expired and a new trigger should fire.
            // The transition from idle→dodge proves a NEW trigger event,
            // not a stale action display.
            Assert.AreEqual("dodge", window.CurrentAction,
                "After cooldown expires, a new near fast swipe must trigger dodge again — " +
                "idle→dodge transition proves a new trigger event");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 3. DPI 1.0 → 1.5: sampler and detector update, old samples cleared
    // ════════════════════════════════════════════════════════════════

    [STATestMethod]
    public void F04_DpiChange_UpdatesSamplerAndDetector_ClearsOldSamples()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetDpiScale = () => 1.0;

            // ── Build up samples at DPI 1.0 ──
            window.TestGetCursorPos = () => (150, 150);
            InvokeTick(window);
            clockMs += 33;

            window.TestGetCursorPos = () => (155, 155);
            InvokeTick(window);
            clockMs += 33;

            window.TestGetCursorPos = () => (160, 160);
            InvokeTick(window);
            clockMs += 33;

            // Verify we have samples.
            Assert.IsTrue(window.MouseSampler.WindowCount >= 2,
                $"Must have built up samples at DPI 1.0. WindowCount={window.MouseSampler.WindowCount}");
            Assert.AreEqual(1.0, window.LastDpiScale, 0.001,
                "LastDpiScale must be 1.0 before DPI change");
            Assert.AreEqual(1.0, window.MouseSampler.Config.DpiScale, 0.001,
                "Sampler DPI must be 1.0 before change");
            Assert.AreEqual(1.0, window.SwipeDetector.Config.DpiScale, 0.001,
                "SwipeDetector DPI must be 1.0 before change");

            // ── Change DPI to 1.5 ──
            window.TestGetDpiScale = () => 1.5;

            // Next tick should detect the DPI change, update configs, and clear samples.
            window.TestGetCursorPos = () => (165, 165);
            InvokeTick(window);
            clockMs += 33;

            Assert.AreEqual(1.5, window.LastDpiScale, 0.001,
                "LastDpiScale must update to 1.5 after DPI change");
            Assert.AreEqual(1.5, window.MouseSampler.Config.DpiScale, 0.001,
                "Sampler Config.DpiScale must update to 1.5");
            Assert.AreEqual(1.5, window.SwipeDetector.Config.DpiScale, 0.001,
                "SwipeDetector Config.DpiScale must update to 1.5");

            // Old samples must be cleared. After the DPI-change tick, the sampler
            // was cleared and then received one new sample (which alone produces
            // Invalid, so WindowCount should be 1 — the single new sample).
            Assert.IsTrue(window.MouseSampler.WindowCount <= 1,
                $"Old samples must be cleared on DPI change. WindowCount={window.MouseSampler.WindowCount}");
        }
        finally
        {
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════
    // 4. Stateful interrupt: build state, interrupt, verify cleanup, recover
    // ════════════════════════════════════════════════════════════════

    [STATestMethod]
    public void F04_Interrupt_AfterStateBuilt_DragClearsAndRecovers()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetDpiScale = () => 1.0;

            // ── Phase 1: Build non-empty sampler, in-progress hover, and cooldown ──

            // Build sampler with several samples (hovering near pet center).
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            for (int i = 0; i < 10; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            // Verify sampler has samples.
            Assert.IsTrue(window.MouseSampler.WindowCount >= 2,
                $"Sampler must have samples before interrupt. WindowCount={window.MouseSampler.WindowCount}");

            // Verify hover is in progress (IsHovering true, may or may not have triggered yet).
            Assert.IsTrue(window.HoverDetector.IsHovering,
                "HoverDetector must be in hovering state after dwelling on pet");

            // Trigger a swipe to establish cooldown state.
            // Coverage note: The test calls MouseSampler.Clear() here to
            // prepare a clean sampler window for the fast segment, so the
            // swipe's PeakSegmentEnd is not contaminated by old hover
            // samples. This is test setup, not a claim that the production
            // drag entry point executes Clear at this point.
            window.MouseSampler.Clear();

            // First sample far away (starts fresh window).
            window.TestGetCursorPos = () => (400, 160);
            InvokeTick(window);
            clockMs += 33;

            // Fast sweep near pet: 230 px in 33 ms ≈ 6970 px/s.
            // PeakSegmentEnd = (170, 160), distance to edge = 70 px < 80.
            window.TestGetCursorPos = () => (170, 160);
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.SwipeDetector.IsInCooldown,
                "SwipeDetector must be in cooldown after triggering dodge (state built)");

            // ── Phase 2: Interrupt with drag ──
            // Coverage note: The test calls MouseSampler.Clear() to simulate
            // the pre-cleaned state that the production drag entry point
            // (PetCanvas_MouseLeftButtonDown) would produce. This proves
            // state disable/recovery under that pre-cleaned condition, but
            // does NOT automatically prove the production entry point
            // executes Clear. The production Clear call in
            // PetCanvas_MouseLeftButtonDown is verified by static inspection
            // of MainWindow.xaml.cs, not by this test.
            SetField(window, "_isDragging", true);
            window.MouseSampler.Clear();
            Assert.IsTrue(window.IsDragging, "Must be in drag state");

            // Tick during drag — should skip sampling and reset detectors.
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            InvokeTick(window);
            clockMs += 33;

            Assert.AreEqual(0, window.MouseSampler.WindowCount,
                "Sampler must be empty during drag (cleared by drag start, no new samples)");
            Assert.IsFalse(window.HoverDetector.IsHovering,
                "HoverDetector must be reset during drag");
            Assert.IsFalse(window.SwipeDetector.IsInCooldown,
                "SwipeDetector cooldown must be reset during drag");

            // ── Phase 3: End drag and verify recovery ──
            SetField(window, "_isDragging", false);

            // After drag ends, fresh sampling should work and hover can re-trigger.
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.MouseSampler.WindowCount >= 1,
                "Sampler must accept new samples after drag ends");

            // Dwell again to re-trigger curious.
            var retriggered = false;
            for (int i = 0; i < 50; i++)
            {
                InvokeTick(window);
                if (window.CurrentAction == "curious")
                    retriggered = true;
                clockMs += 33;
            }
            Assert.IsTrue(retriggered,
                "Hover must re-trigger after drag interrupt ends and fresh dwelling occurs");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F04_Interrupt_AfterStateBuilt_MenuClearsAndRecovers()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetDpiScale = () => 1.0;

            // ── Phase 1: Build non-empty sampler and in-progress hover ──
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            for (int i = 0; i < 10; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            Assert.IsTrue(window.MouseSampler.WindowCount >= 2,
                "Sampler must have samples before menu interrupt");
            Assert.IsTrue(window.HoverDetector.IsHovering,
                "Hover must be in progress before menu interrupt");

            // ── Phase 2: Simulate menu open ──
            // Coverage note: The test calls MouseSampler.Clear() to simulate
            // the pre-cleaned state that the production menu entry point
            // (ShowNativeContextMenu) would produce. This proves state
            // disable/recovery under that pre-cleaned condition, but does
            // NOT automatically prove the production entry point executes
            // Clear. The production Clear calls in ShowNativeContextMenu
            // (before menu display and after menu close) are verified by
            // static inspection of MainWindow.xaml.cs, not by this test.
            SetField(window, "_isContextMenuOpen", true);
            Assert.IsTrue(window.IsContextMenuOpen, "Must be in menu state");

            window.MouseSampler.Clear();

            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            InvokeTick(window);
            clockMs += 33;

            Assert.AreEqual(0, window.MouseSampler.WindowCount,
                "Sampler must be empty during menu");
            Assert.IsFalse(window.HoverDetector.IsHovering,
                "HoverDetector must be reset during menu");

            // ── Phase 3: Menu closes, verify recovery ──
            SetField(window, "_isContextMenuOpen", false);

            // Fresh sampling after menu close.
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.MouseSampler.WindowCount >= 1,
                "Sampler must accept new samples after menu closes");

            // Dwell to re-trigger curious.
            var retriggered = false;
            for (int i = 0; i < 50; i++)
            {
                InvokeTick(window);
                if (window.CurrentAction == "curious")
                    retriggered = true;
                clockMs += 33;
            }
            Assert.IsTrue(retriggered,
                "Hover must re-trigger after menu interrupt ends");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void F04_Interrupt_AfterStateBuilt_PauseClearsAndRecovers()
    {
        var window = CreateMainWindowOrFail();
        try
        {
            var clockMs = 0.0;
            window.TestGetElapsedMs = () => clockMs;
            window.TestGetDpiScale = () => 1.0;

            // ── Phase 1: Build non-empty sampler, in-progress hover, and cooldown ──
            // First build hover state.
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            for (int i = 0; i < 10; i++)
            {
                InvokeTick(window);
                clockMs += 33;
            }

            Assert.IsTrue(window.MouseSampler.WindowCount >= 2,
                "Sampler must have samples before pause");
            Assert.IsTrue(window.HoverDetector.IsHovering,
                "Hover must be in progress before pause");

            // Trigger swipe cooldown.
            // Coverage note: The test calls MouseSampler.Clear() here to
            // prepare a clean sampler window for the fast segment, so the
            // swipe's PeakSegmentEnd is not contaminated by old hover
            // samples. This is test setup, not a claim that the production
            // pause entry point executes Clear. The production pause path
            // (PauseAnimation → OnUpdateTick with _isAppPaused=true) skips
            // sampling and resets detectors via the else branch; the
            // sampler Clear in PauseAnimation itself is verified by static
            // inspection of MainWindow.xaml.cs.
            window.MouseSampler.Clear();

            // First sample far away (starts fresh window).
            window.TestGetCursorPos = () => (400, 160);
            InvokeTick(window);
            clockMs += 33;

            // Fast sweep near pet.
            window.TestGetCursorPos = () => (170, 160);
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.SwipeDetector.IsInCooldown,
                "Must be in cooldown before pause (state built)");

            // ── Phase 2: Pause ──
            window.PauseAnimation();
            Assert.IsTrue(window.IsAppPaused, "Must be paused");

            // Tick during pause — should skip sampling and reset detectors.
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            InvokeTick(window);
            clockMs += 33;

            Assert.AreEqual(0, window.MouseSampler.WindowCount,
                "Sampler must be empty during pause");
            Assert.IsFalse(window.HoverDetector.IsHovering,
                "HoverDetector must be reset during pause");
            Assert.IsFalse(window.SwipeDetector.IsInCooldown,
                "SwipeDetector cooldown must be reset during pause");

            // ── Phase 3: Resume and verify recovery ──
            window.ResumeAnimation();
            Assert.IsFalse(window.IsAppPaused, "Must not be paused after resume");

            // Fresh sampling after resume.
            window.TestGetCursorPos = () => ((int)PetCenterX, (int)PetCenterY);
            InvokeTick(window);
            clockMs += 33;

            Assert.IsTrue(window.MouseSampler.WindowCount >= 1,
                "Sampler must accept new samples after resume");

            // Dwell to re-trigger curious.
            var retriggered = false;
            for (int i = 0; i < 50; i++)
            {
                InvokeTick(window);
                if (window.CurrentAction == "curious")
                    retriggered = true;
                clockMs += 33;
            }
            Assert.IsTrue(retriggered,
                "Hover must re-trigger after pause interrupt ends");
        }
        finally
        {
            window.Close();
        }
    }
}
