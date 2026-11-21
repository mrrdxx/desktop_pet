using DesktopPet.App;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P4-003: App-level integration tests for the night sleepy behavior
/// (R-06: local 23:30 inclusive → 06:00 exclusive).
/// <para>
/// Verifies with a controlled local clock (TestGetLocalNow):
/// - Boundary entry 23:29 → 23:30 and exit 05:59 → 06:00.
/// - Midnight cross-day: sleep continues across 23:59:59 → 00:00.
/// - Drag interrupts sleep; after release the pet re-enters sleep at night.
/// - Hover/curiosity is suppressed while sleeping; works again in daytime.
/// - Rare-event probe is suppressed while sleeping; the plan stays
///   unconsumed and can fire after waking.
/// - Rest-reminder performance outranks sleep; after the performance
///   completes the pet re-enters sleep on the same tick.
/// - Pause crossing a boundary: the frozen action persists while paused;
///   the first tick after resume re-judges by the then-current local time.
/// </para>
/// </summary>
[TestClass]
public class NightSleepyIntegrationTests
{
    private static readonly DateTime Day1 = new(2026, 11, 16, 12, 0, 0);
    private static readonly DateTime NightStart = new(2026, 11, 16, 23, 30, 0);
    private static readonly DateTime NightJustBefore = new(2026, 11, 16, 23, 29, 0);
    private static readonly DateTime Night2359 = new(2026, 11, 16, 23, 59, 59);
    private static readonly DateTime MidnightNextDay = new(2026, 11, 17, 0, 0, 0);
    private static readonly DateTime Night0559 = new(2026, 11, 17, 5, 59, 0);
    private static readonly DateTime Morning0600 = new(2026, 11, 17, 6, 0, 0);

    private static App GetOrCreateApp()
    {
        return (App?)System.Windows.Application.Current ?? new App();
    }

    private static string GetTempDir(string prefix)
    {
        return System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
    }

