using System.Reflection;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Input;
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// P5A-003：窗口栖息正式入口（右键菜单拾取模式 + 主动离开）的 App 级测试。
///
/// 覆盖：
/// - 菜单命令进入拾取模式（与原生菜单同一 BeginInvoke 分发路径）；
/// - 拾取中左键点击合格外部窗口 → 复用 TrySelectExplicit 落位（selected）；
/// - 点中宠物自身取消（cancelled-self，不选自己）；
/// - 不合格目标拒绝并结束拾取（rejected-ineligible）；
/// - 超时取消（cancelled-timeout）；位置所有权被占用兜底取消（cancelled）；
/// - 位置被占用时不进入拾取（rejected-busy）；
/// - 开菜单取消拾取；
/// - Leave Perch 菜单命令 → Detach("user-left")，宠物留在当前位置。
///
/// 伪造句柄的落脚区 WindowFromPoint 必然命中其他根窗口，落位成功用
/// TestLandingOcclusionSampler=(1.0, 空) 强制（仅替换采样来源，
/// 落位/提交/事件路径不变，P5A-002 同款缝）。
/// </summary>
[TestClass]
public class PerchEntryTests
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
                "PerchEntryTests: MainWindow construction failed: {0}", ex);
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

    /// <summary>反射驱动一轮拾取轮询（同步测试不泵消息，确定性执行）。</summary>
    private static void InvokeUpdatePerchPick(MainWindow window, double nowMs)
        => typeof(MainWindow).GetMethod("UpdatePerchPick",
               BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { nowMs });

    /// <summary>反射进入菜单模式（开菜单打断拾取的取消点）。</summary>
    private static void InvokeEnterContextMenuMode(MainWindow window)
        => typeof(MainWindow).GetMethod("EnterContextMenuMode",
               BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);

    /// <summary>泵 Dispatcher 到 Background 优先级，让 BeginInvoke 排队的
    /// 菜单命令（Normal 优先级）先执行完。</summary>
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>非自身进程的合格外部窗口快照（落脚区采样用测试缝替换）。</summary>
    private static ExternalWindowSnapshot FakeSnapshot() => new(
        0x99999, "fake-perch-target", "FakeClass", 5000, "fakeproc",
        true, true, false, false, false, 100, 200, 900, 700);

    [STATestMethod]
    public void PerchPick_MenuEntry_StartsPickModeThenLeftClickSelects()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            window.Left = 1000;
            window.Top = 800;
            // 强制落位可见（伪造句柄落脚区采样只换来源，不换落位路径）。
            window.Perch.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());

            // 1) 正式菜单入口进入拾取模式（原生菜单同一分发路径）。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdPerchOnWindow);
            PumpDispatcher();

            Assert.IsTrue(window.IsPerchPickActive, "菜单命令后必须进入拾取模式。");
            Assert.AreEqual("active", window.LastPerchPickStatus);

            // 2) 左键按下在目标上缘中点（宠物矩形之外）→ 显式选中并落位。
            // 边沿触发：先喂一帧释放，清除进入拾取时的等待标记。
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 400);
            Assert.IsTrue(window.IsPerchPickActive, "释放帧不得结束拾取。");
            window.TestPerchPickInput = () => (true, 500, 202);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();
            var events = new List<PetPerchEvent>();
            window.Perch.PerchEvent += events.Add;

            clock = 500;
            InvokeUpdatePerchPick(window, clock);

            Assert.AreEqual("selected", window.LastPerchPickStatus,
                "点击合格目标必须经正式入口选中。");
            Assert.IsFalse(window.IsPerchPickActive, "任何终态都必须退出拾取模式。");
            Assert.IsTrue(window.Perch.IsActive, "选中后栖息必须激活。");
            Assert.AreEqual(0x99999L, window.Perch.TargetHwndValue);
            Assert.AreEqual(1, events.Count, "选中恰好发一条 landed。");
            Assert.AreEqual("landed", events[0].Type);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_LeftClickOnPetSelf_CancelsWithoutSelecting()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            window.StartPerchPick();
            Assert.IsTrue(window.IsPerchPickActive);

            // 边沿触发：先喂一帧释放。
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 50);
            // 点击点取宠物自身矩形中心（不选自己）。
            var petRect = (PetRectPx)typeof(MainWindow)
                .GetMethod("GetPetRectPhysicalPx", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(window, null)!;
            var cx = (int)(petRect.Left + petRect.Width / 2);
            var cy = (int)(petRect.Top + petRect.Height / 2);
            window.TestPerchPickInput = () => (true, cx, cy);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();

            InvokeUpdatePerchPick(window, 100);

            Assert.AreEqual("cancelled-self", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "点中自身后必须退出拾取。");
            Assert.IsFalse(window.Perch.IsActive, "点中自身不得选中任何目标。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_IneligibleTarget_RejectedAndPickEnds()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            window.StartPerchPick();

            // 边沿触发：先喂一帧释放。
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 50);
            // 点中一个最小化的外部窗口：不合格。
            window.TestPerchPickInput = () => (true, 500, 202);
            window.TestPerchPickSnapshotSource = (_, _) => new ExternalWindowSnapshot(
                0x77777, "minimized-target", "FakeClass", 5000, "fakeproc",
                true, true, true, false, false, 100, 200, 900, 700);

            InvokeUpdatePerchPick(window, 100);

            Assert.AreEqual("rejected-ineligible", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "拒绝后必须结束拾取。");
            Assert.IsFalse(window.Perch.IsActive, "不合格目标不得被选中。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_Timeout_Cancels()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            window.TestPerchPickTimeoutMs = 1000;
            window.StartPerchPick();
            window.TestPerchPickInput = () => (false, 0, 0); // 未按下：等待中

            InvokeUpdatePerchPick(window, 500);
            Assert.IsTrue(window.IsPerchPickActive, "超时前拾取必须保持。");

            InvokeUpdatePerchPick(window, 1500);
            Assert.AreEqual("cancelled-timeout", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "超时后必须退出拾取。");
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_PositionOwnershipLostWhilePicking_Cancels()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            window.StartPerchPick();

            // 拾取进行中位置被占用（如物理活动启动）：兜底取消。
            SetField(window, "_isPhysicsActive", true);
            InvokeUpdatePerchPick(window, 100);

            Assert.AreEqual("cancelled", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_EntryWhileBusy_DoesNotEnterPickMode()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            SetField(window, "_isPhysicsActive", true);
            window.StartPerchPick();

            Assert.AreEqual("rejected-busy", window.LastPerchPickStatus,
                "位置被占用时不得进入拾取模式。");
            Assert.IsFalse(window.IsPerchPickActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_OpeningMenu_CancelsPick()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            window.StartPerchPick();

            InvokeEnterContextMenuMode(window);

            Assert.AreEqual("cancelled", window.LastPerchPickStatus);
            Assert.IsFalse(window.IsPerchPickActive, "开菜单必须取消拾取。");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// P5A-003 R1 F-01：暂停入口必须同步结束拾取（计时器停止后
    /// UpdatePerchPick 的暂停兜底永远没有机会执行），恢复后不得续接
    /// 旧拾取。PauseAnimation 是托盘 Pause 与菜单 Pause/Resume 共用的
    /// 应用级暂停入口。
    /// </summary>
    [STATestMethod]
    public void PerchPick_PauseDuringPick_CancelsImmediately_ResumeDoesNotResume()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            double clock = 0;
            window.TestGetElapsedMs = () => clock;
            window.StartPerchPick();
            // 拾取进行中：按住左键处于边沿触发等待段（监理反例同状态）。
            window.TestPerchPickInput = () => (true, 500, 202);
            InvokeUpdatePerchPick(window, 100);
            Assert.IsTrue(window.IsPerchPickActive, "前置：暂停前拾取必须仍激活。");

            // 暂停：必须同步取消，不能依赖已停止的 tick。
            window.PauseAnimation();

            Assert.AreEqual("cancelled", window.LastPerchPickStatus,
                "暂停入口必须同步结束拾取。");
            Assert.IsFalse(window.IsPerchPickActive, "暂停后拾取不得保留。");
            Assert.IsFalse(window.IsPerchPickAwaitingRelease, "暂停必须清除边沿触发等待标记。");

            // 恢复：不续接旧拾取（即使左键仍被"按住"）。
            clock = 5000;
            window.ResumeAnimation();
            window.TestPerchPickInput = () => (true, 500, 202);
            InvokeUpdatePerchPick(window, 5100);
            Assert.IsFalse(window.IsPerchPickActive, "恢复后不得续接暂停前的旧拾取。");
            Assert.IsFalse(window.Perch.IsActive, "恢复后旧拾取不得选中任何目标。");

            // 回归：恢复后可以重新进入拾取并正常选择（边沿触发不受污染）。
            window.TestPerchPickInput = () => (false, 0, 0);
            window.StartPerchPick();
            Assert.IsTrue(window.IsPerchPickActive, "恢复后必须允许重新进入拾取。");
            Assert.AreEqual("active", window.LastPerchPickStatus);
            Assert.IsTrue(window.IsPerchPickAwaitingRelease, "重新进入必须重新等待释放。");
            InvokeUpdatePerchPick(window, 5200);
            Assert.IsTrue(window.IsPerchPickActive, "释放帧不得结束拾取。");
            window.Perch.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());
            window.TestPerchPickInput = () => (true, 500, 202);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();
            InvokeUpdatePerchPick(window, 5300);
            Assert.AreEqual("selected", window.LastPerchPickStatus,
                "恢复后的新拾取必须正常选择。");
            Assert.IsTrue(window.Perch.IsActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void PerchPick_EntryWhileLeftButtonDown_IgnoresHeldButtonUntilRelease()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            window.TestGetElapsedMs = () => 0;
            // 强制落位可见（伪造句柄落脚区采样只换来源，不换落位路径）。
            window.Perch.TestLandingOcclusionSampler = (_, _, _) => (1.0, new List<string>());
            window.StartPerchPick();

            // 进入拾取时左键已被按住：不得把"已按下的键"当作新点击。
            window.TestPerchPickInput = () => (true, 500, 202);
            window.TestPerchPickSnapshotSource = (_, _) => FakeSnapshot();
            InvokeUpdatePerchPick(window, 100);

            Assert.IsTrue(window.IsPerchPickActive, "按住左键进入拾取不得立即选择。");
            Assert.AreEqual("active", window.LastPerchPickStatus);
            Assert.IsFalse(window.Perch.IsActive, "残留按下不得选中任何目标。");

            // 释放后新的按下才构成选择。
            window.TestPerchPickInput = () => (false, 0, 0);
            InvokeUpdatePerchPick(window, 150);
            Assert.IsTrue(window.IsPerchPickActive, "释放帧不得结束拾取。");

            window.TestPerchPickInput = () => (true, 500, 202);
            InvokeUpdatePerchPick(window, 200);

            Assert.AreEqual("selected", window.LastPerchPickStatus,
                "释放后的新按下必须正常选择。");
            Assert.IsFalse(window.IsPerchPickActive);
            Assert.IsTrue(window.Perch.IsActive);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void LeavePerch_MenuEntry_DetachesWithUserLeft_PetStaysPut()
    {
        var window = TryCreateMainWindow();
        if (window is null) { Assert.Inconclusive("MainWindow requires character assets; skipping."); return; }
        try
        {
            // 构造激活中的栖息绑定（真实落位需真实外部目标，反射构造同
            // WindowPerchOwnershipTests 夹具模式）。
            var perch = window.Perch;
            var events = new List<PetPerchEvent>();
            perch.PerchEvent += events.Add;
            var tracker = (PerchTargetTracker)GetField(perch, "_tracker")!;
            Assert.IsTrue(tracker.Select(FakeSnapshot(), (uint)Environment.ProcessId));
            typeof(PetWindowPerch).GetProperty("IsActive")!.SetValue(perch, true);
            var timer = (DispatcherTimer)GetField(perch, "_timer")!;
            timer.Start();

            // 正式菜单入口：Leave Perch（与原生菜单同一分发路径）。
            // EnterContextMenuMode → EndDrag(cancellation) 会执行一次
            // ClampWindowToWorkArea。测试窗口无 HWND（未 Show），源 DPI
            // 回落 1.0 而目标显示器 DPI 是真实值（如 144DPI=1.5），钳制
            // 管线在源/目标 DPI 不一致时会平移窗口（真实应用 HWND 存在，
            // 源=目标 DPI，钳制是恒等操作）。Detach 命令是 BeginInvoke
            // 排队的，菜单生命周期返回时尚未执行——在移动之后、脱离
            // 之前取基线，断言只考察 detach(user-left) 本身不移动宠物。
            window.TestOpenContextMenuLifecycle((int)MainWindow.MenuIdLeavePerch);
            Assert.IsTrue(perch.IsActive, "菜单生命周期不得提前脱离。");
            var leftAtLeave = window.Left;
            var topAtLeave = window.Top;
            PumpDispatcher();

            Assert.IsFalse(perch.IsActive, "主动离开必须解除栖息绑定。");
            Assert.IsNull(perch.TargetHwndValue);
            Assert.IsFalse(timer.IsEnabled, "主动离开必须停止轮询。");
            Assert.AreEqual(1, events.Count, "主动离开恰好发一条 detached。");
            Assert.AreEqual("detached", events[0].Type);
            Assert.AreEqual("user-left", events[0].Reason);
            Assert.AreEqual(leftAtLeave, window.Left, 0.001, "主动离开宠物留在当前位置。");
            Assert.AreEqual(topAtLeave, window.Top, 0.001, "主动离开宠物留在当前位置。");
        }
        finally { window.Close(); }
    }
}
