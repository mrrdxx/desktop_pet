using System.Runtime.CompilerServices;
using DesktopPet.App;

namespace DesktopPet.App.Tests;

// 套件级时间固定（P5B-001 首次夜间运行发现）：P4-003 夜间窗口
// （23:30–06:00）会把 idle 基线切换为 sleep 并抑制好奇/稀有事件/
// 休息提醒，导致未固定时间的旧 App 测试只在白天全绿。程序集加载时
// 把套件默认本地时间固定为白天正午；夜间行为专项测试用实例级
// MainWindow.TestGetLocalNow 覆盖（优先级更高）。
internal static class TestSuiteTime
{
    private static readonly DateTime PinnedDaytime =
        new(2026, 1, 15, 12, 0, 0, DateTimeKind.Local);

    [ModuleInitializer]
    internal static void PinSuiteLocalNow()
        => MainWindow.TestSuiteLocalNowDefault = () => PinnedDaytime;
}
