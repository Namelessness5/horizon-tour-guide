namespace HorizonGuide.Forza;

/// <summary>
/// 一份最新的车辆状态。每收到一个遥测包就整体替换。
/// </summary>
public sealed class VehicleState
{
    public float PositionX { get; init; }
    public float PositionY { get; init; }
    public float PositionZ { get; init; }

    /// <summary>米/秒。</summary>
    public float Speed { get; init; }

    /// <summary>游戏是否处于“在跑”状态（暂停/菜单时为 false）。</summary>
    public bool IsRaceOn { get; init; }

    /// <summary>游戏侧时间戳，毫秒，会回绕。用于识别重复包。</summary>
    public uint TimestampMs { get; init; }

    public DateTime UpdatedAt { get; init; }
}
