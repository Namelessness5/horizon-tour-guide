using HorizonGuide.Core.Content;
using HorizonGuide.Core.Locations;
using HorizonGuide.Core.Scheduling;
using Xunit;

namespace HorizonGuide.Tests;

public class ContentSelectorTests
{
    /// <summary>一篇内容。segmentSeconds 是每一段的时长。</summary>
    private static PlayableContent Piece(
        string id, string locationId, string category, string lang = "zh",
        params float[] segmentSeconds) =>
        new()
        {
            Id = id,
            LocationId = locationId,
            Lang = lang,
            Category = category,
            Segments = segmentSeconds.Select((secs, i) => new ContentSegment
            {
                Subtitle = $"{id} 第{i + 1}段",
                AudioPath = $"audio/{id}_{i + 1}.wav",
                Seconds = secs,
            }).ToList(),
        };

    private static Location Loc(string id, string? parentId = null) => new()
    {
        Id = id,
        Name = id,
        ParentId = parentId,
        Boundary = [new Point2D(-1, -1), new Point2D(1, -1), new Point2D(1, 1)],
    };

    private static LocationStack Stack(params Location[] locations) => new(locations);

    private static readonly TimeBudget Unlimited = new(null, "测试");

    [Fact]
    public void 先看最具体的地点没有内容才退到地区()
    {
        var store = new ContentStore([
            Piece("region_1", "TOKYO", "trivia", "zh", 10),
            Piece("landmark_1", "SHIBUYA", "trivia", "zh", 10),
        ]);
        var selector = new ContentSelector(store);

        var picked = selector.Select(Stack(Loc("TOKYO"), Loc("SHIBUYA", "TOKYO")), Unlimited);

        Assert.Equal("landmark_1", picked!.Id);
    }

    [Fact]
    public void 最具体的地点没有内容时退到地区()
    {
        var store = new ContentStore([Piece("region_1", "TOKYO", "trivia", "zh", 10)]);
        var selector = new ContentSelector(store);

        var picked = selector.Select(Stack(Loc("TOKYO"), Loc("SHIBUYA", "TOKYO")), Unlimited);

        Assert.Equal("region_1", picked!.Id);
    }

    [Fact]
    public void 门槛是第一段的时长不是整篇的总长()
    {
        // 这是片段化的核心性质。一篇 100 秒的深度内容，第一段（总述）只有 6 秒：
        // 玩家只有 10 秒预算，照样该给他听——他听完总述就开走了，那也是完整的一段话。
        //
        // 要是按总长过滤，所有长内容都会被永久排除，长博客就白写了。
        var store = new ContentStore([
            Piece("deep_dive", "SHIBUYA", "history", "zh", 6, 10, 12, 12, 20, 20, 20),
        ]);
        var selector = new ContentSelector(store);

        var picked = selector.Select(Stack(Loc("SHIBUYA")), new TimeBudget(10, "只够听个开头"));

        Assert.NotNull(picked);
        Assert.Equal(100f, picked!.TotalSeconds);        // 整篇 100 秒
        Assert.Equal(6f, picked.FirstSegmentSeconds);    // 但门槛只看这 6 秒
    }

    [Fact]
    public void 连第一段都塞不下就不选()
    {
        var store = new ContentStore([Piece("long_one", "SHIBUYA", "intro", "zh", 30, 20)]);
        var selector = new ContentSelector(store);

        // 150 km/h 冲过涩谷只有 4 秒。连总述都放不完——起个头就被打断，
        // 比全程沉默更糟。宁可闭嘴。
        var picked = selector.Select(Stack(Loc("SHIBUYA")), new TimeBudget(4, "太快了"));

        Assert.Null(picked);
    }

