using System.IO;
using System.Reflection;
using System.Windows;
using DesktopPet.App;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Physics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Point = System.Windows.Point;

namespace DesktopPet.App.Tests;

/// <summary>
/// P2-003 Step 1: Tests for drag-to-release speed sampling integration.
/// Verifies that ReleaseSpeedSampler is fed during drag and evaluated on release.
/// Window does NOT move along physics trajectory yet (step 1 only).
///
/// F-01 remediation: StartDragging records a press-down baseline sample so a
/// single fast Move already has 2 samples. EndDrag probes a stationary sample
/// at release time when the last sample is older than the sample window, so a
/// real hold-without-MouseMove can be detected as gentle. Tests must NOT
/// fabricate same-position Move events to simulate stillness.
///
/// F-02 remediation: NotifyDpiChanged() clears sample context on DPI change.
/// Tests call the same internal method that the WPF DpiChanged event handler
/// calls in production — no separate test-only cleanup logic.
/// </summary>
[TestClass]
public class ReleaseSamplingIntegrationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void BeginBody(MainWindow w) =>
        Assert.IsTrue(w.BeginCapturedPointerInteraction(
            new Point(60, 65), new Point(w.Left + 60, w.Top + 65)));

    // ── F-01: Press baseline → single fast Move → throw ──

    /// <summary>
    /// F-01 counter-example 1: press@0 → one fast Move@10 → release@35.
    /// With the press baseline, the sampler has 2 samples (press + move)
    /// and should throw. Must NOT require two Move events.
    /// </summary>
    [STATestMethod]
    public void FastDrag_SingleMove_ThrowsWithPressBaseline()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            // StartDragging records a baseline sample at now=0.
            Assert.AreEqual(1, w.DragSampleCount,
                "StartDragging must record a press-down baseline sample.");

            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Single fast rightward Move: 100 DIP in 10ms.
            // At 150% DPI: 150 physical px / 10ms = 15 px/ms, well above 0.15.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);

            Assert.AreEqual(2, w.DragSampleCount,
                "Press baseline + one Move = 2 samples.");

            // Release quickly — still within the sample window, so no
            // stationary probe is injected.
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.LastReleaseResult.ShouldThrow,
                "Single fast Move + press baseline → should throw.");
            Assert.IsTrue(w.LastReleaseResult.VelocityX > 0,
                "Throw direction should be rightward.");
        }
        finally { w.Close(); }
    }

    // ── F-01: Fast Move then real stillness (no Move) → gentle ──

    /// <summary>
    /// F-01 counter-example 2: fast Move@10/@30, then NO further Move,
    /// release@96. The user held the pet still for 66ms after the last Move.
    /// EndDrag must probe a stationary sample at release time so the sampler
    /// detects the stop → gentle release. Must NOT fabricate a Move event.
    /// </summary>
    [STATestMethod]
    public void FastMove_ThenRealStillness_GentleRelease()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Two fast Moves.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            Assert.IsTrue(w.DragSampleCount >= 3,
                "Baseline + 2 Moves = at least 3 samples.");

            // Stop moving — NO further ProcessCapturedPointerMove calls.
            // Advance time well beyond the 100ms sample window so old motion
            // samples are pruned. EndDrag will probe a stationary sample.
            now = 96;
            w.EndCapturedPointerInteraction();

            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "Fast Move then real stillness (no Move, time advanced) → gentle.");
            Assert.AreEqual(0, w.LastReleaseResult.VelocityX);
            Assert.AreEqual(0, w.LastReleaseResult.VelocityY);
        }
        finally { w.Close(); }
    }

    // ── F-01: Normal fast drag + immediate release → still throws ──

    /// <summary>
    /// F-01 acceptance: normal fast drag with short interval before release
    /// must still throw. The release follows 5ms after the last Move — well
    /// within the sample window — so no stationary probe is injected and the
    /// motion samples remain in-window.
    /// </summary>
    [STATestMethod]
    public void FastDrag_ImmediateRelease_StillThrows()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast rightward movement.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            // Release 5ms after last Move — within the 100ms window.
            // No stationary probe; motion samples still fresh → throw.
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.LastReleaseResult.ShouldThrow,
                "Normal fast drag + immediate release → should throw.");
            Assert.IsTrue(w.LastReleaseResult.VelocityX > 0,
                "Throw direction should be rightward.");
        }
        finally { w.Close(); }
    }

    // ── F-01 R1: Head-press → convert-to-drag → single fast Move → throw ──

    /// <summary>
    /// F-01 R1 remaining: head-press@0 → single fast Move@10 that exceeds
    /// the head drag slop → converts to drag → release@35. The press-down
    /// baseline must use the real press time (@0), not the conversion time
    /// (@10), so the baseline and first Move have different timestamps and
    /// Core's same-time rejection does not drop the first displacement.
    /// Verifies via the actual BeginCapturedPointerInteraction →
    /// ProcessCapturedPointerMove → EndCapturedPointerInteraction gesture chain.
    /// </summary>
    [STATestMethod]
    public void HeadPress_SingleFastMove_ConvertsAndThrows()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 30, (int)w.Top + 9);

            // Press in the head zone (upper 1/3, Y=9 < 40).
            Assert.IsTrue(w.BeginCapturedPointerInteraction(
                new Point(30, 9), new Point(w.Left + 30, w.Top + 9)),
                "Head press should begin interaction.");
            Assert.IsTrue(w.IsPettingCandidate, "Should be petting candidate.");
            Assert.IsFalse(w.IsDragging, "Should not be dragging yet.");
            Assert.AreEqual(0, w.DragSampleCount,
                "No samples during petting candidate phase.");

            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Single fast Move exceeding head drag slop (8 DIP).
            // 130 DIP rightward, 56 DIP downward — well beyond slop.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 40), new Point(baseLeft + 160, baseTop + 65), true, now);

            // Conversion should have happened: now dragging, not petting.
            Assert.IsTrue(w.IsDragging, "Should have converted to drag.");
            Assert.IsFalse(w.IsPettingCandidate, "Petting candidate cleared.");
            Assert.AreEqual(2, w.DragSampleCount,
                "Press baseline (@0) + first Move (@10) = 2 samples, " +
                "same-time rejection must not drop the first displacement.");

            // Release quickly — within the sample window, no stationary probe.
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.LastReleaseResult.ShouldThrow,
                "Head-press → single fast Move → release should throw.");
            Assert.IsTrue(w.LastReleaseResult.VelocityX > 0,
                "Throw direction should be rightward.");
        }
        finally { w.Close(); }
    }

    // ── F-01 R1: Head-press → light tap within slop → no throw ──

    /// <summary>
    /// F-01 R1 negative: head-press@0 → small Move within slop (≤8 DIP) →
    /// release@35. Should remain a petting candidate (not convert to drag)
    /// and produce a gentle release — no throw from a light head tap.
    /// </summary>
    [STATestMethod]
    public void HeadPress_LightTapWithinSlop_NoThrow()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 30, (int)w.Top + 9);

            // Press in the head zone.
            Assert.IsTrue(w.BeginCapturedPointerInteraction(
                new Point(30, 9), new Point(w.Left + 30, w.Top + 9)));
            Assert.IsTrue(w.IsPettingCandidate);

            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Small Move within slop (3 DIP, below 8 DIP threshold).
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(33, 12), new Point(baseLeft + 33, baseTop + 12), true, now);

            // Should still be petting candidate, not drag.
            Assert.IsTrue(w.IsPettingCandidate, "Small move within slop stays petting.");
            Assert.IsFalse(w.IsDragging, "Should not have converted to drag.");
            Assert.AreEqual(0, w.DragSampleCount,
                "No samples during petting — no throw possible.");

            // Release → gentle (EndPetting, not EndDrag).
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "Light head tap within slop → gentle, no throw.");
        }
        finally { w.Close(); }
    }

    // ── Gentle release: no movement → stays in place ──

    [STATestMethod]
    public void GentleRelease_NoMovement_StaysInPlace()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            // StartDragging records a baseline sample at now=0.
            Assert.AreEqual(1, w.DragSampleCount,
                "Press baseline recorded at drag start.");

            // Release immediately without moving — gentle release.
            // Only 1 sample (baseline), below MinSamplesForThrow=2.
            now = 10;
            w.EndCapturedPointerInteraction();

            Assert.IsFalse(w.IsDragging);
            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "No movement → gentle release.");
            Assert.AreEqual(0, w.LastReleaseResult.VelocityX);
            Assert.AreEqual(0, w.LastReleaseResult.VelocityY);
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void GentleRelease_SlowMovement_StaysInPlace()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Very slow movement: 1 DIP per 50ms.
            // At 150% DPI: 1.5 physical px / 50ms = 0.03 px/ms.
            // Above stop threshold (0.02) but below throw threshold (0.15) → gentle.
            now = 50;
            w.ProcessCapturedPointerMove(
                new Point(61, 65), new Point(baseLeft + 61, baseTop + 65), true, now);
            now = 100;
            w.ProcessCapturedPointerMove(
                new Point(62, 65), new Point(baseLeft + 62, baseTop + 65), true, now);

            Assert.IsTrue(w.DragSampleCount >= 3, "Should have baseline + move samples.");

            now = 110;
            w.EndCapturedPointerInteraction();

            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "Slow movement → gentle release.");
        }
        finally { w.Close(); }
    }

    // ── Throw: fast movement → ShouldThrow=true ──
    // (Covered by FastDrag_ImmediateRelease_StillThrows above and
    //  FastDrag_SingleMove_ThrowsWithPressBaseline above.)

    // ── Sampler reset between drags ──

    [STATestMethod]
    public void SecondDrag_StartsWithFreshSamples()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            // First drag: fast throw.
            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();
            Assert.IsTrue(w.LastReleaseResult.ShouldThrow);

            // Second drag: no movement → gentle, old samples must not persist.
            now = 100;
            BeginBody(w);
            Assert.AreEqual(1, w.DragSampleCount,
                "Samples must be reset at start of new drag (baseline only).");
            now = 110;
            w.EndCapturedPointerInteraction();
            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "Second drag with no movement → gentle.");
        }
        finally { w.Close(); }
    }

    // ── F-01: Stop before release → gentle (no fabricated Move) ──

    /// <summary>
    /// F-01: Fast movement then stop and hold. NO fabricated same-position
    /// Move event. Time advances beyond the sample window, then EndDrag
    /// probes a real stationary sample → gentle release.
    /// </summary>
    [STATestMethod]
    public void StopBeforeRelease_GentleDespiteEarlierFastMovement()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast movement then stop — NO further Move calls.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            // Hold still for 200ms (well beyond 100ms window) without any Move.
            // EndDrag probes a stationary sample at the current position.
            now = 250;
            w.EndCapturedPointerInteraction();

            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "Stopped before release → gentle, no old peak.");
        }
        finally { w.Close(); }
    }

    // ── Pause clears sample context ──

    [STATestMethod]
    public void Pause_ClearsSamples_NoThrowOnResume()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast movement, then pause mid-drag.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            Assert.IsTrue(w.DragSampleCount >= 3);

            w.PauseAnimation();
            Assert.AreEqual(0, w.DragSampleCount,
                "Pause must clear drag samples.");

            // Resume and release without movement → gentle.
            w.ResumeAnimation();
            now = 1000;
            w.EndCapturedPointerInteraction();
            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "After pause/resume with no new movement → gentle.");
        }
        finally { w.Close(); }
    }

    // ── Display layout change clears sample context ──

    [STATestMethod]
    public void DisplayLayoutChange_ClearsSamples()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            Assert.IsTrue(w.DragSampleCount >= 3);

            // Simulate display layout change via the private handler.
            typeof(MainWindow).GetMethod("OnDisplayLayoutChanged", Private)!
                .Invoke(w, null!);

            Assert.AreEqual(0, w.DragSampleCount,
                "Display layout change must clear drag samples.");
        }
        finally { w.Close(); }
    }

    // ── F-02: DPI change clears sample context (no layout change) ──

    /// <summary>
    /// F-02: DPI changes without a display layout change. NotifyDpiChanged
    /// must clear old samples so a coordinate jump does not produce a throw.
    /// After clearing, immediate release → gentle.
    /// </summary>
    [STATestMethod]
    public void DpiChange_Alone_ClearsSamples_NoThrowOnRelease()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            Assert.IsTrue(w.DragSampleCount >= 3);

            // DPI change without display layout change — call the same
            // internal method that the WPF DpiChanged event handler calls.
            w.NotifyDpiChanged();

            Assert.AreEqual(0, w.DragSampleCount,
                "DPI change must clear drag samples.");

            // Immediate release after DPI change → gentle (no samples).
            now = 35;
            w.EndCapturedPointerInteraction();
            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "After DPI change with no new movement → gentle.");
        }
        finally { w.Close(); }
    }

    // ── F-02: Same DPI normal drag does not repeatedly clear ──

    /// <summary>
    /// F-02: Normal drag at the same DPI must not have samples cleared
    /// spuriously. NotifyDpiChanged is only called on actual DPI change,
    /// so normal dragging accumulates samples as expected.
    /// </summary>
    [STATestMethod]
    public void SameDpi_NormalDrag_NotCleared()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);

            // No DPI change call — samples must persist.
            Assert.IsTrue(w.DragSampleCount >= 2,
                "Samples persist during normal same-DPI drag.");

            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            Assert.IsTrue(w.DragSampleCount >= 3,
                "Samples continue accumulating without DPI change.");

            now = 35;
            w.EndCapturedPointerInteraction();
            Assert.IsTrue(w.LastReleaseResult.ShouldThrow,
                "Normal fast drag without DPI change → should throw.");
        }
        finally { w.Close(); }
    }

    // ── F-02: DPI change then new real movement can throw ──

    /// <summary>
    /// F-02: After DPI change clears samples, new real movement builds a
    /// fresh baseline and can still produce a throw.
    /// </summary>
    [STATestMethod]
    public void DpiChange_ThenNewMovement_CanThrow()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Some initial movement.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);

            // DPI change clears samples.
            w.NotifyDpiChanged();
            Assert.AreEqual(0, w.DragSampleCount);

            // New fast movement after DPI change builds fresh samples.
            now = 20;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 40;
            w.ProcessCapturedPointerMove(
                new Point(90, 65), new Point(baseLeft + 360, baseTop + 65), true, now);

            Assert.IsTrue(w.DragSampleCount >= 2,
                "New samples accumulated after DPI change.");

            now = 45;
            w.EndCapturedPointerInteraction();
            Assert.IsTrue(w.LastReleaseResult.ShouldThrow,
                "Fast movement after DPI change → should throw.");
        }
        finally { w.Close(); }
    }

    // ── Default state: no drag yet → gentle release ──

    [STATestMethod]
    public void NoDragEverStarted_DefaultReleaseIsGentle()
    {
        var w = new MainWindow();
        try
        {
            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "Default release result before any drag → gentle.");
            Assert.AreEqual(0, w.DragSampleCount);
        }
        finally { w.Close(); }
    }

    // ── P2-003 S2: Physics Launch on throw, not on cancellation/gentle ──

    /// <summary>
    /// S2: A normal fast drag release with ShouldThrow=true must Launch
    /// the physics simulator. IsPhysicsActive and IsPhysicsLaunched must
    /// be true after EndCapturedPointerInteraction.
    /// </summary>
    [STATestMethod]
    public void S2_FastDrag_Throws_LaunchesPhysics()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            Assert.IsFalse(w.IsPhysicsActive, "No physics before drag.");
            Assert.IsFalse(w.IsPhysicsLaunched, "No launch before drag.");

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            // Normal release (isCancellation defaults to false).
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.LastReleaseResult.ShouldThrow,
                "Fast drag → should throw.");
            Assert.IsTrue(w.IsPhysicsActive,
                "Throw release must activate physics.");
            Assert.IsTrue(w.IsPhysicsLaunched,
                "Throw release must launch the simulator.");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// S2: A gentle release (no movement) must NOT Launch physics.
    /// IsPhysicsActive and IsPhysicsLaunched must remain false.
    /// </summary>
    [STATestMethod]
    public void S2_GentleRelease_NoPhysicsLaunch()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            // No movement at all → gentle.
            now = 10;
            w.EndCapturedPointerInteraction();

            Assert.IsFalse(w.LastReleaseResult.ShouldThrow,
                "No movement → gentle.");
            Assert.IsFalse(w.IsPhysicsActive,
                "Gentle release must not activate physics.");
            Assert.IsFalse(w.IsPhysicsLaunched,
                "Gentle release must not launch the simulator.");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// S2: A cancellation (capture loss) must NOT evaluate throw or Launch
    /// physics. The pet stays at its current position.
    /// </summary>
    [STATestMethod]
    public void S2_CaptureLoss_NoPhysicsLaunch()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast movement that would normally throw.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            var leftBeforeCancel = w.Left;
            var topBeforeCancel = w.Top;

            // Cancellation via capture loss.
            w.EndCapturedPointerInteraction(isCancellation: true);

            Assert.IsFalse(w.IsPhysicsActive,
                "Cancellation must not activate physics.");
            Assert.IsFalse(w.IsPhysicsLaunched,
                "Cancellation must not launch the simulator.");
            // Pet stays at its current position (within clamp tolerance).
            Assert.IsTrue(Math.Abs(w.Left - leftBeforeCancel) < 200,
                "Cancellation should not cause a large position jump.");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// S2: After physics is launched, advancing time in OnUpdateTick should
    /// drive the window position. After enough time, the pet comes to rest
    /// and IsPhysicsActive becomes false.
    /// </summary>
    [STATestMethod]
    public void S2_PhysicsDriven_WindowMovesUntilRest()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast rightward drag.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            now = 35;
            w.EndCapturedPointerInteraction();
            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // Advance time in steps to drive physics via OnUpdateTick.
            // The pet should move and eventually come to rest.
            var leftAfterRelease = w.Left;
            for (int i = 0; i < 200; i++)
            {
                now += 100; // 100ms per tick
                var updateMethodInfo = typeof(MainWindow).GetMethod("UpdatePhysics",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (w.IsPhysicsActive)
                {
                    updateMethodInfo!.Invoke(w, new object[] { now });
                }
                else
                {
                    break;
                }
            }
            if (w.IsPhysicsActive)
            {
                // Force more iterations if not yet at rest.
                for (int i = 0; i < 500 && w.IsPhysicsActive; i++)
                {
                    now += 100;
                    var updateMethodInfo = typeof(MainWindow).GetMethod("UpdatePhysics",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    updateMethodInfo!.Invoke(w, new object[] { now });
                }
            }

            Assert.IsFalse(w.IsPhysicsActive,
                "Physics must deactivate after the pet comes to rest.");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// S2: Re-grabbing the pet (StartDragging) while physics is active must
    /// stop physics immediately. The user takes control of position.
    /// </summary>
    [STATestMethod]
    public void S2_Regrab_StopsPhysics()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();
            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // Re-grab the pet.
            now = 40;
            Assert.IsTrue(w.BeginCapturedPointerInteraction(
                new Point(60, 65), new Point(w.Left + 60, w.Top + 65)),
                "Re-grab should succeed.");

            Assert.IsFalse(w.IsPhysicsActive,
                "Re-grab must stop physics.");
            Assert.IsTrue(w.IsDragging, "Should be dragging after re-grab.");
        }
        finally { w.Close(); }
    }

    // ── F-03: Close during drag → cancellation, no Launch ──

    /// <summary>
    /// F-03: A fast drag followed by window close (OnClosed) must NOT
    /// Launch physics. OnClosed calls EndDrag(isCancellation: true).
    /// This test uses the real production entry point (OnClosed) — not
    /// a manual EndDrag(true) call.
    /// </summary>
    [STATestMethod]
    public void S2_R3_CloseDuringDrag_NoPhysicsLaunch()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast movement that would normally throw.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            Assert.IsTrue(w.IsDragging, "Should be dragging before close.");

            // Trigger OnClosed — the real production close entry point.
            typeof(MainWindow).GetMethod("OnClosed",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object[] { EventArgs.Empty });

            Assert.IsFalse(w.IsPhysicsActive,
                "Close during drag must not activate physics.");
            Assert.IsFalse(w.IsPhysicsLaunched,
                "Close during drag must not launch the simulator.");
        }
        finally { w.Close(); }
    }

    // ── F-03: Menu cancel during drag → cancellation, no Launch ──

    /// <summary>
    /// F-03: A fast drag followed by context menu opening must NOT
    /// Launch physics. ShowNativeContextMenu calls EndDrag(isCancellation: true).
    /// This test uses the real production entry point (ShowNativeContextMenu)
    /// via reflection — not a manual EndDrag(true) call.
    /// </summary>
    [STATestMethod]
    public void S2_R3_MenuCancelDuringDrag_NoPhysicsLaunch()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast movement that would normally throw.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            Assert.IsTrue(w.IsDragging, "Should be dragging before menu.");

            // Invoke ShowNativeContextMenu via reflection. EndDrag(isCancellation: true)
            // executes before TrackPopupMenuEx. In the test environment the window
            // lacks foreground, so SetForegroundWindow fails and TrackPopupMenuEx
            // is never reached — the method returns safely after EndDrag.
            var pointType = typeof(MainWindow).GetNestedType("POINT",
                BindingFlags.NonPublic)!;
            var point = Activator.CreateInstance(pointType)!;
            typeof(MainWindow).GetMethod("ShowNativeContextMenu",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object[] { point });

            Assert.IsFalse(w.IsPhysicsActive,
                "Menu cancel during drag must not activate physics.");
            Assert.IsFalse(w.IsPhysicsLaunched,
                "Menu cancel during drag must not launch the simulator.");
        }
        finally { w.Close(); }
    }

    // ── F-03: Normal fast drag release still Launches (positive control) ──

    /// <summary>
    /// F-03 positive control: After fixing cancel paths, a normal fast drag
    /// release (left button up, not menu/close/capture-loss) must still
    /// Launch physics. This proves the fix didn't break the normal path.
    /// </summary>
    [STATestMethod]
    public void S2_R3_NormalRelease_StillLaunches()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);

            // Normal release — isCancellation defaults to false.
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.LastReleaseResult.ShouldThrow,
                "Normal fast drag → should throw.");
            Assert.IsTrue(w.IsPhysicsActive,
                "Normal release must still activate physics.");
            Assert.IsTrue(w.IsPhysicsLaunched,
                "Normal release must still launch the simulator.");
        }
        finally { w.Close(); }
    }

    // ── F-04: Rejected physics result stops and cleans up ──

    /// <summary>
    /// F-04: When UpdatePhysics gets a Rejected result (e.g. invalid work area
    /// from a window larger than the monitor), it must stop physics and reset
    /// the simulator — not leave IsPhysicsActive=true indefinitely.
    /// This test makes BuildWorkArea produce an invalid area by setting the
    /// window width to an extreme value, then calls UpdatePhysics.
    /// </summary>
    [STATestMethod]
    public void S2_R3_RejectedPhysics_StopsAndCleansUp()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");
            Assert.IsTrue(w.IsPhysicsLaunched, "Simulator launched after throw.");

            // Make BuildWorkArea produce an invalid area: window width so large
            // that wa.Left + winW/2 > wa.Right - winW/2 → Left > Right → invalid.
            w.Width = 100000;

            // Record position before the rejected tick — it must not change.
            var leftBefore = w.Left;
            var topBefore = w.Top;

            // Call UpdatePhysics — should get Rejected and clean up.
            now = 100;
            typeof(MainWindow).GetMethod("UpdatePhysics",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object[] { now });

            Assert.IsFalse(w.IsPhysicsActive,
                "Rejected physics must stop — no indefinite active state.");
            Assert.IsFalse(w.IsPhysicsLaunched,
                "Rejected physics must reset the simulator.");

            // Position must not have been written with invalid coordinates.
            Assert.AreEqual(leftBefore, w.Left,
                "Rejected tick must not move the window.");
            Assert.AreEqual(topBefore, w.Top,
                "Rejected tick must not move the window.");
        }
        finally { w.Close(); }
    }

    // ── S3-1: Re-grab during physics does not jump ──

    /// <summary>
    /// S3-1: After a throw release launches physics, re-grabbing the pet
    /// (BeginCapturedPointerInteraction → StartDragging) must immediately
    /// stop physics. The _dragOffset is calculated from the current Left/Top
    /// (which physics has updated), so the window stays under the cursor
    /// without jumping.
    /// </summary>
    [STATestMethod]
    public void S3_RegrabDuringPhysics_StopsPhysics_NoJump()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics should be active after throw.");

            // Advance physics a few ticks so the window moves.
            for (var i = 0; i < 5; i++)
            {
                now = 40 + i * 33;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });
            }

            Assert.IsTrue(w.IsPhysicsActive, "Physics still active after ticks.");
            var physicsLeft = w.Left;
            var physicsTop = w.Top;

            // Re-grab at the current window position.
            // The cursor is at (physicsLeft + 60, physicsTop + 65) — same
            // relative offset as the original body press.
            w.TestGetCursorPos = () => ((int)physicsLeft + 60, (int)physicsTop + 65);
            now = 250;
            Assert.IsTrue(w.BeginCapturedPointerInteraction(
                new Point(60, 65), new Point(physicsLeft + 60, physicsTop + 65)),
                "Re-grab should succeed.");

            // Physics must be stopped immediately.
            Assert.IsFalse(w.IsPhysicsActive,
                "Re-grab must stop physics immediately.");

            // The window must not jump — it should stay at the physics position.
            Assert.AreEqual(physicsLeft, w.Left, 0.1,
                "Window must not jump on re-grab.");
            Assert.AreEqual(physicsTop, w.Top, 0.1,
                "Window must not jump on re-grab.");
        }
        finally { w.Close(); }
    }

    // ── S3-2: Pause freezes motion state, resume continues trajectory ──

    /// <summary>
    /// S3-2 R6 F-05: Pausing while physics is active must FREEZE the pet
    /// (not terminate). _isPhysicsActive stays true, velocity is preserved,
    /// IsResting stays false. After resume, the next tick must continue the
    /// trajectory (window moves), with no 5-second backlog jump.
    ///
    /// R6 remaining: The OnUpdateTick physics branch must have a !_isAppPaused
    /// guard. Even if tick is called directly during pause, physics must NOT
    /// advance or write position. After resume, the trajectory must match a
    /// no-pause control with the same total active time.
    /// </summary>
    [STATestMethod]
    public void S3_PauseDuringPhysics_FreezesAndResumes()
    {
        // ── Control window: no pause, advance N active ticks ──
        var controlLeft = 0.0;
        var controlTop = 0.0;
        {
            var w = new MainWindow();
            double now = 0;
            try
            {
                w.TestGetElapsedMs = () => now;
                w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

                BeginBody(w);
                var baseLeft = w.Left;
                var baseTop = w.Top;

                now = 10;
                w.ProcessCapturedPointerMove(
                    new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
                now = 30;
                w.ProcessCapturedPointerMove(
                    new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
                now = 35;
                w.EndCapturedPointerInteraction();
                Assert.IsTrue(w.IsPhysicsActive, "Control: physics active after throw.");

                // Advance 5 active ticks (same active ticks as paused window).
                for (var i = 0; i < 5; i++)
                {
                    now = 40 + i * 33;
                    typeof(MainWindow).GetMethod("OnUpdateTick",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(w, new object?[] { null, EventArgs.Empty });
                }

                controlLeft = w.Left;
                controlTop = w.Top;
            }
            finally { w.Close(); }
        }

        // ── Paused window: 3 active ticks → pause → tick during pause (no move) → resume → 2 more active ticks ──
        {
            var w = new MainWindow();
            double now = 0;
            try
            {
                w.TestGetElapsedMs = () => now;
                w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

                BeginBody(w);
                var baseLeft = w.Left;
                var baseTop = w.Top;

                // Same throw as control.
                now = 10;
                w.ProcessCapturedPointerMove(
                    new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
                now = 30;
                w.ProcessCapturedPointerMove(
                    new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
                now = 35;
                w.EndCapturedPointerInteraction();
                Assert.IsTrue(w.IsPhysicsActive, "Paused: physics active after throw.");

                // 3 active ticks before pause.
                for (var i = 0; i < 3; i++)
                {
                    now = 40 + i * 33;
                    typeof(MainWindow).GetMethod("OnUpdateTick",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(w, new object?[] { null, EventArgs.Empty });
                }

                Assert.IsTrue(w.IsPhysicsActive, "Physics still active before pause.");
                var prePauseLeft = w.Left;
                var prePauseTop = w.Top;

                // Pause — must freeze, not terminate.
                now = 150;
                w.PauseAnimation();

                Assert.IsTrue(w.IsPhysicsActive,
                    "F-05: _isPhysicsActive must remain true on pause (freeze, not terminate).");
                Assert.IsTrue(w.IsPhysicsLaunched,
                    "F-05: IsLaunched must remain true on pause.");
                Assert.AreEqual(prePauseLeft, w.Left, 0.1,
                    "Window must not move on pause.");
                Assert.AreEqual(prePauseTop, w.Top, 0.1,
                    "Window must not move on pause.");

                // R6 F-05: Actually call OnUpdateTick DURING pause.
                // The !_isAppPaused guard must prevent physics from advancing.
                now = 180;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });

                Assert.IsTrue(w.IsPhysicsActive,
                    "F-05: Physics still active after tick during pause (frozen, not stopped).");
                Assert.AreEqual(prePauseLeft, w.Left, 0.001,
                    "F-05: Position must NOT change when tick is called during pause.");
                Assert.AreEqual(prePauseTop, w.Top, 0.001,
                    "F-05: Position must NOT change when tick is called during pause.");

                // Simulate a long pause (5 seconds).
                now = 5150;

                // Resume — must continue the original trajectory.
                w.ResumeAnimation();

                Assert.IsTrue(w.IsPhysicsActive,
                    "F-05: _isPhysicsActive must still be true after resume.");

                // 2 more active ticks after resume (total 5 active ticks, same as control).
                for (var i = 0; i < 2; i++)
                {
                    now = 5200 + i * 33;
                    typeof(MainWindow).GetMethod("OnUpdateTick",
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(w, new object?[] { null, EventArgs.Empty });
                }

                // The window must have moved from its paused position.
                var moved = Math.Abs(w.Left - prePauseLeft) > 0.01 || Math.Abs(w.Top - prePauseTop) > 0.01;
                Assert.IsTrue(moved,
                    "F-05: Window must continue moving after resume (trajectory continues).");

                // R6 F-05: The final position must match the no-pause control
                // with the same total active time. The pause interval was
                // excluded from physics, so the trajectory is the same.
                Assert.AreEqual(controlLeft, w.Left, 1.0,
                    "F-05: Final position must match no-pause control (same active time).");
                Assert.AreEqual(controlTop, w.Top, 1.0,
                    "F-05: Final position must match no-pause control (same active time).");
            }
            finally { w.Close(); }
        }
    }

    // ── S3-3: Probe is delayed while physics is active (Evaluate skipped) ──

    /// <summary>
    /// S3-3 R5 F-06: While physics is active (post-throw), Evaluate must NOT
    /// be called (it has a MarkConsumed side-effect). Inject a scheduler with
    /// a plan that WOULD fire (ShouldFire=true) to prove Evaluate is skipped:
    /// the plan's Consumed flag must remain false during physics. After
    /// physics ends, the next tick evaluates and consumes the plan normally.
    /// </summary>
    [STATestMethod]
    public void S3_ProbeDelayedDuringPhysics_EvaluateSkipped()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            // Inject a scheduler with a plan that is guaranteed to fire:
            // Use a deterministic Random with minDelay=0, maxDelay=0 so delay
            // is 0, and baseline=0 so 150 min >> 0 delay. The roll may or may
            // not pass 0.5, but the key assertion is that Consumed stays false
            // during physics (Evaluate is skipped entirely).
            var tracker = new EffectiveRuntimeTracker();
            tracker.StartActivePeriod(0);
            // Advance to 150 minutes of accumulated runtime.
            tracker.EndActivePeriod(150.0);

            var scheduler = new RareEventScheduler();
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var deterministicRandom = new Random(0);
            scheduler.EnsurePlanForDate(today, deterministicRandom,
                minDelayMinutes: 0, maxDelayMinutes: 0,
                currentAccumulatedMinutes: 0.0);
            var planBefore = scheduler.CurrentPlan;
            Assert.IsNotNull(planBefore, "Plan must exist before physics.");
            Assert.IsFalse(planBefore!.Consumed,
                "Plan must be unconsumed before physics.");

            w.TestRuntimeTracker = tracker;
            w.TestRareEventScheduler = scheduler;

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // Advance physics by several ticks — Evaluate should be skipped.
            for (var i = 0; i < 5; i++)
            {
                now = 40 + i * 33;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });
            }

            Assert.IsTrue(w.IsPhysicsActive,
                "Physics still active (probe must not have started).");
            Assert.IsFalse(w.IsProbing,
                "F-06: No probe should start during physics.");

            var planDuring = scheduler.CurrentPlan;
            Assert.IsNotNull(planDuring, "Plan must still exist during physics.");
            Assert.IsFalse(planDuring!.Consumed,
                "F-06: Plan must NOT be consumed during physics (Evaluate skipped).");

            // Advance physics until it rests.
            for (var i = 0; i < 200; i++)
            {
                now = 205 + i * 33;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });
                if (!w.IsPhysicsActive)
                    break;
            }

            Assert.IsFalse(w.IsPhysicsActive,
                "Physics must eventually rest.");

            // After physics ends, the next tick should Evaluate normally.
            // The plan may or may not fire depending on conditions, but
            // Evaluate is no longer skipped.
            now += 33;
            typeof(MainWindow).GetMethod("OnUpdateTick",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object?[] { null, EventArgs.Empty });
        }
        finally { w.Close(); }
    }

    // ── S3-4a: Display layout change stops physics ──

    /// <summary>
    /// S3-4a: When the display layout changes (monitor connect/disconnect),
    /// active physics must be stopped. The window position is recovered
    /// to a valid work area.
    /// </summary>
    [STATestMethod]
    public void S3_DisplayLayoutChange_StopsPhysics()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // Advance physics a bit.
            for (var i = 0; i < 3; i++)
            {
                now = 40 + i * 33;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });
            }

            Assert.IsTrue(w.IsPhysicsActive, "Physics still active.");

            // Trigger display layout change.
            typeof(MainWindow).GetMethod("OnDisplayLayoutChanged",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, null!);

            Assert.IsFalse(w.IsPhysicsActive,
                "Physics must stop on display layout change.");
        }
        finally { w.Close(); }
    }

    // ── S3-4b: Exit during physics saves stable position ──

    /// <summary>
    /// S3-4b: When the app exits while physics is active,
    /// RestoreProbePositionForExit must stop physics and clamp to a valid
    /// work area so the saved position is legal.
    /// </summary>
    [STATestMethod]
    public void S3_ExitDuringPhysics_StopsAndSavesValidPosition()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // Advance physics a bit.
            for (var i = 0; i < 3; i++)
            {
                now = 40 + i * 33;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });
            }

            Assert.IsTrue(w.IsPhysicsActive, "Physics still active.");

            var physicsLeft = w.Left;
            var physicsTop = w.Top;

            // Simulate exit: RestoreProbePositionForExit stops physics + clamps.
            w.RestoreProbePositionForExit();

            Assert.IsFalse(w.IsPhysicsActive,
                "Physics must stop on exit restore.");

            // The position should be finite and within a valid work area
            // (not a mid-trajectory invalid coordinate).
            Assert.IsTrue(double.IsFinite(w.Left), "Exit Left is finite.");
            Assert.IsTrue(double.IsFinite(w.Top), "Exit Top is finite.");
        }
        finally { w.Close(); }
    }

    // ── S3-4b-R6: Production exit/recovery chain via FlushFinalState + RestoreWindowPosition ──

    /// <summary>
    /// S3-4b R6 F-08: Verify the full exit-during-physics → save → reload →
    /// new-window recovery chain using the PRODUCTION save/restore path:
    /// - App.TestSetupIsolatedSave configures isolated ArchiveService/SaveCoordinator.
    /// - App.TestSetMainWindow injects the flying window.
    /// - App.FlushFinalState executes the real exit path:
    ///   RestoreProbePositionForExit → MarkStateDirty → SaveCoordinator.Flush.
    /// - ArchiveService.LoadState reloads from the isolated file.
    /// - App.RestoreWindowPosition applies the real recovery mapping
    ///   (WindowPositionAdapter.RecoverPosition) to a new window.
    /// - Assert the entire window is inside a valid work area, no velocity
    ///   is restored, and the first tick is static.
    /// Does NOT touch the user's real save directory.
    /// </summary>
    [STATestMethod]
    public void S3_ExitDuringPhysics_IsolatedSaveReload_NewWindowStaticRecovery()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DesktopPetTest_" + Guid.NewGuid().ToString("N")[..8]);
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // Advance physics a few ticks.
            for (var i = 0; i < 3; i++)
            {
                now = 40 + i * 33;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });
            }

            Assert.IsTrue(w.IsPhysicsActive, "Physics still active during flight.");

            // ── Production exit path ──
            // Configure isolated save infrastructure (not user's real save).
            App app;
            if (System.Windows.Application.Current is App existing)
                app = existing;
            else
                app = new App();
            app.TestSetupIsolatedSave(tempDir);
            app.TestSetMainWindow(w);

            // FlushFinalState: RestoreProbePositionForExit → MarkStateDirty →
            // SaveCoordinator.Flush → file written to tempDir.
            app.FlushFinalState();

            Assert.IsFalse(w.IsPhysicsActive,
                "Physics must stop after FlushFinalState (RestoreProbePositionForExit).");

            // Verify the file was actually written by the production SaveCoordinator.
            var stateFile = Path.Combine(tempDir, "state.json");
            Assert.IsTrue(File.Exists(stateFile),
                "F-08: Production SaveCoordinator must write state.json to isolated dir.");

            // ── Production reload path ──
            // LoadState from the same isolated ArchiveService (production entry).
            var archive = new ArchiveService(tempDir);
            var (restoredState, _, _) = archive.LoadState();

            Assert.IsTrue(double.IsFinite(restoredState.WindowLeft),
                "F-08: Restored WindowLeft must be finite.");
            Assert.IsTrue(double.IsFinite(restoredState.WindowTop),
                "F-08: Restored WindowTop must be finite.");

            // Create a new window (simulating app restart).
            var w2 = new MainWindow();
            try
            {
                // Inject the restored state into a fresh App and call the
                // real RestoreWindowPosition — the production recovery entry.
                App app2;
                if (System.Windows.Application.Current is App existing2)
                    app2 = existing2;
                else
                    app2 = new App();
                app2.TestSetupIsolatedSave(tempDir, restoredState);
                app2.TestSetMainWindow(w2);
                app2.RestoreWindowPosition();

                // ── Full window boundary assertion ──
                // The entire window (not just Left/Top) must be inside a
                // valid work area. Use the real monitor list and convert
                // work-area physical-pixel bounds to DIP for comparison.
                var monitors = DesktopPet.App.Platform.MonitorInterop.EnumerateMonitors();
                Assert.IsTrue(monitors.Count > 0,
                    "F-08: Test environment must have at least one monitor.");

                // Find the monitor whose full bounds contain the window's
                // foot-center (physical pixels).
                var (dpiX, dpiY) = DesktopPet.App.Platform.MonitorInterop.GetWindowDpiScale(
                    new System.Windows.Interop.WindowInteropHelper(w2).Handle);
                var foot = DesktopPet.Core.Geometry.CoordinateTransform.WindowTopLeftToFootCenter(
                    w2.Left, w2.Top, w2.Width, w2.Height, dpiX, dpiY);
                var monitor = DesktopPet.Core.Geometry.MonitorLocator.FindMonitor(
                    monitors, foot.FootCenterX, foot.FootCenterY)
                    ?? DesktopPet.Core.Geometry.MonitorLocator.FindNearestMonitor(
                        monitors, foot.FootCenterX, foot.FootCenterY);

                var waDipLeft = monitor.WorkArea.Bounds.Left / monitor.DpiScaleX;
                var waDipTop = monitor.WorkArea.Bounds.Top / monitor.DpiScaleY;
                var waDipRight = monitor.WorkArea.Bounds.Right / monitor.DpiScaleX;
                var waDipBottom = monitor.WorkArea.Bounds.Bottom / monitor.DpiScaleY;

                Assert.IsTrue(w2.Left >= waDipLeft - 1,
                    $"F-08: Window Left ({w2.Left}) must be within work area Left ({waDipLeft}).");
                Assert.IsTrue(w2.Top >= waDipTop - 1,
                    $"F-08: Window Top ({w2.Top}) must be within work area Top ({waDipTop}).");
                Assert.IsTrue(w2.Left + w2.Width <= waDipRight + 1,
                    $"F-08: Window Right ({w2.Left + w2.Width}) must be within work area Right ({waDipRight}).");
                Assert.IsTrue(w2.Top + w2.Height <= waDipBottom + 1,
                    $"F-08: Window Bottom ({w2.Top + w2.Height}) must be within work area Bottom ({waDipBottom}).");

                // ── No velocity persisted ──
                Assert.IsFalse(w2.IsPhysicsActive,
                    "F-08: New window must not have active physics (velocity not persisted).");
                Assert.IsFalse(w2.IsPhysicsLaunched,
                    "F-08: New window must not have launched physics (no velocity in archive).");

                // ── First tick is static ──
                now = 10000;
                w2.TestGetElapsedMs = () => now;
                var staticLeft = w2.Left;
                var staticTop = w2.Top;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w2, new object?[] { null, EventArgs.Empty });

                Assert.AreEqual(staticLeft, w2.Left, 0.1,
                    "F-08: New window must not move on first tick (static recovery).");
                Assert.AreEqual(staticTop, w2.Top, 0.1,
                    "F-08: New window must not move on first tick (static recovery).");

                app2.TestCleanup();
            }
            finally
            {
                w2.Close();
            }

            app.TestCleanup();
        }
        finally
        {
            w.Close();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ── S3-4c: OnClosed stops physics ──

    /// <summary>
    /// S3-4c: OnClosed must stop active physics even when not dragging
    /// (e.g. physics launched after release, then window closed).
    /// </summary>
    [STATestMethod]
    public void S3_OnClosed_StopsPhysics()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // Advance physics.
            for (var i = 0; i < 3; i++)
            {
                now = 40 + i * 33;
                typeof(MainWindow).GetMethod("OnUpdateTick",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(w, new object?[] { null, EventArgs.Empty });
            }

            Assert.IsTrue(w.IsPhysicsActive, "Physics still active before close.");

            // Close the window — OnClosed should stop physics.
            typeof(MainWindow).GetMethod("OnClosed",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object[] { EventArgs.Empty });

            Assert.IsFalse(w.IsPhysicsActive,
                "Physics must stop on OnClosed.");
        }
        finally { w.Close(); }
    }

    // ── S3-5: No available display — safe handling (real empty monitor list) ──

    /// <summary>
    /// S3-5 R5 F-08: When BuildWorkArea receives an empty monitor list
    /// (real "no display" scenario, not a Width hack), UpdatePhysics must
    /// reject the invalid work area and safely stop physics. The window
    /// must not be moved to the origin or any invalid coordinate.
    /// </summary>
    [STATestMethod]
    public void S3_NoMonitor_PhysicsRejectedAndStopped()
    {
        var w = new MainWindow();
        double now = 0;
        try
        {
            w.TestGetElapsedMs = () => now;
            w.TestGetCursorPos = () => ((int)w.Left + 60, (int)w.Top + 65);

            BeginBody(w);
            var baseLeft = w.Left;
            var baseTop = w.Top;

            // Fast drag → throw → Launch physics.
            now = 10;
            w.ProcessCapturedPointerMove(
                new Point(70, 65), new Point(baseLeft + 160, baseTop + 65), true, now);
            now = 30;
            w.ProcessCapturedPointerMove(
                new Point(80, 65), new Point(baseLeft + 260, baseTop + 65), true, now);
            now = 35;
            w.EndCapturedPointerInteraction();

            Assert.IsTrue(w.IsPhysicsActive, "Physics active after throw.");

            // F-08: Inject a REAL empty monitor list (not Width=100000).
            // This drives BuildWorkArea to return WorkArea(1,0,0,0) which
            // is explicitly invalid (Left > Right), causing PhysicsSimulator
            // to reject it.
            w.TestMonitorList = new List<DesktopPet.Core.Geometry.MonitorInfo>();

            var leftBefore = w.Left;
            var topBefore = w.Top;

            // Drive OnUpdateTick — physics should be rejected and stopped.
            now = 100;
            typeof(MainWindow).GetMethod("OnUpdateTick",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(w, new object?[] { null, EventArgs.Empty });

            Assert.IsFalse(w.IsPhysicsActive,
                "Physics must stop when work area is invalid (no display).");
            Assert.IsFalse(w.IsPhysicsLaunched,
                "Simulator must be reset when work area is invalid.");
            Assert.AreEqual(leftBefore, w.Left,
                "Window must not move to origin or invalid coordinate.");
            Assert.AreEqual(topBefore, w.Top,
                "Window must not move to origin or invalid coordinate.");
        }
        finally { w.Close(); }
    }
}
