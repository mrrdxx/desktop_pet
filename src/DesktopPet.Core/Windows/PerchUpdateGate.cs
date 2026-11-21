namespace DesktopPet.Core.Windows;

/// <summary>
/// P5A-PLAN：外框更新上报闸门（纯逻辑）。
/// 以上一次有效（已上报）外框为比较基线：仅当最新外框与基线不同才上报，
/// 上报后基线前移。因此移动后静止时不重复上报；移回初始位置（与当前基线
/// 不同）仍会上报；连续快速移动时每次外框变化都上报，最终基线即最终外框。
/// 纯逻辑无 I/O；探针/栖息实现负责采样与事件写入。
/// </summary>
public sealed class PerchUpdateGate
{
    private int[]? _lastReported;

    /// <summary>当前基线（上一次已上报的外框）；尚未建立时为 null。</summary>
    public int[]? LastReported => _lastReported is null ? null : new[] { _lastReported[0], _lastReported[1], _lastReported[2], _lastReported[3] };

    /// <summary>建立基线（选择或恢复目标时调用；不触发上报）。</summary>
    public void Reset(int left, int top, int right, int bottom)
        => _lastReported = new[] { left, top, right, bottom };

    /// <summary>
    /// 判断最新外框是否应上报：与基线不同返回 true 并把基线前移到最新外框；
    /// 相同返回 false（静止，不重复上报）。尚未建立基线时视为应上报并建立基线。
    /// </summary>
    public bool ShouldReport(int left, int top, int right, int bottom)
    {
        if (_lastReported is null)
        {
            _lastReported = new[] { left, top, right, bottom };
            return true;
        }
        if (_lastReported[0] == left && _lastReported[1] == top
            && _lastReported[2] == right && _lastReported[3] == bottom)
            return false;
        _lastReported = new[] { left, top, right, bottom };
        return true;
    }

    /// <summary>
    /// P5A-001 R1 F-02/F-04：只读判断最新外框是否应上报（不消费基线）。
    /// 判定与 <see cref="ShouldReport"/> 一致；调用方先以本方法预检，
    /// 跟随实际提交成功后再用 <see cref="ShouldReport"/> 消费基线，
    /// 保证"无法提交时不报告、不消费"。
    /// </summary>
    public bool PeekShouldReport(int left, int top, int right, int bottom)
    {
        if (_lastReported is null)
            return true;
        return !(_lastReported[0] == left && _lastReported[1] == top
            && _lastReported[2] == right && _lastReported[3] == bottom);
    }
}