    [Fact]
    public void 预算之内的都可能被选中()
    {
        var store = new ContentStore([
            Piece("short_one", "SHIBUYA", "trivia", "zh", 5),
            Piece("long_one", "SHIBUYA", "history", "zh", 30),
        ]);
        var selector = new ContentSelector(store);

        // 预算 10 秒：只有短的塞得进
        var rushed = selector.Select(Stack(Loc("SHIBUYA")), new TimeBudget(10, "快"));
        Assert.Equal("short_one", rushed!.Id);

        // 预算充足：两条都可能被抽中，多抽几次两条都该出现
        var seen = new HashSet<string>();
        for (var i = 0; i < 50; i++)
            seen.Add(selector.Select(Stack(Loc("SHIBUYA")), Unlimited)!.Id);

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void 只选当前语言的内容()
    {
        var store = new ContentStore([
            Piece("zh_1", "SHIBUYA", "intro", "zh", 10),
            Piece("ja_1", "SHIBUYA", "intro", "ja", 10),
            Piece("en_1", "SHIBUYA", "intro", "en", 10),
        ]);
        var selector = new ContentSelector(store) { Language = "ja" };

        var picked = selector.Select(Stack(Loc("SHIBUYA")), Unlimited);

        Assert.Equal("ja_1", picked!.Id);
    }

    [Fact]
    public void 不在任何地点内时沉默()
    {
        var store = new ContentStore([Piece("a", "SHIBUYA", "intro", "zh", 10)]);
        var selector = new ContentSelector(store);

        Assert.Null(selector.Select(LocationStack.Empty, Unlimited));
    }

    /// <summary>
    /// 玩家停在地点里不走，讲完一篇接着讲下一篇——但不能把讲过的再讲一遍。
    /// 随机抽取本身不保证不重复，所以调用方要把播过的传进来。
    /// </summary>
    [Fact]
    public void 排除这次停留已经播过的篇目()
    {
        var store = new ContentStore([
            Piece("a", "SHIBUYA", "intro", "zh", 10),
            Piece("b", "SHIBUYA", "history", "zh", 10),
        ]);
        var selector = new ContentSelector(store);

        var played = new HashSet<string> { "a" };
        for (var i = 0; i < 50; i++)
            Assert.Equal("b", selector.Select(Stack(Loc("SHIBUYA")), Unlimited, played)!.Id);
    }

    [Fact]
    public void ExcludesSameScriptAcrossLanguages()
    {
        var store = new ContentStore([
            Piece("SHIBUYA:s1:zh", "SHIBUYA", "intro", "zh", 10),
            Piece("SHIBUYA:s1:ja", "SHIBUYA", "intro", "ja", 10),
            Piece("SHIBUYA:s2:ja", "SHIBUYA", "history", "ja", 10),
        ]);
        var selector = new ContentSelector(store) { Language = "ja" };

        var picked = selector.Select(
            Stack(Loc("SHIBUYA")),
            Unlimited,
            new HashSet<string> { "SHIBUYA:s1" });

        Assert.Equal("SHIBUYA:s2:ja", picked!.Id);
    }

    [Fact]
    public void FindsVariantForSameScript()
    {
        var zh = Piece("SHIBUYA:s1:zh", "SHIBUYA", "intro", "zh", 10);
        var ja = Piece("SHIBUYA:s1:ja", "SHIBUYA", "intro", "ja", 10);
        var store = new ContentStore([zh, ja]);

        Assert.Same(zh, store.VariantFor(ja, "zh"));
    }

    /// <summary>
    /// 全部讲完之后就安静下来，而不是回头再讲一遍。
    /// 玩家在金阁寺停了十分钟，听完八篇，第九次不该是"我们再从头说起"。
    /// </summary>
    [Fact]
    public void 讲完所有篇目之后沉默()
    {
        var store = new ContentStore([
            Piece("a", "SHIBUYA", "intro", "zh", 10),
            Piece("b", "SHIBUYA", "history", "zh", 10),
        ]);
        var selector = new ContentSelector(store);

        var played = new HashSet<string> { "a", "b" };

        Assert.Null(selector.Select(Stack(Loc("SHIBUYA")), Unlimited, played));
    }
}