    private static MainWindow CreateShownWindow(App app, double timeMs = 0)
    {
        app.TestSetupIsolatedSave(GetTempDir("P4003"));
        app.TestState!.Pets = new List<PetInstanceArchive>
        {
            new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 50, WindowLeft = 100, WindowTop = 100 },
        };
        app.CreatePetWindow(app.TestState!.Pets[0]);
        var w = app.FindWindowByPetId("bear-1");
        Assert.IsNotNull(w);
        w!.Show();
        w.TestGetElapsedMs = () => timeMs;
        return w;
    }

    // ── Boundary entry/exit ────────────────────────────────────────────

    [STATestMethod]
    public void NightEntry_2329To2330_SwitchesToSleep()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.TestGetLocalNow = () => NightJustBefore;
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction, "23:29 is still daytime.");

            w.TestGetLocalNow = () => NightStart;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction, "23:30 (inclusive) must enter sleep.");
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void MidnightCross_SleepContinues()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.TestGetLocalNow = () => Night2359;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            // Cross midnight into the next local day.
            w.TestGetLocalNow = () => MidnightNextDay;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction,
                "Sleep must continue seamlessly across midnight without a transient idle.");

            // Still night later.
            w.TestGetLocalNow = () => Night0559;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void MorningExit_0559To0600_ReturnsToIdle()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.TestGetLocalNow = () => Night0559;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction, "05:59 is still night.");

            w.TestGetLocalNow = () => Morning0600;
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction, "06:00 (exclusive) must wake up.");
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void Daytime_NeverSleeps()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;

            w.TestGetLocalNow = () => Day1;
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction, "Noon must not enter sleep.");
        }
        finally { w.Close(); }
    }

    // ── Drag interruption ──────────────────────────────────────────────

    [STATestMethod]
    public void DragInterrupt_EndDrag_ReentersSleepAtNight()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;
            w.TestGetLocalNow = () => NightStart;

            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            // Real production release path: EndDrag with wasDragging=true
            // plays idle (R9 F-05), releasing the sleep action.
            w.TestSimulateDragRelease(w.Left, w.Top);
            Assert.AreEqual("idle", w.CurrentAction,
                "Drag release must end in idle before the next tick re-judges.");

            // Next tick: night still active → sleep re-enters.
            timeMs = 1000;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction,
                "After the interaction ends, the pet must re-enter sleep at night.");
        }
        finally { w.Close(); }
    }

    // ── Hover suppression ──────────────────────────────────────────────

    [STATestMethod]
    public void Hover_SuppressedWhileSleeping_StillWorksInDaytime()
    {
        var w = new MainWindow();
        try
        {
            double now = 0;
            w.TestGetElapsedMs = () => now;
            // Cursor over the opaque body (same hit point as
            // VisiblePetInteractionTests).
            w.TestGetCursorPos = () => (160, 165);

            // Night: enter sleep, then hover dwell well past 1200ms.
            w.TestGetLocalNow = () => NightStart;
            for (int i = 0; i < 55; i++) { w.OnUpdateTickTest(); now += 33; }
            Assert.AreEqual("sleep", w.CurrentAction,
                "Hover must not trigger curious while the pet is sleeping.");

            // Daytime contrast: the same hover still works.
            w.TestGetLocalNow = () => Day1;
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction);
            for (int i = 0; i < 55; i++) { w.OnUpdateTickTest(); now += 33; }
            Assert.AreEqual("curious", w.CurrentAction,
                "Daytime hover must still trigger curious after waking.");
        }
        finally { w.Close(); }
    }

    // ── Rare-event probe suppression ───────────────────────────────────

    [STATestMethod]
    public void RareEvent_SuppressedWhileSleeping_PlanNotConsumed_FiresAfterWaking()
    {
        var w = new MainWindow();
        try
        {
            double clockMs = 0;
            w.TestGetElapsedMs = () => clockMs;
            w.TestGetLocalNow = () => NightStart;
            w.TestGetCursorPos = () => (900, 900);

            // Fireable scheduler (triggerProbability=1.0). Runtime starts at
            // 0 — below the plan's delay — so the first tick cannot fire.
            var rng = new Random(42);
            var tracker = new EffectiveRuntimeTracker();
            var scheduler = new RareEventScheduler(1.0);
            var today = NightStart.ToString("yyyy-MM-dd");
            tracker.StartActivePeriod(0);
            scheduler.EnsurePlanForDate(today, rng, currentAccumulatedMinutes: tracker.AccumulatedMinutes);
            w.TestRuntimeTracker = tracker;
            w.TestRareEventScheduler = scheduler;

            // Tick 1: pet enters sleep while runtime is still below the
            // plan's delay (Evaluate ran but did not fire).
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            // Tick 2: runtime advances past the delay (150 min exceeds any
            // 20–120 min plan delay) while sleeping — Evaluate is skipped,
            // no probe starts, the plan stays unconsumed.
            clockMs = 150 * 60000.0;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);
            Assert.IsFalse(w.IsProbing, "Probe must not start while sleeping.");
            Assert.IsFalse(scheduler.CurrentPlan?.Consumed == true,
                "Plan must not be consumed while sleeping — Evaluate is delayed.");

            // Wake up past 06:00: first tick exits sleep to idle; the next
            // tick evaluates normally and the probe fires.
            w.TestGetLocalNow = () => Morning0600;
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction);
            w.OnUpdateTickTest();
            Assert.IsTrue(w.IsProbing,
                "After waking, the retained plan must fire the probe.");
            Assert.IsTrue(scheduler.CurrentPlan?.Consumed == true);
        }
        finally { w.Close(); }
    }

    // ── Rest-reminder priority ─────────────────────────────────────────

    [STATestMethod]
    public void RestReminder_OutranksSleep_ReturnsToSleepAfterCompletion()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;
            w.TestGetLocalNow = () => NightStart;

            // Enter sleep first.
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            // The rest-reminder performance is higher priority than sleep.
            w.StartRestReminder(timeMs);
            Assert.AreEqual("stretch", w.CurrentAction,
                "Rest-reminder performance must override the sleep baseline.");

            // Performance runs the rest phase.
            timeMs = 1600;
            w.OnUpdateTickTest();
            Assert.AreEqual("rest", w.CurrentAction);

            // Natural completion at ~30s: back to idle, then the night tick
            // on the SAME tick re-enters sleep.
            timeMs = 30000;
            w.OnUpdateTickTest();
            Assert.IsFalse(w.IsRestReminderActive);
            Assert.AreEqual("sleep", w.CurrentAction,
                "After the performance completes, the pet must re-enter sleep at night.");
        }
        finally { w.Close(); }
    }

    // ── Pause crossing boundaries (App path) ───────────────────────────

    [STATestMethod]
    public void PauseDuringSleep_CrossMorningBoundary_ResumeWakesUp()
    {
        var app = GetOrCreateApp();
        try
        {
            double timeMs = 0;
            var w = CreateShownWindow(app, timeMs);
            w.TestGetLocalNow = () => Night0559;

            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            // Pause at 05:59 while sleeping. The system clock then crosses
            // 06:00 (simulated via TestGetLocalNow) while paused.
            app.OnPauseToggled(null, true);
            w.TestGetLocalNow = () => new DateTime(2026, 11, 17, 6, 30, 0);

            // Even a direct tick while paused must not change the frozen action.
            timeMs = 60000;
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction,
                "While paused the animation is frozen — no boundary re-judgment.");

            // Resume: the first tick re-judges by the then-current local time.
            app.OnPauseToggled(null, false);
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction,
                "After resume past 06:00, the pet must wake up.");
        }
        finally { app.TestCleanup(); }
    }

    [STATestMethod]
    public void PauseBeforeNight_CrossEntryBoundary_ResumeEntersSleep()
    {
        var app = GetOrCreateApp();
        try
        {
            double timeMs = 0;
            var w = CreateShownWindow(app, timeMs);
            w.TestGetLocalNow = () => new DateTime(2026, 11, 16, 23, 0, 0);

            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction);

            // Pause before the night window; local time crosses 23:30 while
            // paused (e.g. lock/screen-sleep).
            app.OnPauseToggled(null, true);
            w.TestGetLocalNow = () => NightStart;

            timeMs = 60000;
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction,
                "While paused the pet must not enter sleep.");

            // Resume after 23:30: the first tick re-judges and enters sleep.
            app.OnPauseToggled(null, false);
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction,
                "After resume at night, the pet must enter sleep.");
        }
        finally { app.TestCleanup(); }
    }

    // ── R1 F-01: interaction must interrupt the sleep animation ────

    // R1 F-01 repro: previously a head press on the sleeping bear accepted
    // the gesture (petting candidate) but the visible animation kept playing
    // "sleep". The accepted gesture must break the sleep action immediately.
    [STATestMethod]
    public void R1F01_HeadPressDuringSleep_InterruptsSleepAnimation_ReentersSleepAfterRelease()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;
            w.TestGetLocalNow = () => NightStart;

            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            // Real production gesture entry with an opaque head point
            // (same head zone point as PettingDetectionTests).
            var headPoint = new System.Windows.Point(60, 20);
            Assert.IsTrue(w.BeginCapturedPointerInteraction(headPoint,
                new System.Windows.Point(w.Left + 60, w.Top + 20)));
            Assert.IsTrue(w.IsPettingCandidate);
            Assert.AreNotEqual("sleep", w.CurrentAction,
                "R1 F-01: An accepted head press must break the sleep animation " +
                "immediately — the screen must not keep playing sleep.");
            Assert.AreEqual("idle", w.CurrentAction);

            // Release the head press; the next night tick must re-enter sleep.
            timeMs = 1000;
            w.EndCapturedPointerInteraction();
            Assert.AreEqual("idle", w.CurrentAction,
                "After the gesture ends the pet is idle before re-judgment.");
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction,
                "Released at night — the pet must return to sleep.");
        }
        finally { w.Close(); }
    }

    [STATestMethod]
    public void R1F01_BodyPressDuringSleep_InterruptsSleepAnimation()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;
            w.TestGetLocalNow = () => NightStart;

            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            // Body press → immediate drag.
            Assert.IsTrue(w.BeginCapturedPointerInteraction(new System.Windows.Point(60, 80),
                new System.Windows.Point(w.Left + 60, w.Top + 80)));
            Assert.IsTrue(w.IsDragging);
            Assert.AreNotEqual("sleep", w.CurrentAction,
                "R1 F-01: A body press starting a drag must break the sleep animation.");

            // Release the drag; night still active → sleep re-enters.
            timeMs = 1000;
            w.EndCapturedPointerInteraction();
            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction,
                "Drag released at night — the pet must return to sleep.");
        }
        finally { w.Close(); }
    }

    // R1 F-01: "松手后按当时的本地时间决定是否回睡" — released after 06:00
    // the pet must NOT go back to sleep even though the press began at night.
    [STATestMethod]
    public void R1F01_HeadPressAtNight_ReleaseAfter0600_StaysAwake()
    {
        var w = new MainWindow();
        try
        {
            double timeMs = 0;
            w.TestGetElapsedMs = () => timeMs;
            w.TestGetLocalNow = () => Night0559;

            w.OnUpdateTickTest();
            Assert.AreEqual("sleep", w.CurrentAction);

            Assert.IsTrue(w.BeginCapturedPointerInteraction(new System.Windows.Point(60, 20),
                new System.Windows.Point(w.Left + 60, w.Top + 20)));
            Assert.AreNotEqual("sleep", w.CurrentAction);

            // The system clock crosses 06:00 while the head is still held.
            timeMs = 1000;
            w.TestGetLocalNow = () => Morning0600;
            w.EndCapturedPointerInteraction();
            w.OnUpdateTickTest();
            Assert.AreEqual("idle", w.CurrentAction,
                "Released after 06:00 — local time decides: no re-sleep.");
        }
        finally { w.Close(); }
    }
}
