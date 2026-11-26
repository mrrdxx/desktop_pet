using System.Globalization;
using DesktopPet.Core.Persistence;

namespace DesktopPet.Core.Events;

/// <summary>
/// P6-004（第一小步）：一次主动气泡展示判定结果。
/// <para>
/// 只描述“是否展示、展示什么依据”，不含位置/动画/窗口操作；位置与呈现由
/// App 层负责。文案只表达统计依据，不写指令式措辞，也不含准确率或“知道用户
/// 意图”的宣称（PRODUCT R-09）。
/// </para>
/// </summary>
public sealed class UsageStatsBubbleDecision
{
    /// <summary>是否应展示主动气泡。</summary>
    public bool ShouldShow { get; init; }

    /// <summary>要展示的文案（仅 <see cref="ShouldShow"/> 为 true 时有意义）。</summary>
    public string Message { get; init; } = "";

    /// <summary>命中候选的应用标识（规范化可执行文件基名）。</summary>
    public string AppId { get; init; } = "";

    /// <summary>命中候选的半小时时段起点（"HH:mm"）。</summary>
    public string SlotStart { get; init; } = "";

    /// <summary>判定时的本地日期（yyyy-MM-dd），用于每日去重。</summary>
    public string LocalDate { get; init; } = "";
}

/// <summary>
/// P6-004（第一小步）：“已记录的统计”汇总。全部来自已提交聚合桶，沿用 R-09
/// 口径：近似前台活跃时长与会话次数，不使用“使用次数/启动次数”等会与 5 秒
/// 采样或程序启动混淆的名称。
/// </summary>
public sealed class UsageStatsRecordedSummary
{
    /// <summary>有记录的日期数（存在 ActiveMs &gt; 0 的不同本地日期）。</summary>
    public int RecordedDateCount { get; init; }

    /// <summary>近似前台活跃时长（毫秒，聚合桶 ActiveMs 之和）。</summary>
    public double ApproximateForegroundActiveMs { get; init; }

    /// <summary>会话次数（聚合桶 SessionCount 之和；一次使用＝一个会话）。</summary>
    public int SessionCount { get; init; }

    /// <summary>涉及的应用数（不同非空 AppId）。</summary>
    public int AppCount { get; init; }

    /// <summary>是否存在任何已记录统计。</summary>
    public bool HasRecords =>
        RecordedDateCount > 0 || SessionCount > 0 || ApproximateForegroundActiveMs > 0.0 || AppCount > 0;
}

