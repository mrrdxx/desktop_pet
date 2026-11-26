using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopPet.App.Platform;

/// <summary>
/// P6-004（第一小步）：主动气泡展示窗口。
/// <para>
/// 独立透明置顶窗口：不激活（<c>ShowActivated=False</c> +
/// <c>WS_EX_NOACTIVATE</c>）、不占任务栏（<c>ShowInTaskbar=False</c> +
/// <c>WS_EX_TOOLWINDOW</c>），只承载一条统计依据文案和一个关闭按钮。它不移动
/// 宠物、不写宠物位置，也不拦截宠物窗口的拖动与透明区命中——气泡放在宠物矩形
/// 外侧，不与宠物重叠。
/// </para>
/// <para>
/// 生命周期：约 <see cref="DefaultAutoHideMs"/> 毫秒后自动隐藏；点击关闭按钮只
/// 关闭气泡本身。窗口实例由 App 复用，隐藏而不是销毁，避免反复创建 HWND。
/// </para>
/// </summary>
public partial class StatsBubbleWindow : Window
{
    /// <summary>保守初值：自动消失前的展示时长（约 8 秒）。</summary>
    internal const double DefaultAutoHideMs = 8000.0;

    /// <summary>气泡与宠物窗口之间保留的间隙（DIP）。</summary>
    internal const double GapDip = 8.0;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly DispatcherTimer _autoHideTimer;
    private bool _autoHides;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public StatsBubbleWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DefaultAutoHideMs)
        };
        _autoHideTimer.Tick += OnAutoHideTick;
    }

    /// <summary>
    /// 展示一条气泡文案，放在宠物矩形外侧并夹到工作区内。
    /// <para>
    /// 先以屏幕外坐标 Show 一次完成布局测量，再按实测尺寸定位，避免可见跳动。
    /// <paramref name="autoHide"/> 为 false 时不计时（测试/预览用）。
    /// </para>
    /// </summary>
    /// <param name="message">只表达统计依据的文案。</param>
    /// <param name="petRectDip">宠物窗口矩形（DIP）。</param>
    /// <param name="workAreaDip">宠物所在显示器工作区（DIP）。</param>
    /// <param name="autoHide">是否按默认时长自动隐藏。</param>
    internal void ShowNear(string message, Rect petRectDip, Rect workAreaDip, bool autoHide = true)
    {
        MessageText.Text = message;
        _autoHides = autoHide;

        // 先离屏显示完成测量，再定位，避免在 (0,0) 闪一下。
        Left = -10000;
        Top = -10000;
        if (!IsVisible)
            Show();
        UpdateLayout();

        var (left, top) = ComputePlacement(petRectDip, workAreaDip);
        Left = left;
        Top = top;

        _autoHideTimer.Stop();
        if (_autoHides)
            _autoHideTimer.Start();
    }

    /// <summary>当前气泡文案（供测试与预览断言）。</summary>
    internal string CurrentMessage => MessageText.Text;

    /// <summary>立即隐藏气泡并停止自动消失计时。</summary>
    internal void HideBubble()
    {
        _autoHideTimer.Stop();
        if (IsVisible)
            Hide();
    }

    /// <summary>
    /// 计算不与宠物矩形重叠的放置位置：优先宠物左侧，其次右侧，再次上方，
    /// 最后下方，并夹到工作区内。
    /// </summary>
    internal (double Left, double Top) ComputePlacement(Rect petRectDip, Rect workAreaDip)
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 80.0;

        // 优先放在宠物左侧或右侧（完全不与宠物矩形重叠）；两侧都放不下
        // 时退回宠物上方或下方，横向居中。
        double left;
        double top;
        if (petRectDip.Left - GapDip - width >= workAreaDip.Left)
        {
            left = petRectDip.Left - GapDip - width;
            top = petRectDip.Top + (petRectDip.Height - height) / 2.0;
        }
        else if (petRectDip.Right + GapDip + width <= workAreaDip.Right)
        {
            left = petRectDip.Right + GapDip;
            top = petRectDip.Top + (petRectDip.Height - height) / 2.0;
        }
        else
        {
            left = petRectDip.Left + (petRectDip.Width - width) / 2.0;
            var above = petRectDip.Top - GapDip - height;
            top = above >= workAreaDip.Top ? above : petRectDip.Bottom + GapDip;
        }

        // 夹到工作区，确保整个气泡可见。
        if (left < workAreaDip.Left) left = workAreaDip.Left;
        if (left + width > workAreaDip.Right) left = workAreaDip.Right - width;
        if (top < workAreaDip.Top) top = workAreaDip.Top;
        if (top + height > workAreaDip.Bottom) top = workAreaDip.Bottom - height;
        if (left < workAreaDip.Left) left = workAreaDip.Left;
        if (top < workAreaDip.Top) top = workAreaDip.Top;

        return (left, top);
    }

    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        HideBubble();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideBubble();
    }

    private void StatsBubbleWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // 不抢焦点、不进 Alt-Tab：与宠物窗口同样的无激活工具窗口样式。
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }
}
