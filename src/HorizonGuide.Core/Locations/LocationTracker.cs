namespace HorizonGuide.Core.Locations;

/// <summary>
/// 边界稳定处理。车在边界附近行驶会反复进出，直接用瞬时结果会抖。
///
/// 连续位于新地点内 <see cref="EnterDelay"/> 才确认进入；
/// 连续不在当前地点内 <see cref="LeaveDelay"/> 才确认离开。
/// 时间是配置项，方便实机调整。
/// </summary>
public sealed class LocationTracker
{
    public LocationTracker(TimeSpan? enterDelay = null, TimeSpan? leaveDelay = null)
    {
        EnterDelay = enterDelay ?? TimeSpan.FromSeconds(1);
        LeaveDelay = leaveDelay ?? TimeSpan.FromSeconds(2);
    }

    public TimeSpan EnterDelay { get; }
    public TimeSpan LeaveDelay { get; }

    /// <summary>确认过的位置栈。内容选择只看这个，不看瞬时结果。</summary>
    public LocationStack Stable { get; private set; } = LocationStack.Empty;

    /// <summary>正在等待确认的位置栈；没有待确认的变化时为 null。</summary>
    public LocationStack? Pending { get; private set; }

    private DateTime _pendingSince;

    /// <summary>还要等多久确认；没有待确认的变化时为 <see cref="TimeSpan.Zero"/>。</summary>
    public TimeSpan RemainingToConfirm(DateTime now)
    {
        if (Pending is null)
            return TimeSpan.Zero;

        var delay = Pending.IsEmpty ? LeaveDelay : EnterDelay;
        var remaining = delay - (now - _pendingSince);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>返回 true 表示确认过的位置栈发生了变化。</summary>
    public bool Update(LocationStack raw, DateTime now)
    {
        if (raw.SameAs(Stable))
        {
            Pending = null;
            return false;
        }

        if (Pending is null || !raw.SameAs(Pending))
        {
            Pending = raw;
            _pendingSince = now;
            return false;
        }

        if (RemainingToConfirm(now) > TimeSpan.Zero)
            return false;

        Stable = raw;
        Pending = null;
        return true;
    }
}
