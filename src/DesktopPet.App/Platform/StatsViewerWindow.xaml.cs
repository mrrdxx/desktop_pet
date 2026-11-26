using System.Windows;

namespace DesktopPet.App.Platform;

/// <summary>
/// P6-004（第一小步）：只读统计查看窗的数据快照。由 App 从
/// <see cref="UsageStatsService"/> 取得，窗口只负责渲染。
/// </summary>
internal sealed class StatsViewSnapshot
{
    /// <summary>统计服务是否可用（启动后应始终为 true，关闭开关不代表不可用）。</summary>
    public bool IsServiceAvailable { get; init; }

    /// <summary>统计采集开关状态。</summary>
    public bool IsEnabled { get; init; }

    /// <summary>“已记录的统计”文本行（名称沿用 R-09 口径）。</summary>
    public IReadOnlyList<string> RecordedLines { get; init; } = Array.Empty<string>();

    /// <summary>“有样本支持的时间偏好”文本行；为空表示没有达标候选。</summary>
    public IReadOnlyList<string> PreferenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// P6-004（第一小步）：主动查看入口打开的只读窗口。分两段展示“已记录的统计”
/// 与“有样本支持的时间偏好”，后者明确标注为统计依据而非准确率。窗口不修改
/// 任何数据，也不触碰宠物存档。
/// </summary>
public partial class StatsViewerWindow : Window
{
    private Func<StatsViewSnapshot>? _provider;

    public StatsViewerWindow()
    {
        InitializeComponent();
    }

    /// <summary>注入数据来源并立即渲染一次。</summary>
    internal void SetProvider(Func<StatsViewSnapshot> provider)
    {
        _provider = provider;
        Refresh();
    }

    /// <summary>按当前数据重新渲染（统计关闭/清除后由 App 调用）。</summary>
    internal void Refresh()
    {
        var snapshot = _provider?.Invoke();
        if (snapshot is null)
        {
            StatusText.Text = "统计状态不可用。";
            RecordedItems.ItemsSource = new[] { "暂无已记录的统计。" };
            PreferenceItems.ItemsSource = new[] { "暂无可显示的时间偏好。" };
            return;
        }

        StatusText.Text = !snapshot.IsServiceAvailable
            ? "统计状态不可用。"
            : snapshot.IsEnabled
                ? "统计：已开启。全部记录仅保存在本机。"
                : "统计：已关闭。关闭期间不采集新记录，已有记录按保留期保留。";

        RecordedItems.ItemsSource = snapshot.RecordedLines.Count > 0
            ? snapshot.RecordedLines
            : new[] { "暂无已记录的统计。" };

        PreferenceItems.ItemsSource = snapshot.PreferenceLines.Count > 0
            ? snapshot.PreferenceLines
            : new[] { "暂无可显示的时间偏好。" };
    }

    /// <summary>当前“已记录的统计”行（供隔离预览/测试断言）。</summary>
    internal IReadOnlyList<string> TestRecordedLines =>
        RecordedItems.ItemsSource as IReadOnlyList<string> ?? Array.Empty<string>();

    /// <summary>当前“时间偏好”行（供隔离预览/测试断言）。</summary>
    internal IReadOnlyList<string> TestPreferenceLines =>
        PreferenceItems.ItemsSource as IReadOnlyList<string> ?? Array.Empty<string>();

    /// <summary>当前状态行（供隔离预览/测试断言）。</summary>
    internal string TestStatusText => StatusText.Text;

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        Refresh();
    }
}