/// <summary>
/// P6-004（第一小步）：主动气泡与只读查看入口的纯逻辑。无状态、无持久化、
/// 无 WPF 依赖，便于规则测试；App 层只负责取数据与呈现。
/// </summary>
public static class UsageStatsBubblePlanner
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string SlotFormat = "HH:mm";

    /// <summary>
    /// 当前本地时间按 30 分钟截断得到的时段起点："HH:00" 或 "HH:30"。
    /// 与 P6-002 固定桶网格、P6-003 候选的 <c>SlotStart</c> 同一口径。
    /// </summary>
    public static string GetCurrentSlotStart(DateTimeOffset localNow)
    {
        var flooredMinute = localNow.Minute < 30 ? 0 : 30;
        return $"{localNow.Hour:D2}:{flooredMinute:D2}";
    }

    /// <summary>
    /// 把时段起点（"HH:mm"）渲染成可见区间标签，例如 "12:00–12:29"、
    /// "12:30–12:59"、"23:30–23:59"。无法解析时原样返回。
    /// </summary>
    public static string FormatSlotLabel(string slotStart)
    {
        if (!TimeOnly.TryParseExact(slotStart, SlotFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start))
            return slotStart;

        var end = start.AddMinutes(29);
        return $"{start.ToString(SlotFormat, CultureInfo.InvariantCulture)}–{end.ToString(SlotFormat, CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 生成主动气泡文案：只陈述“过去有记录的日期里有几天在这个时段用过该
    /// 应用”，不暗示用户此刻应当打开它，也不包装准确率。
    /// </summary>
    public static string FormatBubbleMessage(UsageStatsFrequencyCandidate candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        return $"过去有记录的日期里，你有 {candidate.SupportDateCount} 天在 {FormatSlotLabel(candidate.SlotStart)} 这个时段使用 {candidate.AppId}。";
    }

    /// <summary>
    /// 主动气泡判定。任一条件不满足都不展示，且不消费当日额度：
    /// <list type="bullet">
    /// <item>统计关闭 / 结果为空 → 安静（不提示“样本不足”）；</item>
    /// <item>暂停、锁屏、睡眠任一挂起 → 不展示；</item>
    /// <item>没有可接受展示的空闲宠物 → 不展示；</item>
    /// <item>同一本地日期已经展示过（含跨重启读回的去重日期）→ 不展示；</item>
    /// <item>没有与当前半小时时段匹配的达标候选 → 不展示。</item>
    /// </list>
    /// </summary>
    public static UsageStatsBubbleDecision Decide(
        UsageStatsFrequencyResult? result,
        DateTimeOffset localNow,
        string? lastShownLocalDate,
        bool isSuspended,
        bool hasAvailablePet)
    {
        var localDate = localNow.ToString(DateFormat, CultureInfo.InvariantCulture);
        var suppressed = new UsageStatsBubbleDecision { LocalDate = localDate };

        if (result is null || !result.IsEnabled)
            return suppressed;
        if (isSuspended || !hasAvailablePet)
            return suppressed;
        if (string.Equals(lastShownLocalDate, localDate, StringComparison.Ordinal))
            return suppressed;

        var slot = GetCurrentSlotStart(localNow);
        UsageStatsFrequencyCandidate? match = null;
        foreach (var candidate in result.Candidates)
        {
            if (candidate is null) continue;
            if (string.Equals(candidate.SlotStart, slot, StringComparison.Ordinal))
            {
                match = candidate;
                break;
            }
        }
        if (match is null)
            return suppressed;

        return new UsageStatsBubbleDecision
        {
            ShouldShow = true,
            Message = FormatBubbleMessage(match),
            AppId = match.AppId,
            SlotStart = match.SlotStart,
            LocalDate = localDate
        };
    }

    /// <summary>
    /// 汇总“已记录的统计”。只看 ActiveMs &gt; 0 的聚合桶；缺失日期不代表用户
    /// 没有使用，因此这里只报“有记录的日期数”，不推“未使用天数”。
    /// <para>
    /// P6-004 R1 F-02：汇总在读取侧就按聚合保留期过滤，首次查询即生效，不等待
    /// 下一次 drain 的物理清理。窗口与 <see cref="UsageStatsCollector.PruneToRetention"/>
    /// 同口径：以 <paramref name="localNow"/> 的本地日期为今天，保留
    /// <c>[today − (retentionDays − 1), today]</c>（含今天）。查询本身不写盘、
    /// 不延长保留期、不恢复过期记录，也不复用 P6-003 预测的“排除今天”口径。
    /// 无法解析日期的桶不做额外删除，与采集器的保留判定保持一致。
    /// </para>
    /// </summary>
    /// <param name="buckets">已提交聚合桶。</param>
    /// <param name="localNow">判定“今天”的本地时间（与采集器同一时间源）。</param>
    /// <param name="retentionDays">聚合保留天数，默认 30（含今天）。</param>
    public static UsageStatsRecordedSummary SummarizeRecorded(
        IEnumerable<UsageStatsBucket>? buckets,
        DateTimeOffset localNow,
        int retentionDays = UsageStatsCollector.AggregatedRetentionDays)
    {
        if (retentionDays < 1) retentionDays = 1;
        var today = DateOnly.FromDateTime(localNow.Date);
        var cutoff = today.AddDays(-(retentionDays - 1)); // oldest kept bucket date

        var dates = new HashSet<string>(StringComparer.Ordinal);
        var apps = new HashSet<string>(StringComparer.Ordinal);
        double activeMs = 0.0;
        var sessions = 0;

        if (buckets is not null)
        {
            foreach (var bucket in buckets)
            {
                if (bucket is null) continue;
                if (!(bucket.ActiveMs > 0.0)) continue;
                if (!IsWithinAggregatedRetention(bucket.Date, cutoff, today)) continue;

                var appId = bucket.AppId?.Trim();
                if (!string.IsNullOrEmpty(appId))
                    apps.Add(appId);

                var date = bucket.Date?.Trim();
                if (!string.IsNullOrEmpty(date))
                    dates.Add(date);

                activeMs += bucket.ActiveMs;
                if (bucket.SessionCount > 0)
                    sessions += bucket.SessionCount;
            }
        }

        return new UsageStatsRecordedSummary
        {
            RecordedDateCount = dates.Count,
            ApproximateForegroundActiveMs = activeMs,
            SessionCount = sessions,
            AppCount = apps.Count
        };
    }

    /// <summary>
    /// 桶的计数日期是否在聚合保留窗口内。可解析日期必须落在
    /// <c>[cutoff, today]</c>；无法解析的日期不做额外过滤（与采集器一致）。
    /// </summary>
    private static bool IsWithinAggregatedRetention(string? date, DateOnly cutoff, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(date)) return true;
        if (!DateOnly.TryParse(date, out var parsed)) return true;
        return parsed >= cutoff && parsed <= today;
    }

    /// <summary>
    /// 把“已记录的统计”渲染成查看窗文本行，名称与 R-09 口径一致。
    /// </summary>
    public static IReadOnlyList<string> FormatRecordedSummaryLines(UsageStatsRecordedSummary summary)
    {
        if (summary is null) throw new ArgumentNullException(nameof(summary));
        if (!summary.HasRecords)
            return new[] { "暂无已记录的统计。" };

        return new[]
        {
            $"有记录的日期：{summary.RecordedDateCount} 天",
            $"近似前台活跃时长：{FormatDuration(summary.ApproximateForegroundActiveMs)}",
            $"会话次数：{summary.SessionCount} 次",
            $"涉及应用：{summary.AppCount} 个"
        };
    }

    /// <summary>
    /// 把一条时间偏好候选渲染成查看窗文本行。明确是“支持日期/会话”的统计
    /// 依据，不是准确率。
    /// </summary>
    public static string FormatPreferenceLine(UsageStatsFrequencyCandidate candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        return $"{candidate.AppId}　{FormatSlotLabel(candidate.SlotStart)}　支持日期 {candidate.SupportDateCount} 天，会话 {candidate.SessionCount} 次";
    }

    /// <summary>把毫秒渲染为“X 小时 Y 分钟”或“不足 1 分钟”。</summary>
    public static string FormatDuration(double activeMs)
    {
        if (!(activeMs > 0.0))
            return "0 分钟";

        var totalMinutes = (long)Math.Round(activeMs / 60000.0);
        if (totalMinutes < 1)
            return "不足 1 分钟";
        if (totalMinutes < 60)
            return $"{totalMinutes} 分钟";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return minutes == 0 ? $"{hours} 小时" : $"{hours} 小时 {minutes} 分钟";
    }
}
