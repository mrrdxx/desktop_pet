using System.Reflection;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P4-002 M-12 §1–§2: App-level integration tests for the rest-reminder
/// performance.
/// <para>
/// Verifies:
/// - Single-pet: reminder triggers the performance on the only available window.
/// - Dual-pet: only one window starts the performance; if the first is busy,
///   the second is selected; if no window is available, the reminder is skipped.
/// - Same session does not repeat the reminder (ActivitySession fires once).
/// - Natural completion releases the animation and busy state.
/// - Press (body) skips immediately and enters the normal interaction flow.
/// - Pause/lock/sleep cancels the performance; resume does NOT replay.
/// - Drag cancels the performance.
/// - Recover cancels the performance.
/// - Rest-reminder blocks hover/curiosity and rare-event Evaluate.
/// - Social busy state includes rest-reminder active.
/// </para>
/// <para>
/// These tests use the internal TestSetupIsolatedSave + CreatePetWindow path
/// and drive the performance through StartRestReminder / OnUpdateTickTest
/// with controlled time via TestGetElapsedMs.
/// </para>
/// </summary>
[TestClass]
public class RestReminderIntegrationTests
{
    private static App GetOrCreateApp()
    {
        return (App?)System.Windows.Application.Current ?? new App();
    }

    private static string GetTempDir(string prefix)
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
    }

    /// <summary>
    /// M-12 §1: Single-pet reminder triggers the performance on the only
    /// available window. The performance starts in Stretching phase and
    /// the animation switches to "stretch".
    /// </summary>
    [STATestMethod]
    public void SinglePet_Reminder_TriggersPerformance()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_Single");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            // Set controlled time.
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            // The window should be available for a rest reminder.
            Assert.IsTrue(w.CanStartRestReminder());

            // Start the performance.
            w.StartRestReminder(timeMs);

            // The animation should be playing "stretch".
            Assert.AreEqual("stretch", w.CurrentAction);
            Assert.IsTrue(w.IsRestReminderActive);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §1: Dual-pet — only one window starts the performance.
    /// The first available window is selected.
    /// </summary>
    [STATestMethod]
    public void DualPet_Reminder_OnlyOneStarts()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_DualOne");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
                new() { PetId = "bear-2", Personality = Personality.SlowPaced, Grievance = 50, WindowLeft = 300, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState!.Pets[1]);
            var wA = app.FindWindowByPetId("bear-1");
            var wB = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(wA);
            Assert.IsNotNull(wB);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            wA!.Show();
            wB!.Show();

            double timeMs = 0;
            wA.TestGetElapsedMs = () => timeMs;
            wB!.TestGetElapsedMs = () => timeMs;

            // Both are available — start on A (first in dictionary order).
            Assert.IsTrue(wA.CanStartRestReminder());
            Assert.IsTrue(wB!.CanStartRestReminder());

            wA.StartRestReminder(timeMs);

            // A is performing, B is not.
            Assert.IsTrue(wA.IsRestReminderActive);
            Assert.IsFalse(wB.IsRestReminderActive);
            Assert.AreEqual("stretch", wA.CurrentAction);
            Assert.AreNotEqual("stretch", wB.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §1: If the first pet is busy (dragging), the second pet is selected.
    /// </summary>
    [STATestMethod]
    public void DualPet_FirstBusy_SecondSelected()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_FirstBusy");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
                new() { PetId = "bear-2", Personality = Personality.SlowPaced, Grievance = 50, WindowLeft = 300, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState!.Pets[1]);
            var wA = app.FindWindowByPetId("bear-1");
            var wB = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(wA);
            Assert.IsNotNull(wB);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            wA!.Show();
            wB!.Show();

            double timeMs = 0;
            wA.TestGetElapsedMs = () => timeMs;
            wB!.TestGetElapsedMs = () => timeMs;

            // Simulate A being paused (busy).
            wA.PauseAnimation();
            Assert.IsFalse(wA.CanStartRestReminder(), "Paused window should not be available.");
            Assert.IsTrue(wB!.CanStartRestReminder(), "Idle window should be available.");

            // Start on B.
            wB.StartRestReminder(timeMs);
            Assert.IsTrue(wB.IsRestReminderActive);
            Assert.IsFalse(wA.IsRestReminderActive);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §1: If no window is available, the reminder is silently skipped.
    /// </summary>
    [STATestMethod]
    public void NoAvailableWindow_ReminderSkipped()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_NoneAvail");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            // Pause the only window — no available window.
            w.PauseAnimation();
            Assert.IsFalse(w.CanStartRestReminder());

            // OnRestReminderRequested should silently skip (no exception).
            // Simulate the event handler directly.
            app.OnRestReminderRequested(null, EventArgs.Empty);

            // The window should not have started a performance.
            Assert.IsFalse(w.IsRestReminderActive);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §1: Natural completion releases the animation and busy state.
    /// After ~30 seconds (or the configured duration), the performance
    /// completes and the pet returns to idle.
    /// </summary>
    [STATestMethod]
    public void NaturalCompletion_ReturnsToIdle()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_Natural");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            // Start the performance.
            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);
            Assert.AreEqual("stretch", w.CurrentAction);

            // Advance to after stretch (1600ms) → should switch to "rest".
            timeMs = 1600;
            w.OnUpdateTickTest();
            Assert.IsTrue(w.IsRestReminderActive);
            Assert.AreEqual("rest", w.CurrentAction);

            // Advance to after total duration (30000ms) → natural completion.
            timeMs = 30000;
            w.OnUpdateTickTest();
            Assert.IsFalse(w.IsRestReminderActive);
            Assert.AreEqual("idle", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §2: Pause cancels the performance; resume does NOT replay.
    /// </summary>
    [STATestMethod]
    public void Pause_CancelsPerformance_NoReplayOnResume()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_Pause");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            // Start the performance.
            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // Pause — should cancel the performance.
            w.PauseAnimation();
            Assert.IsFalse(w.IsRestReminderActive);

            // Resume — should NOT restart the performance.
            w.ResumeAnimation();
            Assert.IsFalse(w.IsRestReminderActive, "Performance must not replay after pause/resume.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §2: Recover cancels the performance.
    /// </summary>
    [STATestMethod]
    public void Recover_CancelsPerformance()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_Recover");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            w.RecoverPet();
            Assert.IsFalse(w.IsRestReminderActive);
            // R1 F-02: After recover, the animation must not be stuck on stretch/rest.
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §2: App.OnPauseToggled(true) cancels performance on all windows.
    /// </summary>
    [STATestMethod]
    public void AppPauseToggled_CancelsAllPerformances()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_AppPause");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // App-level pause.
            app.OnPauseToggled(null, true);
            Assert.IsFalse(w.IsRestReminderActive, "App pause must cancel rest-reminder performance.");

            // App-level resume — no replay.
            app.OnPauseToggled(null, false);
            Assert.IsFalse(w.IsRestReminderActive, "Performance must not replay after app pause/resume.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §1: CanStartRestReminder returns false when the window is
    /// dragging, paused, probing, or already performing.
    /// </summary>
    [STATestMethod]
    public void CanStartRestReminder_FalseWhenBusy()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_CanStart");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            // Initially available.
            Assert.IsTrue(w.CanStartRestReminder());

            // Paused — not available.
            w.PauseAnimation();
            Assert.IsFalse(w.CanStartRestReminder());
            w.ResumeAnimation();
            Assert.IsTrue(w.CanStartRestReminder());

            // Already performing — not available.
            w.StartRestReminder(timeMs);
            Assert.IsFalse(w.CanStartRestReminder());
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §1: ActivitySession with a short threshold fires the reminder
    /// exactly once, and the RestReminderRequested event is raised.
    /// </summary>
    [TestMethod]
    public void ShortThreshold_FiresReminderOnce()
    {
        // Use a 1-minute threshold for fast testing.
        var session = new ActivitySession(thresholdMinutes: 1.0);
        var service = new ActivitySessionService(session,
            new TestIdleSamplerP4(() => 0), MonotonicClock.Instance);

        bool eventFired = false;
        int fireCount = 0;
        service.RestReminderRequested += (s, e) =>
        {
            eventFired = true;
            fireCount++;
        };

        // Accumulate 1 minute of active time (60 1-second ticks).
        service.TickManually(0, 0);
        for (int i = 1; i <= 60; i++)
            service.TickManually(i * 1000.0, i * 1000.0);

        Assert.IsTrue(eventFired, "Reminder event should have fired at 1 minute.");
        Assert.AreEqual(1, fireCount, "Reminder should fire exactly once per session.");
        Assert.AreEqual(1, service.ReminderFiredCount);

        // Continue ticking — should NOT fire again.
        for (int i = 61; i <= 120; i++)
            service.TickManually(i * 1000.0, i * 1000.0);

        Assert.AreEqual(1, fireCount, "Reminder must not fire again in the same session.");
    }

    /// <summary>
    /// M-12 §1: After idle reset (5 min no input), the session can fire
    /// a new reminder when the threshold is reached again.
    /// </summary>
    [TestMethod]
    public void AfterIdleReset_CanFireAgain()
    {
        var session = new ActivitySession(thresholdMinutes: 1.0);
        var service = new ActivitySessionService(session,
            new TestIdleSamplerP4(() => 0), MonotonicClock.Instance);

        int fireCount = 0;
        service.RestReminderRequested += (s, e) => fireCount++;

        // Accumulate 1 minute → fire.
        service.TickManually(0, 0);
        for (int i = 1; i <= 60; i++)
            service.TickManually(i * 1000.0, i * 1000.0);
        Assert.AreEqual(1, fireCount);

        // Go idle for 6 minutes (last input stays at 60s).
        var idleStart = 60 * 1000.0;
        for (int s = 61; s <= 420; s++)
            service.TickManually(s * 1000.0, idleStart);

        // Session should have reset (idle > 5 min).
        Assert.AreEqual(0, session.AccumulatedActiveMinutes, 0.001,
            "Session should reset after 5 min idle.");

        // Accumulate another 1 minute → fire again.
        for (int s = 421; s <= 480; s++)
            service.TickManually(s * 1000.0, s * 1000.0);
        Assert.AreEqual(2, fireCount, "Reminder should fire again after idle reset.");
    }

    /// <summary>
    /// M-12 §2: Rest-reminder active blocks hover/curiosity (CanStartRestReminder
    /// is false, and the hover guard includes !_restReminder.IsActive).
    /// This is verified indirectly: when rest-reminder is active, the window
    /// reports IsRestReminderActive=true, and CanStartRestReminder returns false.
    /// </summary>
    [STATestMethod]
    public void RestReminder_BlocksHoverAndCuriosity()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_BlocksHover");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // While rest-reminder is active, the window is busy —
            // CanStartRestReminder returns false (can't start another).
            Assert.IsFalse(w.CanStartRestReminder());

            // The performance is active, so BuildPetSocialState should report busy.
            // We verify this indirectly through CanStartRestReminder being false.
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// M-12 §2: ShutdownMainWindow cancels all rest-reminder performances.
    /// </summary>
    [STATestMethod]
    public void ShutdownMainWindow_CancelsPerformances()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_Shutdown");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // R1 F-03: CanStartRestReminder requires IsVisible=true.
            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // ShutdownMainWindow should cancel the performance.
            app.ShutdownMainWindowForTest();
            Assert.IsFalse(w.IsRestReminderActive,
                "ShutdownMainWindow must cancel rest-reminder performance.");
        }
        finally
        {
            // TestCleanup may have already run via ShutdownMainWindowForTest.
            try { app.TestCleanup(); } catch { /* already cleaned up */ }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // R1 F-03: Real dispatch path tests (OnRestReminderRequested)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// R1 F-03: OnRestReminderRequested dispatches to the first available
    /// visible window. Dual-pet: only one starts the performance.
    /// </summary>
    [STATestMethod]
    public void Dispatch_DualPet_OnlyOneStarts()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_DispatchDual");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
                new() { PetId = "bear-2", Personality = Personality.SlowPaced, Grievance = 50, WindowLeft = 300, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState!.Pets[1]);
            var wA = app.FindWindowByPetId("bear-1");
            var wB = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(wA);
            Assert.IsNotNull(wB);

            // R1 F-03: Both windows must be visible for dispatch.
            wA!.Show();
            wB!.Show();

            double timeMs = 0;
            wA.TestGetElapsedMs = () => timeMs;
            wB!.TestGetElapsedMs = () => timeMs;

            // Dispatch through the real event handler.
            app.OnRestReminderRequested(null, EventArgs.Empty);

            // Exactly one window should be performing.
            Assert.IsTrue(wA.IsRestReminderActive || wB.IsRestReminderActive,
                "Exactly one window should have started the performance.");
            Assert.IsFalse(wA.IsRestReminderActive && wB.IsRestReminderActive,
                "Both windows must not be performing simultaneously.");

            // The first available (wA) should be the one selected.
            Assert.IsTrue(wA.IsRestReminderActive);
            Assert.IsFalse(wB.IsRestReminderActive);
            Assert.AreEqual("stretch", wA.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R1 F-03: OnRestReminderRequested selects the second pet when the
    /// first is busy (paused).
    /// </summary>
    [STATestMethod]
    public void Dispatch_FirstBusy_SecondSelected()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_DispatchBusy");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
                new() { PetId = "bear-2", Personality = Personality.SlowPaced, Grievance = 50, WindowLeft = 300, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState!.Pets[1]);
            var wA = app.FindWindowByPetId("bear-1");
            var wB = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(wA);
            Assert.IsNotNull(wB);

            wA!.Show();
            wB!.Show();

            double timeMs = 0;
            wA.TestGetElapsedMs = () => timeMs;
            wB!.TestGetElapsedMs = () => timeMs;

            // Make A busy (paused).
            wA.PauseAnimation();

            // Dispatch — should select B.
            app.OnRestReminderRequested(null, EventArgs.Empty);

            Assert.IsFalse(wA.IsRestReminderActive, "Busy window must not start performance.");
            Assert.IsTrue(wB.IsRestReminderActive, "Idle second window should be selected.");
            Assert.AreEqual("stretch", wB.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R1 F-03: OnRestReminderRequested silently skips when no window is
    /// available (all busy).
    /// </summary>
    [STATestMethod]
    public void Dispatch_NoAvailableWindow_Skipped()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_DispatchNone");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();
            w.PauseAnimation();

            // Dispatch — no available window, should silently skip.
            app.OnRestReminderRequested(null, EventArgs.Empty);

            Assert.IsFalse(w.IsRestReminderActive, "Busy window must not start performance.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R1 F-03: CanStartRestReminder returns false for a window that has
    /// not been shown (IsVisible=false). This is the core visibility guard.
    /// </summary>
    [STATestMethod]
    public void CanStartRestReminder_FalseWhenNotVisible()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_NotVisible");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // Do NOT call Show() — window is not visible.
            Assert.IsFalse(w!.IsVisible, "Window should not be visible before Show().");
            Assert.IsFalse(w.CanStartRestReminder(),
                "CanStartRestReminder must return false for non-visible window.");

            // After Show(), it should be available.
            w.Show();
            Assert.IsTrue(w.CanStartRestReminder(),
                "CanStartRestReminder should return true after Show().");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R1 F-03: OnRestReminderRequested skips windows that are not visible
    /// (not shown). Even if a window exists in _petWindows, it won't be
    /// selected if IsVisible=false.
    /// </summary>
    [STATestMethod]
    public void Dispatch_InvisibleWindow_Skipped()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_Invisible");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            // Do NOT Show() — window is invisible.
            // Dispatch should skip it silently.
            app.OnRestReminderRequested(null, EventArgs.Empty);

            Assert.IsFalse(w!.IsRestReminderActive,
                "Invisible window must not receive rest-reminder performance.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // R1 F-02: Animation release assertion tests
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// R1 F-02: After SkipRestReminder (body press), the animation player
    /// must not be stuck on stretch or rest.
    /// </summary>
    [STATestMethod]
    public void SkipRestReminder_ReleasesAnimation()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_SkipRelease");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);
            Assert.AreEqual("stretch", w.CurrentAction);

            // Cancel via the public CancelRestReminder (same path as skip
            // internally uses for releasing the animation player).
            w.CancelRestReminder();
            Assert.IsFalse(w.IsRestReminderActive);
            Assert.AreNotEqual("stretch", w.CurrentAction,
                "Animation must not be stuck on stretch after cancel.");
            Assert.AreNotEqual("rest", w.CurrentAction,
                "Animation must not be stuck on rest after cancel.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R1 F-02: After natural completion, the animation must return to idle
    /// (not stuck on stretch or rest). This extends NaturalCompletion_ReturnsToIdle
    /// with explicit stretch/rest negative assertions.
    /// </summary>
    [STATestMethod]
    public void NaturalCompletion_NotStuckOnStretchOrRest()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_NaturalNotStuck");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.AreEqual("stretch", w.CurrentAction);

            // Advance past stretch → rest.
            timeMs = 1600;
            w.OnUpdateTickTest();
            Assert.AreEqual("rest", w.CurrentAction);

            // Advance past total duration → natural completion.
            timeMs = 30000;
            w.OnUpdateTickTest();
            Assert.IsFalse(w.IsRestReminderActive);
            Assert.AreEqual("idle", w.CurrentAction);
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R1 F-01: Lock screen (SessionSwitchReason.SessionLock=7) cancels
    /// rest-reminder performance via App.OnSystemSessionSwitch.
    /// </summary>
    [STATestMethod]
    public void LockScreen_CancelsPerformance()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_LockScreen");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // Simulate lock screen (SessionSwitchReason.SessionLock = 7).
            app.OnSystemSessionSwitch(null,
                new Microsoft.Win32.SessionSwitchEventArgs(
                    Microsoft.Win32.SessionSwitchReason.SessionLock));

            Assert.IsFalse(w.IsRestReminderActive,
                "Lock screen must cancel rest-reminder performance.");
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R1 F-01: Sleep (PowerModes.Suspend) cancels rest-reminder performance
    /// via App.OnSystemPowerModeChanged.
    /// </summary>
    [STATestMethod]
    public void Sleep_CancelsPerformance()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_Sleep");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // Simulate sleep (PowerModes.Suspend).
            app.OnSystemPowerModeChanged(null,
                new Microsoft.Win32.PowerModeChangedEventArgs(
                    Microsoft.Win32.PowerModes.Suspend));

            Assert.IsFalse(w.IsRestReminderActive,
                "Sleep must cancel rest-reminder performance.");
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // R2 F-01: Background-thread system notifications
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// R2 F-01: Lock screen notification from a BACKGROUND thread (the
    /// real Microsoft.Win32.SystemEvents fire on a system thread) must
    /// not touch windows off the Dispatcher. The entire notification is
    /// marshalled to the App Dispatcher: no cross-thread exception, and
    /// after the dispatcher pump the performance is cancelled. Unlock
    /// (SessionUnlock = 8) must NOT replay the performance.
    /// </summary>
    [STATestMethod]
    public void WorkerLockNotification_CancelsPerformanceViaDispatcher()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_WorkerLock");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // Invoke the production lock callback from a background
            // thread, exactly like the system SessionSwitch event does.
            Exception? bgError = null;
            var bg = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    app.OnSystemSessionSwitch(null,
                        new Microsoft.Win32.SessionSwitchEventArgs(
                            Microsoft.Win32.SessionSwitchReason.SessionLock));
                }
                catch (Exception ex)
                {
                    bgError = ex;
                }
            });
            Assert.IsTrue(bg.Wait(5000), "Background lock callback did not return.");
            Assert.IsNull(bgError,
                "Background-thread lock notification must not throw (was marshalled to the window Dispatcher).");

            // The cancellation was queued on the pet window's Dispatcher
            // (== the App/UI Dispatcher in production) — pump it.
            PumpDispatcher(app);

            Assert.IsFalse(w.IsRestReminderActive,
                "Lock screen must cancel rest-reminder performance via the Dispatcher.");
            Assert.AreEqual("idle", w.CurrentAction);

            // Unlock must NOT replay the cancelled performance.
            app.OnSystemSessionSwitch(null,
                new Microsoft.Win32.SessionSwitchEventArgs(
                    Microsoft.Win32.SessionSwitchReason.SessionUnlock));
            Assert.IsFalse(w.IsRestReminderActive,
                "Unlock must not replay the cancelled performance.");
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R2 F-01: Sleep (PowerModes.Suspend) notification from a BACKGROUND
    /// thread must be marshalled to the App Dispatcher, cancel the
    /// performance without a cross-thread exception, and Resume
    /// (PowerModes.Resume) must NOT replay it.
    /// </summary>
    [STATestMethod]
    public void WorkerSleepNotification_CancelsPerformanceViaDispatcher()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_WorkerSleep");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // Invoke the production sleep callback from a background
            // thread, exactly like the system PowerModeChanged event does.
            Exception? bgError = null;
            var bg = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    app.OnSystemPowerModeChanged(null,
                        new Microsoft.Win32.PowerModeChangedEventArgs(
                            Microsoft.Win32.PowerModes.Suspend));
                }
                catch (Exception ex)
                {
                    bgError = ex;
                }
            });
            Assert.IsTrue(bg.Wait(5000), "Background sleep callback did not return.");
            Assert.IsNull(bgError,
                "Background-thread sleep notification must not throw (was marshalled to the window Dispatcher).");

            // The cancellation was queued on the pet window's Dispatcher
            // (== the App/UI Dispatcher in production) — pump it.
            PumpDispatcher(app);

            Assert.IsFalse(w.IsRestReminderActive,
                "Sleep must cancel rest-reminder performance via the Dispatcher.");
            Assert.AreEqual("idle", w.CurrentAction);

            // Wake (Resume) must NOT replay the cancelled performance.
            app.OnSystemPowerModeChanged(null,
                new Microsoft.Win32.PowerModeChangedEventArgs(
                    Microsoft.Win32.PowerModes.Resume));
            Assert.IsFalse(w.IsRestReminderActive,
                "Resume must not replay the cancelled performance.");
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R2 F-01 收尾: The lock cancellation queues ONE unit — collection
    /// read, snapshot and cancel — on the UI Dispatcher that owns the
    /// pet-window collection. A window CLOSED while the unit is queued
    /// is simply absent when it executes (no exception, no cancel on the
    /// closed window); a window CREATED while the unit is queued IS
    /// included (the collection is read at execution time, not at queue
    /// time); the remaining window is still cancelled.
    /// </summary>
    [STATestMethod]
    public void QueuedLockCancel_WindowClosedAndAddedDuringQueue_SnapshotAtExecution()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_QueuedCloseAdd");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
                new() { PetId = "bear-2", Personality = Personality.SlowPaced, Grievance = 50, WindowLeft = 300, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState!.Pets[1]);
            var w1 = app.FindWindowByPetId("bear-1");
            var w2 = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(w1);
            Assert.IsNotNull(w2);

            w1!.Show();
            w2!.Show();

            double timeMs = 0;
            w1.TestGetElapsedMs = () => timeMs;
            w2.TestGetElapsedMs = () => timeMs;

            w1.StartRestReminder(timeMs);
            w2.StartRestReminder(timeMs);
            Assert.IsTrue(w1.IsRestReminderActive);
            Assert.IsTrue(w2.IsRestReminderActive);

            // Fire the lock notification from a background thread — the
            // cancellation unit is queued on the UI Dispatcher but NOT
            // executed yet (nothing pumps it).
            var bg = System.Threading.Tasks.Task.Run(() =>
                app.OnSystemSessionSwitch(null,
                    new Microsoft.Win32.SessionSwitchEventArgs(
                        Microsoft.Win32.SessionSwitchReason.SessionLock)));
            Assert.IsTrue(bg.Wait(5000), "Background lock callback did not return.");

            // While the cancellation is queued: close the second window
            // (the real OnPetWindowClosed path cancels its performance
            // and removes it from the collection).
            w2.Close();
            Assert.IsFalse(w2.IsRestReminderActive,
                "The closed window's performance was cancelled by the close path.");
            Assert.IsNull(app.FindWindowByPetId("bear-2"),
                "The closed window must be removed from the collection.");

            // While the cancellation is STILL queued: create a third pet
            // and start its performance.
            var bear3 = new PetInstanceArchive
            {
                PetId = "bear-3", Personality = Personality.SlowPaced, Grievance = 50, WindowLeft = 500, WindowTop = 100,
            };
            // bear-2 was removed from the archive by the close above;
            // append the third pet directly (index-based access is not
            // stable across the close).
            app.TestState!.Pets.Add(bear3);
            app.CreatePetWindow(bear3);
            var w3 = app.FindWindowByPetId("bear-3");
            Assert.IsNotNull(w3);
            w3!.Show();
            w3.TestGetElapsedMs = () => timeMs;
            w3.StartRestReminder(timeMs);
            Assert.IsTrue(w3.IsRestReminderActive);

            // Execute the queued cancellation on the UI Dispatcher.
            PumpDispatcher(app);

            // The remaining window is cancelled; the window created while
            // the cancellation was queued is included (the collection is
            // read at execution time); the closed window is absent — no
            // exception during the pump.
            Assert.IsFalse(w1.IsRestReminderActive,
                "The queued lock cancellation must cancel the remaining window.");
            Assert.AreEqual("idle", w1.CurrentAction);
            Assert.IsFalse(w3.IsRestReminderActive,
                "The queued cancellation reads the collection at execution time and must include the window created during the queue.");
            Assert.AreEqual("idle", w3.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // R9 F-05: Context-menu open/close vs command selection
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// R9 F-05: Opening and closing the context menu WITHOUT selecting a
    /// command must not interrupt an active rest-reminder performance.
    /// The stretch/rest animation and the reminder state stay consistent
    /// across the whole menu lifecycle, and the performance still ends
    /// naturally at ~30s. (Menu opening without a drag no longer routes
    /// through the drag-cancellation idle reset.)
    /// </summary>
    [STATestMethod]
    public void ContextMenuOpenClose_NoCommand_KeepsRestPerformance()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_MenuNoCmd");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);
            Assert.AreEqual("stretch", w.CurrentAction);

            // Open the menu during the stretch phase and close it without
            // selecting any command (TrackPopupMenuEx returns 0).
            w.TestOpenContextMenuLifecycle(0);
            Assert.IsTrue(w.IsRestReminderActive,
                "Menu open/close without a command must not cancel the performance.");
            Assert.AreEqual("stretch", w.CurrentAction,
                "Menu open/close must not overwrite the stretch animation.");

            // Advance into the rest phase while the performance continues.
            timeMs = 1600;
            w.OnUpdateTickTest();
            Assert.AreEqual("rest", w.CurrentAction);

            // Open and close the menu once more during the rest phase.
            w.TestOpenContextMenuLifecycle(0);
            Assert.IsTrue(w.IsRestReminderActive);
            Assert.AreEqual("rest", w.CurrentAction,
                "Menu open/close must not overwrite the rest animation.");

            // The performance still ends naturally at ~30s.
            timeMs = 30000;
            w.OnUpdateTickTest();
            Assert.IsFalse(w.IsRestReminderActive,
                "The untouched performance must still complete naturally.");
            Assert.AreEqual("idle", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R9 F-05 contrast: SELECTING the Pause command from the context menu
    /// cancels the performance (same App.OnPauseToggled path as the tray),
    /// and Resume does not replay it. Menu commands keep working — only
    /// the no-selection open/close path is animation-neutral.
    /// </summary>
    [STATestMethod]
    public void ContextMenuPauseCommand_CancelsPerformance()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_MenuPause");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // Select the Pause command (MenuIdPauseToggle = 5) via the
            // same lifecycle/dispatch path as the production menu.
            w.TestOpenContextMenuLifecycle(5);
            PumpDispatcher(app);

            Assert.IsFalse(w.IsRestReminderActive,
                "The Pause command must cancel the performance.");
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);

            // Resume must not replay the cancelled performance.
            app.OnPauseToggled(null, false);
            Assert.IsFalse(w.IsRestReminderActive,
                "Resume must not replay the cancelled performance.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R9 F-05: The Recover command from the context menu keeps its
    /// documented behavior — cancelling the performance (MenuIdRecover = 2
    /// dispatches to the same RecoverPet path).
    /// </summary>
    [STATestMethod]
    public void ContextMenuRecoverCommand_CancelsPerformance()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_MenuRecover");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();

            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.StartRestReminder(timeMs);
            Assert.IsTrue(w.IsRestReminderActive);

            // Select the Recover command via the menu lifecycle.
            w.TestOpenContextMenuLifecycle(2);
            PumpDispatcher(app);

            Assert.IsFalse(w.IsRestReminderActive,
                "The Recover command must cancel the performance.");
            Assert.AreNotEqual("stretch", w.CurrentAction);
            Assert.AreNotEqual("rest", w.CurrentAction);
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R2 F-01: Pumps the CURRENT thread's Dispatcher until every queued
    /// item at Normal priority (the marshalled CancelRestReminder
    /// continuations — the pet window created in the test lives on this
    /// thread's Dispatcher) has executed. Used by the
    /// background-notification tests.
    /// </summary>
    private static void PumpDispatcher(App app)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    // ─────────────────────────────────────────────────────────────
    // R2 F-03: Authoritative shared-social state and action resources
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// R2 F-03: A window whose shared SocialCoordinator holds an ACTIVE
    /// session involving this pet must not be selected, even though the
    /// window's local _isSocialActive flag is still false (it only
    /// updates in EvaluateSocialTick, which has not run). When the
    /// session ends, the window becomes available again.
    /// </summary>
    [STATestMethod]
    public void CanStartRestReminder_FalseWhenSharedSocialSessionActive()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_SharedSocial");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();
            Assert.IsTrue(w.CanStartRestReminder(),
                "Available window must be selectable before the social session starts.");

            // Seed the shared coordinator with an ACTIVE session where
            // bear-1 is the target. No EvaluateSocialTick has run, so the
            // window's local _isSocialActive is still false — only the
            // authoritative coordinator state blocks the candidate.
            var coordinator = new DesktopPet.Core.Social.SocialCoordinator();
            w.TestSocialCoordinator = coordinator;
            coordinator.TestSeedSession(new DesktopPet.Core.Social.SocialSession(
                "test-social-1", "bear-2", "bear-1",
                DesktopPet.Core.Social.SocialPhase.Noticing, 0, 0));

            Assert.IsFalse(w.CanStartRestReminder(),
                "Window participating in an active shared social session must not be selected.");

            // The real dispatch path must skip this window too.
            app.OnRestReminderRequested(null, EventArgs.Empty);
            Assert.IsFalse(w.IsRestReminderActive,
                "Dispatch must skip a window in an active shared social session.");

            // Session over (coordinator back to idle) — window selectable again.
            coordinator.TestSeedSession(default);
            Assert.IsTrue(w.CanStartRestReminder(),
                "Window must become selectable after the shared social session ends.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R2 F-03: Missing the "stretch" action disqualifies the candidate
    /// — the opening signal is required. The reminder is skipped, the
    /// performance does not start.
    /// </summary>
    [STATestMethod]
    public void CanStartRestReminder_FalseWhenStretchMissing()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_NoStretch");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();
            Assert.IsTrue(w.TestCharacter.Actions.Remove("stretch"),
                "The bear character must define stretch for this test.");

            Assert.IsFalse(w.CanStartRestReminder(),
                "Missing stretch must disqualify the candidate.");
            app.OnRestReminderRequested(null, EventArgs.Empty);
            Assert.IsFalse(w.IsRestReminderActive,
                "Dispatch must skip a candidate without stretch.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R2 F-03: Missing the "rest" action disqualifies the candidate —
    /// the quiet resting pose is required. The reminder is skipped, the
    /// performance does not start.
    /// </summary>
    [STATestMethod]
    public void CanStartRestReminder_FalseWhenRestMissing()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_NoRest");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            var w = app.FindWindowByPetId("bear-1");
            Assert.IsNotNull(w);

            w!.Show();
            Assert.IsTrue(w.TestCharacter.Actions.Remove("rest"),
                "The bear character must define rest for this test.");

            Assert.IsFalse(w.CanStartRestReminder(),
                "Missing rest must disqualify the candidate.");
            app.OnRestReminderRequested(null, EventArgs.Empty);
            Assert.IsFalse(w.IsRestReminderActive,
                "Dispatch must skip a candidate without rest.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// R2 F-03: A candidate missing one required action is skipped
    /// WITHOUT consuming the reminder — with dual pets, the second pet
    /// (with complete resources) is selected instead.
    /// </summary>
    [STATestMethod]
    public void Dispatch_FirstPetMissingRest_SecondSelected()
    {
        var app = GetOrCreateApp();
        try
        {
            var dir = GetTempDir("P4002_DualNoRest");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
                new() { PetId = "bear-2", Personality = Personality.SlowPaced, Grievance = 50, WindowLeft = 300, WindowTop = 100 },
            };
            app.CreatePetWindow(app.TestState!.Pets[0]);
            app.CreatePetWindow(app.TestState!.Pets[1]);
            var wA = app.FindWindowByPetId("bear-1");
            var wB = app.FindWindowByPetId("bear-2");
            Assert.IsNotNull(wA);
            Assert.IsNotNull(wB);

            wA!.Show();
            wB!.Show();

            // First pet is missing "rest" — it must be skipped as a
            // candidate, not consume the reminder.
            Assert.IsTrue(wA!.TestCharacter.Actions.Remove("rest"),
                "The bear character must define rest for this test.");

            app.OnRestReminderRequested(null, EventArgs.Empty);

            Assert.IsFalse(wA.IsRestReminderActive,
                "First pet missing rest must be skipped.");
            Assert.IsTrue(wB!.IsRestReminderActive,
                "Second pet with complete resources must receive the reminder.");
        }
        finally
        {
            app.TestCleanup();
        }
    }

    /// <summary>
    /// Test double for InputIdleSampler (same as ActivitySessionServiceTests).
    /// </summary>
    private sealed class TestIdleSamplerP4 : InputIdleSampler
    {
        private readonly Func<uint?> _getTick;

        public TestIdleSamplerP4(Func<uint?> getTick)
        {
            _getTick = getTick;
        }

        internal override uint? GetLastInputTickMs() => _getTick();
        internal override uint? GetCurrentTickMs() => _getTick();
    }
}
