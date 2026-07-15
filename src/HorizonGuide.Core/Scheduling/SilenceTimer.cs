namespace HorizonGuide.Core.Scheduling;

/// <summary>
/// 两段内容之间的最短沉默。
///
/// 没有它，玩家穿过一片密集的地点时会被连珠炮式地灌解说，很快就烦了。
/// 电台的价值一半在于它知道什么时候闭嘴。
/// </summary>
public sealed class SilenceTimer
{
    private DateTime _lastEnded = DateTime.MinValue;

    public SilenceTimer(TimeSpan? minSilence = null)
    {
        MinSilence = minSilence ?? TimeSpan.FromSeconds(20);
    }

    public TimeSpan MinSilence { get; }

    public bool CanPlayNext(DateTime now) => now - _lastEnded >= MinSilence;

    public TimeSpan Remaining(DateTime now)
    {
        var left = MinSilence - (now - _lastEnded);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    /// <summary>一段内容播完（或被跳过）时调用。沉默是从上一段结束开始算的。</summary>
    public void Restart(DateTime now) => _lastEnded = now;
}
