using System.Reflection;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P5A-001 R1 整改：栖息位置所有权（F-02）与关闭/退出清理（F-03）的
/// App 级反例测试。
///
/// F-02 覆盖：
/// - 暂停及物理活动中显式选中拒绝落位（不报 landed、不移动、不激活）；
/// - ApplyPerchFoot 受守卫位置写入：被占用返回 false 且不移动，
///   空闲时返回 true 且提交位置（位置只由一个行为提交）；
/// - IsPerchWriteAllowed 守卫矩阵（拖动/抚摸/暂停/物理/探头/社交）。
///
/// F-03 覆盖：
/// - 窗口关闭（OnClosed）停止栖息轮询计时器并解除绑定；
/// - 退出保存前 RestoreProbePositionForExit 把窗口移回栖息前位置
///   （存档坐标=休息位，非临时栖息位），并停用栖息。
///
/// 栖息轮询计时器是 DispatcherTimer：同步测试代码运行在 UI 线程上
/// 不泵消息，tick 不会在断言前触发，可安全启动后断言 OnClosed 停止。
/// </summary>
[TestClass]
public class WindowPerchOwnershipTests
{
    private static MainWindow? TryCreateMainWindow()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                "WindowPerchOwnershipTests: MainWindow construction failed: {0}", ex);
            return null;
        }
    }

    private static void SetField(object obj, string name, object value)
        => GetFieldInfo(obj.GetType(), name)!.SetValue(obj, value);

    private static object? GetField(object obj, string name)
        => GetFieldInfo(obj.GetType(), name)?.GetValue(obj);

    private static FieldInfo? GetFieldInfo(Type type, string name)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f is not null) return f;
        }
        return null;
    }

    /// <summary>非自身进程的合格外部窗口快照（守卫拒绝路径不触达 Win32）。</summary>
    private static ExternalWindowSnapshot FakeSnapshot() => new(
        0x99999, "fake-perch-target", "FakeClass", 5000, "fakeproc",
        true, true, false, false, false, 100, 200, 900, 700);

    [STATestMethod]
    public void TrySelectExplicit_WhilePaused_RejectedNoLandedNoMove()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.PauseAnimation();
            var leftBefore = window.Left;
            var topBefore = window.Top;
            var events = new List<PetPerchEvent>();
            window.Perch.PerchEvent += events.Add;

            var selected = window.Perch.TrySelectExplicit(FakeSnapshot(), selfProcessId: (uint)Environment.ProcessId);

            Assert.IsFalse(selected, "暂停中显式选中必须被拒绝。");
            Assert.IsFalse(window.Perch.IsActive, "拒绝后不得激活栖息。");
            Assert.AreEqual(0, events.Count, "拒绝不得报告 landed/任何事件。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "拒绝不得移动窗口。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "拒绝不得移动窗口。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void TrySelectExplicit_WhilePhysicsActive_RejectedNoLandedNoMove()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            SetField(window, "_isPhysicsActive", true);
            var leftBefore = window.Left;
            var topBefore = window.Top;
            var events = new List<PetPerchEvent>();
            window.Perch.PerchEvent += events.Add;

            var selected = window.Perch.TrySelectExplicit(FakeSnapshot(), selfProcessId: (uint)Environment.ProcessId);

            Assert.IsFalse(selected, "物理活动期间显式选中必须被拒绝。");
            Assert.IsFalse(window.Perch.IsActive, "拒绝后不得激活栖息。");
            Assert.AreEqual(0, events.Count, "拒绝不得报告 landed/任何事件。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "拒绝不得移动窗口。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "拒绝不得移动窗口。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ApplyPerchFoot_Free_ReturnsTrueAndMoves()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var before = (window.Left, window.Top);
            var moved = window.ApplyPerchFoot(new Core.Geometry.PetWorldPosition(1234, 1000));
            Assert.IsTrue(moved, "空闲时栖息位置提交必须成功（单一写入者=栖息）。");
            Assert.AreNotEqual(before.Item2, window.Top, 0.001, "提交后窗口必须移动到落脚点换算位置。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ApplyPerchFoot_WhilePhysicsActive_ReturnsFalseNoMove()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            SetField(window, "_isPhysicsActive", true);
            var before = (window.Left, window.Top);
            var moved = window.ApplyPerchFoot(new Core.Geometry.PetWorldPosition(1234, 1000));
            Assert.IsFalse(moved, "物理活动占用位置时栖息不得提交。");
            Assert.AreEqual(before.Item1, window.Left, 0.001);
            Assert.AreEqual(before.Item2, window.Top, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void ApplyPerchFoot_WhilePaused_ReturnsFalseNoMove()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.PauseAnimation();
            var before = (window.Left, window.Top);
            var moved = window.ApplyPerchFoot(new Core.Geometry.PetWorldPosition(1234, 1000));
            Assert.IsFalse(moved, "暂停时栖息不得提交。");
            Assert.AreEqual(before.Item1, window.Left, 0.001);
            Assert.AreEqual(before.Item2, window.Top, 0.001);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void IsPerchWriteAllowed_GuardMatrix()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            Assert.IsTrue(window.IsPerchWriteAllowed, "无占用时栖息写入必须被允许。");
            foreach (var flag in new[] { "_isDragging", "_isPettingCandidate", "_isPhysicsActive",
                                          "_isProbing", "_isSocialActive" })
            {
                SetField(window, flag, true);
                Assert.IsFalse(window.IsPerchWriteAllowed, $"{flag}=true 必须拒绝栖息写入。");
                SetField(window, flag, false);
                Assert.IsTrue(window.IsPerchWriteAllowed, $"{flag}=false 后必须恢复允许。");
            }
            window.PauseAnimation();
            Assert.IsFalse(window.IsPerchWriteAllowed, "暂停必须拒绝栖息写入。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void OnClosed_StopsPerchPollingAndDetaches()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;
            // 模拟栖息活跃：启动轮询计时器并置 IsActive（无真实外部目标，
            // 同步测试不泵消息，tick 不会在 Close 前触发）。
            var timer = (DispatcherTimer)GetField(perch, "_timer")!;
            timer.Start();
            Assert.IsTrue(timer.IsEnabled);
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);

            window.Close();

            Assert.IsFalse(timer.IsEnabled, "窗口关闭必须停止栖息轮询计时器。");
            Assert.IsFalse(perch.IsActive, "窗口关闭必须解除栖息绑定。");
            Assert.AreEqual(1, events.Count, "关闭应恰好发一条 detached 事件。");
            Assert.AreEqual("detached", events[0].Type);
            Assert.AreEqual("closed", events[0].Reason);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RestoreProbePositionForExit_RestoresPrePerchPositionAndDeactivates()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;
            window.Left = 222;
            window.Top = 333;
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            SetField(perch, "_hasPrePerchPosition", true);
            SetField(perch, "_prePerchLeft", 100.0);
            SetField(perch, "_prePerchTop", 120.0);
            var timer = (DispatcherTimer)GetField(perch, "_timer")!;
            timer.Start();

            window.RestoreProbePositionForExit();

            Assert.IsFalse(perch.IsActive, "退出保存前必须停用栖息。");
            Assert.IsFalse(timer.IsEnabled, "退出保存前必须停止栖息轮询。");
            Assert.AreEqual(100.0, window.Left, 0.001, "退出保存坐标必须是栖息前位置（休息位）。");
            Assert.AreEqual(120.0, window.Top, 0.001, "退出保存坐标必须是栖息前位置（休息位）。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RestoreProbePositionForExit_InactivePerch_DoesNotMoveWindow()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.Left = 222;
            window.Top = 333;
            window.RestoreProbePositionForExit();
            Assert.AreEqual(222.0, window.Left, 0.001, "栖息未激活时退出恢复不得移动窗口。");
            Assert.AreEqual(333.0, window.Top, 0.001, "栖息未激活时退出恢复不得移动窗口。");
            Assert.IsFalse(window.Perch.IsActive);
        }
        finally { window.Close(); }
    }

    // ---------- P5A-001 R2 剩余项补证 ----------

    /// <summary>反射驱动一轮栖息轮询（同步测试不泵消息，确定性执行）。</summary>
    private static void InvokeOnTick(PetWindowPerch perch)
        => typeof(PetWindowPerch).GetMethod("OnTick",
               BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(perch, null);

    /// <summary>伪造合格目标 B：落脚区（外框上缘）永远不是 B 的根窗口——
    /// 对伪造句柄 WindowFromPoint 必然命中其他根窗口，构造"落脚处已被
    /// 遮挡"的确定性场景。</summary>
    private static ExternalWindowSnapshot FakeCoveredTarget(long hwndValue = 0x88888)
        => new(hwndValue, "fake-covered-target", "FakeClass", 5100, "fakecovered",
            true, true, false, false, false, 100, 800, 900, 1500);

    [STATestMethod]
    public void TrySelectExplicit_LandingZoneCovered_RejectedWithZeroDisplacement()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var events = new List<PetPerchEvent>();
            window.Perch.PerchEvent += events.Add;
            var leftBefore = window.Left;
            var topBefore = window.Top;

            // F-04(a)：落脚处已被遮挡时初选拒绝（先校验后写入，零位移）。
            var selected = window.Perch.TrySelectExplicit(
                FakeCoveredTarget(), selfProcessId: (uint)Environment.ProcessId);

            Assert.IsFalse(selected, "落脚处已被遮挡时初选必须拒绝。");
            Assert.IsFalse(window.Perch.IsActive, "拒绝后不得激活。");
            Assert.AreEqual(0, events.Count, "拒绝不得报告 landed/任何事件。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "初选拒绝零位移。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "初选拒绝零位移。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void TrySelectExplicit_ReselectFails_PreservesOldBinding_NextPollConsistent()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;

            // 旧绑定：目标 A（伪造合格快照，反射构造）。
            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId),
                "测试前提：旧目标 A 必须可被跟踪。");
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            var timer = (DispatcherTimer)GetField(perch, "_timer")!;
            timer.Start();
            var leftBefore = window.Left;
            var topBefore = window.Top;

            // F-02 反例：已有绑定时重选一个合格但落脚处被占的目标——
            // 必须失败，且保留旧绑定（不清掉旧跟踪器）。
            var selected = perch.TrySelectExplicit(
                FakeCoveredTarget(), selfProcessId: (uint)Environment.ProcessId);

            Assert.IsFalse(selected, "落脚处被占时重选必须失败。");
            Assert.AreEqual(0, events.Count, "重选失败不得发任何事件。");
            Assert.IsTrue(perch.IsActive, "重选失败必须保留旧绑定。");
            Assert.AreEqual(0x99999L, perch.TargetHwndValue, "绑定必须仍是原目标 A。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "重选失败不得移动窗口。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "重选失败不得移动窗口。");

            // 失败后继续推进：下一轮轮询读到旧绑定 A（非空目标），
            // 伪造句柄已销毁 → 一致清除（无空引用、状态一致）。
            InvokeOnTick(perch);

            Assert.IsFalse(perch.IsActive, "轮询伪造句柄后必须脱离。");
            Assert.IsFalse(timer.IsEnabled, "脱离必须停止轮询计时器。");
            Assert.AreEqual(1, events.Count, "清除恰好发一条 detached。");
            Assert.AreEqual("detached", events[0].Type);
            Assert.AreEqual("WindowGone", events[0].Reason);

            // 再推进一轮：未激活早退，无异常、无新事件。
            InvokeOnTick(perch);
            Assert.AreEqual(1, events.Count, "脱离后轮询不得再发事件。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void RecoverPet_ReleasesPerchBinding_NoRewriteAfterwards()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;

            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId),
                "测试前提：旧目标 A 必须可被跟踪。");
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            var timer = (DispatcherTimer)GetField(perch, "_timer")!;
            timer.Start();

            window.RecoverPet();

            Assert.IsFalse(perch.IsActive, "找回必须解除栖息绑定。");
            Assert.IsNull(perch.TargetHwndValue, "找回后不得保留目标绑定。");
            Assert.IsFalse(timer.IsEnabled, "找回必须停止栖息轮询。");
            Assert.AreEqual(1, events.Count, "找回恰好发一条 detached。");
            Assert.AreEqual("detached", events[0].Type);
            Assert.AreEqual("recovered", events[0].Reason);

            // 找回后目标再移动：绑定已解除，轮询零写入。
            var leftAfter = window.Left;
            var topAfter = window.Top;
            InvokeOnTick(perch);
            Assert.AreEqual(leftAfter, window.Left, 0.001, "找回后目标移动不得再写位置。");
            Assert.AreEqual(topAfter, window.Top, 0.001, "找回后目标移动不得再写位置。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void FollowTowardOccludedLanding_RejectsBeforeWrite()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;

            // 目标 A 已绑定，随后移动到落脚处被占的新位置（伪造句柄：
            // 新落脚区 WindowFromPoint 永远命中其他根窗口）。
            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeCoveredTarget(0x99999), (uint)Environment.ProcessId),
                "测试前提：移动后的目标必须可被跟踪。");
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            var leftBefore = window.Left;
            var topBefore = window.Top;

            // F-04(b)：跟随提交——先遮挡校验、后写入，被遮挡时拒绝。
            var result = (PerchLandingResult)typeof(PetWindowPerch)
                .GetMethod("TryApplyLanding", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(perch, new object[] { "follow" })!;

            Assert.AreEqual(PerchLandingResult.Occluded, result,
                "目标移向被遮挡落脚处时跟随提交必须被遮挡校验拒绝。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "跟随不得先写入被遮挡位置（零写入）。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "跟随不得先写入被遮挡位置（零写入）。");
            Assert.AreEqual(0, events.Count, "拒绝不得报告 follow。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void Perch_ExitArchiveRoundTrip_SavesPrePerchPosition_NewWindowNoOldBinding()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        string? dir = null;
        try
        {
            dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"P5A_R2F03_{Guid.NewGuid():N}");
            app.TestSetupIsolatedSave(dir);
            app.TestState!.Pets = new List<PetInstanceArchive>
            {
                new() { PetId = "bear-1", Personality = Personality.Proactive, Grievance = 40,
                        WindowLeft = 222, WindowTop = 333 },
            };

            app.CreatePetWindow(app.TestState.Pets[0]);
            var w = app.FindWindowByPetId("bear-1")!;

            // 栖息激活（反射构造绑定+栖息前位置，真实落位需真实外部目标）；
            // 窗口移到临时栖息位（非存档值）。
            var perch = w.Perch;
            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId));
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            SetField(perch, "_hasPrePerchPosition", true);
            SetField(perch, "_prePerchLeft", 222.0);
            SetField(perch, "_prePerchTop", 333.0);
            ((DispatcherTimer)GetField(perch, "_timer")!).Start();
            w.Left = 555;
            w.Top = 66;

            // 正常退出（真实退出保存链路：RestoreProbePositionForExit →
            // MarkStateDirty → SaveCoordinator 落盘）。
            app.ShutdownMainWindowForTest();

            // 重载存档：必须保存栖息前坐标（休息位），非临时栖息位。
            var archive = new ArchiveService(dir);
            var (restored, _, _) = archive.LoadState();
            Assert.AreEqual(1, restored.Pets.Count, "退出必须保留宠物。");
            Assert.AreEqual(222.0, restored.Pets[0]!.WindowLeft, 0.001,
                "落盘坐标必须是栖息前位置。");
            Assert.AreEqual(333.0, restored.Pets[0]!.WindowTop, 0.001,
                "落盘坐标必须是栖息前位置。");

            // 新窗口从重载状态创建：不续跑旧绑定。
            app.CreatePetWindow(restored.Pets[0]!);
            var w2 = app.FindWindowByPetId("bear-1")!;
            var perch2 = w2.Perch;
            Assert.IsFalse(perch2.IsActive, "新窗口不得续跑旧绑定。");
            Assert.IsNull(perch2.TargetHwndValue, "新窗口不得持有旧目标。");
            Assert.IsFalse(((DispatcherTimer)GetField(perch2, "_timer")!).IsEnabled,
                "新窗口栖息轮询必须未启动。");

            // 驱动一轮轮询：零写入（绑定只在内存，重启后不存在）。
            var left2 = w2.Left;
            var top2 = w2.Top;
            InvokeOnTick(perch2);
            Assert.AreEqual(left2, w2.Left, 0.001, "新窗口轮询不得写位置。");
            Assert.AreEqual(top2, w2.Top, 0.001, "新窗口轮询不得写位置。");
        }
        finally
        {
            try { app.TestCleanup(); }
            catch { /* already cleaned by shutdown */ }
        }
    }

    // ---------- P5A-001 R3 收尾（F-02 共享所有权 / F-04 生产轮询） ----------

    [STATestMethod]
    public void PerchAndSocial_SharedOwnership_OnlyApprovedPathWrites()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;

            // 栖息持有：绑定目标 A（伪造合格快照，反射构造），无核心冲突。
            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId));
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);

            // 同一只宠物同时收到栖息与社交两种位移请求：栖息获准、社交让出。
            Assert.IsFalse(window.IsSocialPositionWriteAllowed,
                "栖息绑定持有时社交步进不得写位置。");
            Assert.IsTrue(window.IsPerchWriteAllowed,
                "栖息持有且无核心冲突时栖息写入获准。");
            Assert.IsTrue(window.ApplyPerchFoot(new Core.Geometry.PetWorldPosition(1234, 1000)),
                "栖息是唯一获准路径，位移必须提交成功。");

            // 社交持有（栖息已让出）：社交步进获准、栖息位移被拒。
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, false);
            SetField(window, "_isSocialActive", true);
            Assert.IsTrue(window.IsSocialPositionWriteAllowed,
                "社交持有（栖息已让出）时社交步进获准写入。");
            Assert.IsFalse(window.ApplyPerchFoot(new Core.Geometry.PetWorldPosition(4321, 1000)),
                "社交持有时栖息位移必须被拒——只有获准路径写入。");

            // 核心冲突（拖动/抚摸/暂停/物理/探头）时双方都拒。
            SetField(window, "_isSocialActive", false);
            SetField(window, "_isDragging", true);
            Assert.IsFalse(window.IsSocialPositionWriteAllowed, "核心冲突时社交位移拒绝。");
            Assert.IsFalse(window.IsPerchWriteAllowed, "核心冲突时栖息位移拒绝。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void OnTick_TargetMovesToOccludedSpot_PetStaysPut_Detaches()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;

            // 目标 A 已绑定（旧外框 100,200,900,700）。
            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId),
                "测试前提：目标 A 必须可被跟踪。");
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            var timer = (DispatcherTimer)GetField(perch, "_timer")!;
            timer.Start();

            // 跟随基线=旧外框；生产轮询（OnTick）经测试缝读到目标 A
            // 移动后的新外框（身份不变：同句柄/标题/类/进程，仅 bounds
            // 变化；伪造句柄：新落脚区 WindowFromPoint 永远命中其他根
            // 窗口→确定性 Occluded）。
            ((PerchUpdateGate)GetField(perch, "_updateGate")!).Reset(100, 200, 900, 700);
            perch.TestSnapshotSource = _ => new ExternalWindowSnapshot(
                0x99999, "fake-perch-target", "FakeClass", 5000, "fakeproc",
                true, true, false, false, false, 100, 800, 900, 1500);

            var leftBefore = window.Left;
            var topBefore = window.Top;

            // F-04 补证：完整走生产轮询路径 Observe→gate→TryApplyLanding
            // ("follow")→Occluded→occluded-detach+解除绑定。
            InvokeOnTick(perch);

            Assert.AreEqual(0, events.Count(e => e.Type == "follow"),
                "被遮挡时不得报告 follow。");
            Assert.AreEqual(1, events.Count(e => e.Type == "occluded-detach"),
                "必须恰好发一条 occluded-detach。");
            Assert.IsFalse(perch.IsActive, "遮挡脱离必须解除栖息绑定。");
            Assert.IsNull(perch.TargetHwndValue, "脱离后不得保留目标绑定。");
            Assert.IsFalse(timer.IsEnabled, "遮挡脱离必须停止栖息轮询计时器。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "小熊必须保持原位。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "小熊必须保持原位。");
        }
        finally { window.Close(); }
    }

    // ---------- P5A-003 R1 F-02：右键菜单打开期间挂起稳态遮挡判定 ----------

    [STATestMethod]
    public void OnTick_ContextMenuOpen_SteadyOcclusionSuspended_CloseResumes()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            var perch = window.Perch;
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;
            var leftBefore = window.Left;
            var topBefore = window.Top;

            // 目标已绑定且外框未变（跟随基线==当前外框）→ OnTick 直达
            // 稳态遮挡分支；伪句柄 → WindowFromPoint 永远命中其他根窗口，
            // 采样确定性全遮挡。
            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId));
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            ((PerchUpdateGate)GetField(perch, "_updateGate")!).Reset(100, 200, 900, 700);
            perch.TestSnapshotSource = _ => FakeSnapshot();
            SetField(perch, "_lastFootX", 500.0);
            SetField(perch, "_lastPetWidthPx", 180.0);

            // 菜单打开（EnterContextMenuMode→_isContextMenuOpen=true）：
            // 稳态遮挡判定挂起——自家菜单短暂盖住落脚区不得脱离，
            // 否则栖息中右键选「Leave Perch」会被先行 occluded-detach
            //（M-16 实机实测 fraction=0.44）。
            SetField(window, "_isContextMenuOpen", true);
            InvokeOnTick(perch);
            Assert.AreEqual(0, events.Count(e => e.Type == "occluded-detach"),
                "菜单打开期间不得因菜单自身遮挡而脱离。");
            Assert.IsTrue(perch.IsActive, "菜单打开期间必须保持栖息绑定。");
            Assert.AreEqual(leftBefore, window.Left, 0.001, "菜单打开期间不得移动。");
            Assert.AreEqual(topBefore, window.Top, 0.001, "菜单打开期间不得移动。");

            // 菜单关闭（ExitContextMenuMode→false）：同一场景下一轮
            // 轮询恢复遮挡判定 → 仍按原规格 occluded-detach。
            SetField(window, "_isContextMenuOpen", false);
            InvokeOnTick(perch);
            Assert.AreEqual(1, events.Count(e => e.Type == "occluded-detach"),
                "菜单关闭后遮挡判定必须恢复并脱离。");
            Assert.IsFalse(perch.IsActive, "恢复判定后的遮挡脱离必须解除绑定。");
        }
        finally { window.Close(); }
    }
}
