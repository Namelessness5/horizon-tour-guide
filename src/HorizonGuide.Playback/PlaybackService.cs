using HorizonGuide.Core.Content;
using HorizonGuide.Core.Scheduling;

namespace HorizonGuide.Playback;

/// <summary>
/// 播一篇内容：一段一段地放，每段之前重新问一次"还剩多少时间"。
///
/// **重估预算是这里的核心**，也是整个片段化设计的收益所在：
///
///   玩家降速  -> 预算变大 -> 多放几段（他有兴趣，那就多讲）
///   玩家加速  -> 预算变小 -> 提前收尾（他要走了，别硬讲）
///   玩家停车  -> 预算不限 -> 整篇放完（3 分钟的深度解说）
///
/// 不需要单独做"降速检测"——按预算贪心地放段，这个行为自己就长出来了。
///
/// 收尾发生在**段与段之间**，不在句子中间。玩家听到的是"说完这句就停了"，
/// 而不是被硬生生切断。
/// </summary>
public sealed class PlaybackService : IDisposable
{
    private readonly AudioPlayer _player = new();
    private readonly string _contentRoot;

    public PlaybackService(string contentRoot)
    {
        _contentRoot = contentRoot;
    }

    /// <summary>
    /// 段与段之间的停顿。
    ///
    /// 每段是独立合成的，语调会在段边界重置——直接拼会有轻微的断层感。
    /// 一点停顿能盖住它，听起来反而像电台的"说完一句停一下"。
    /// 停顿不烤进音频，放在这里，调参不用重新合成。
    /// </summary>
    public TimeSpan SegmentGap { get; init; } = TimeSpan.FromMilliseconds(300);

    public bool IsPlaying { get; private set; }

    public PlayableContent? Current { get; private set; }

    public float Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0f, 2f);
    }

    /// <summary>该显示字幕了。参数是当前这一段的字幕。</summary>
    public event Action<string>? SubtitleShown;

    /// <summary>该收起字幕了。整篇播完、被跳过、出错都会走这里。</summary>
    public event Action? SubtitleHidden;

    /// <summary>音频文件有问题（缺失/损坏）。设计文档 §5.5：跳过，记日志，别让程序倒下。</summary>
    public event Action<PlayableContent, Exception>? PlaybackFailed;

    /// <summary>
    /// 放这篇内容，能放几段放几段。返回实际放了几段。
    ///
    /// <paramref name="budget"/> 每段之前都会被重新调用一次——它读的是**当时**的车辆
    /// 状态，所以玩家一踩刹车，下一段就有机会继续讲。
    /// </summary>
    public async Task<int> PlayAsync(
        PlayableContent content,
        PlayableContent? subtitles,
        Func<TimeBudget> budget,
        CancellationToken cancellationToken)
    {
        Current = content;
        IsPlaying = true;

        var played = 0;
        try
        {
            for (var i = 0; i < content.Segments.Count; i++)
            {
                var segment = content.Segments[i];
                var subtitle = subtitles is not null && i < subtitles.Segments.Count
                    ? subtitles.Segments[i].Subtitle
                    : segment.Subtitle;

                // 第一段无条件放——选中这篇的时候就已经确认它塞得进去了。
                // 之后每一段都要重新问：这段还放得下吗？放不下就收尾。
                if (played > 0)
                {
                    if (!budget().Fits(segment.Seconds))
                        break;

                    await Task.Delay(SegmentGap, cancellationToken);
                }

                SubtitleShown?.Invoke(subtitle);

                var path = Path.Combine(_contentRoot, segment.AudioPath);
                bool finished;
                try
                {
                    finished = await _player.PlayAsync(path, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or FormatException)
                {
                    // 一段音频坏了，不该让整个漫游停摆。整篇丢掉——
                    // 中间缺一段，前后就接不上了。
                    PlaybackFailed?.Invoke(content, ex);
                    break;
                }

                played++;

                if (!finished)   // 被跳过了
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 关漫游 / 退出程序
        }
        finally
        {
            IsPlaying = false;
            Current = null;
            SubtitleHidden?.Invoke();
        }

        return played;
    }

    /// <summary>跳过。当前这一段放完就停，不再往下播。</summary>
    public void Skip() => _player.Stop();

    public void Dispose() => _player.Dispose();
}
