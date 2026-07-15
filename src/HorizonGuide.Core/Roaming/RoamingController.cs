namespace HorizonGuide.Core.Roaming;

public enum RoamingState
{
    Off,
    Active,
}

/// <summary>
/// 漫游模式的开关。设计文档 §5.2。
///
/// 关掉漫游时本次漫游的播放历史也清空——重新开启就是新的一轮，
/// 之前听过的内容可以再听（否则玩家关掉再开，会发现电台变哑巴了）。
/// </summary>
public sealed class RoamingController
{
    public RoamingState State { get; private set; } = RoamingState.Off;

    public bool IsActive => State == RoamingState.Active;

    /// <summary>状态变化时触发。参数是新状态。</summary>
    public event Action<RoamingState>? StateChanged;

    /// <summary>玩家按了跳过键。由主循环消费。</summary>
    public event Action? SkipRequested;

    public void Toggle()
    {
        if (IsActive)
            Stop();
        else
            Start();
    }

    public void Start()
    {
        if (IsActive)
            return;

        State = RoamingState.Active;
        StateChanged?.Invoke(State);
    }

    public void Stop()
    {
        if (!IsActive)
            return;

        State = RoamingState.Off;
        StateChanged?.Invoke(State);
    }

    public void Skip()
    {
        if (IsActive)
            SkipRequested?.Invoke();
    }
}
