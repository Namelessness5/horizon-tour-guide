using HorizonGuide.Core.Locations;
using HorizonGuide.Core.Scheduling;

namespace HorizonGuide.Core.Content;

/// <summary>
/// 从位置栈里随机挑一篇现在能播的内容。
///
/// **准入门槛是第一段的时长，不是整篇的总长。** 这是片段化之后最关键的一处改动：
/// 一篇 100 秒的内容，只要它的第一段（总述，比如 8 秒）塞得进预算，它就是可用的
/// ——玩家听到总述就走了，那也完整；他停下来，就能一路听完。
/// 按总长过滤的话，所有长内容都会被永久排除，长博客就白写了。
///
/// **不记跨停留的历史** —— 出去再进来，一切重来（可能抽到同一篇，这没关系）。
/// 记的只有"这次停留里已经播过哪几篇"（<paramref name="exclude"/>），
/// 因为玩家停在原地时会连着听好几篇，同一篇讲两遍是明显的 bug。
/// 跨会话的历史（听过什么、去过哪）会牵出一堆纠缠的规则，留到之后。
///
/// 这里**不做**时间估算——"还剩几秒"是 <see cref="PlaybackPolicy"/> 的职责。
/// </summary>
public sealed class ContentSelector
{
    private readonly ContentStore _store;
    private readonly Random _random;

    public ContentSelector(ContentStore store, Random? random = null)
    {
        _store = store;
        _random = random ?? Random.Shared;
    }

    /// <summary>播哪种语言。第一版是全局设置，不随地点变。</summary>
    public string Language { get; set; } = "zh";

    /// <summary>
    /// 位置栈从大到小（地区 → 景观）。按设计文档 §13，**先看最具体的地点**，
    /// 它没有能播的了才往上退到地区；都没有就沉默。
    /// </summary>
    /// <param name="exclude">
    /// 这次停留里已经播过的内容 id。玩家停在地点里时会连着听好几篇，
    /// 不排掉的话随机很快就会重复——七八篇里抽两次撞上同一篇的概率并不低。
    /// 全部播完之后 <see cref="Select"/> 返回 null，也就是安静下来，这是对的：
    /// 已经讲完了，不该翻来覆去讲第二遍。
    /// </param>
    public PlayableContent? Select(
        LocationStack stack, TimeBudget budget, IReadOnlySet<string>? exclude = null)
    {
        if (stack.IsEmpty)
            return null;

        for (var i = stack.Locations.Count - 1; i >= 0; i--)
        {
            if (Select(stack.Locations[i], budget, exclude) is { } content)
                return content;
        }

        return null;
    }

    private PlayableContent? Select(
        Location location, TimeBudget budget, IReadOnlySet<string>? exclude)
    {
        var candidates = _store.ForLocation(location.Id)
            .Where(c => c.Lang == Language)
            .Where(c => c.Segments.Count > 0)
            .Where(c => exclude is null || !exclude.Contains(c.PlaybackId))
            .Where(c => budget.Fits(c.FirstSegmentSeconds))
            .ToList();

        return candidates.Count > 0 ? candidates[_random.Next(candidates.Count)] : null;
    }
}
