namespace HorizonGuide.Core.Content;

/// <summary>一句话，一个音频文件。播放的最小单位。</summary>
public sealed class ContentSegment
{
    public required string Subtitle { get; init; }

    /// <summary>相对 content/ 目录的路径。</summary>
    public required string AudioPath { get; init; }

    /// <summary>合成时实测的时长。不能按字数估——同一句话两次合成能差 30%。</summary>
    public float Seconds { get; init; }
}

/// <summary>
/// 一篇内容。运行时唯一认识的单位，对应 content/content-index.json 里的一条。
///
/// **它是一串片段，不是一整块音频。** 运行时按玩家还能在这个地点待多久，
/// 决定放前几段：
///
///   150 km/h 冲过去  -> 只听得到第一段
///   慢慢逛           -> 听前几段
///   停车             -> 整篇听完
///
/// 所以同一篇稿子既是 5 秒的钩子，也是 3 分钟的深度解说。不需要写三个长度版本，
/// 而且**电台天然奖励好奇心**——想听全的自己会停下来。
///
/// 内容制作那边的字段（事实来源、置信度、factIds…）到这里为止都被抹掉了，
/// 只留播放需要的。内容格式改版时只改 <see cref="ContentStore"/>。
/// </summary>
public sealed class PlayableContent
{
    public required string Id { get; init; }
    public required string LocationId { get; init; }

    /// <summary>zh / ja / en。同一篇稿子的三个语言版本是三条独立的内容。</summary>
    public required string Lang { get; init; }

    /// <summary>intro / history / trivia / culture / driving / sensory。</summary>
    public required string Category { get; init; }

    public required IReadOnlyList<ContentSegment> Segments { get; init; }

    /// <summary>
    /// Same script across languages. Runtime ids look like LOCATION:script:lang; visit de-duping
    /// should not replay the same script just because the audio language changed.
    /// </summary>
    public string PlaybackId
    {
        get
        {
            var last = Id.LastIndexOf(':');
            return last > 0 ? Id[..last] : Id;
        }
    }

    /// <summary>
    /// 第一段的时长。**这是选内容时的准入门槛**，不是整篇的总长。
    ///
    /// 连第一段（总述）都放不下的内容，根本不该被选中——起个头就被打断，
    /// 比全程沉默更糟。而只要第一段放得下，这篇内容就是可用的，
    /// 后面能播几段是播放时再说的事。
    /// </summary>
    public float FirstSegmentSeconds => Segments.Count > 0 ? Segments[0].Seconds : 0;

    public float TotalSeconds => Segments.Sum(s => s.Seconds);
}
