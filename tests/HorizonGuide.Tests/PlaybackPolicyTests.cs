using HorizonGuide.Core.Locations;
using HorizonGuide.Core.Scheduling;
using Xunit;

namespace HorizonGuide.Tests;

public class PlaybackPolicyTests
{
    /// <summary>200 米见方的正方形，中心在原点。</summary>
    private static Location Square() => new()
    {
        Id = "SQUARE",
        Name = "方块",
        Boundary =
        [
            new Point2D(-100, -100),
            new Point2D(100, -100),
            new Point2D(100, 100),
            new Point2D(-100, 100),
        ],
    };

    [Fact]
    public void 车停着时不限时长()
    {
        var policy = new PlaybackPolicy();
        var budget = policy.Estimate(Square(), 0, 0, speed: 1f, new Heading(1, 0));

        Assert.True(budget.Unlimited);
        Assert.True(budget.Fits(clipSeconds: 40));
    }

    [Fact]
    public void 沿方向算到边界的剩余时间()
    {
        var policy = new PlaybackPolicy { SafetyMargin = 1.0f };

        // 从中心朝 +X 开，到边界 100 米；20 m/s → 5 秒。
        var budget = policy.Estimate(Square(), 0, 0, speed: 20f, new Heading(1, 0));

        Assert.NotNull(budget.Seconds);
        Assert.Equal(5f, budget.Seconds!.Value, precision: 1);
    }

    [Fact]
    public void 已经离开地点时预算为零()
    {
        var policy = new PlaybackPolicy { SafetyMargin = 1.0f };
        var budget = policy.Estimate(Square(), 120, 0, speed: 20f, new Heading(-1, 0));

        Assert.Equal(0f, budget.Seconds!.Value);
    }

    [Fact]
    public void 同一地点同一车速朝不同方向剩余时间不同()
    {
        // 这是整个 policy 存在的理由：不能用"多边形尺寸 ÷ 车速"。
        // 车贴着边界往外开，和刚从一头扎进对角线，剩余时间差很多。
        var policy = new PlaybackPolicy { SafetyMargin = 1.0f };
        var square = Square();

        // 站在东边界内侧 5 米处，朝东开 —— 马上就出去了
        var leaving = policy.Estimate(square, 95, 0, speed: 20f, new Heading(1, 0));

        // 同一个点，掉头朝西开 —— 还有 195 米可走
        var crossing = policy.Estimate(square, 95, 0, speed: 20f, new Heading(-1, 0));

        Assert.Equal(0f, leaving.Seconds!.Value);        // 太短，不开口
        Assert.True(crossing.Seconds!.Value > 9f);
    }

    [Fact]
    public void 剩余时间太短就不开口()
    {
        var policy = new PlaybackPolicy { MinBudget = 4f, SafetyMargin = 1.0f };

        // 离边界 20 米，60 m/s（216 km/h）→ 0.33 秒
        var budget = policy.Estimate(Square(), 80, 0, speed: 60f, new Heading(1, 0));

        Assert.Equal(0f, budget.Seconds!.Value);
        Assert.False(budget.Fits(clipSeconds: 5));
    }

    [Fact]
    public void 安全余量会砍掉一部分预算()
    {
        var full = new PlaybackPolicy { SafetyMargin = 1.0f }
            .Estimate(Square(), 0, 0, 20f, new Heading(1, 0));

        var trimmed = new PlaybackPolicy { SafetyMargin = 0.75f }
            .Estimate(Square(), 0, 0, 20f, new Heading(1, 0));

        Assert.True(trimmed.Seconds < full.Seconds);
    }

    [Fact]
    public void 方向未知时退回按最远边界估()
    {
        var policy = new PlaybackPolicy { SafetyMargin = 1.0f };
        var budget = policy.Estimate(Square(), 0, 0, speed: 20f, heading: null);

        // 最远的顶点是角，距离 sqrt(100²+100²) ≈ 141.4 米 → 7.07 秒
        Assert.NotNull(budget.Seconds);
        Assert.Equal(7.07f, budget.Seconds!.Value, precision: 1);
        Assert.Contains("方向未知", budget.Reason);
    }

    [Fact]
    public void 位移太小时不猜方向()
    {
        // 车几乎没动，位移里全是噪声。给个随机方向比说"不知道"更糟。
        Assert.Null(Heading.FromDelta(0, 0, 0.1f, 0.1f));
        Assert.NotNull(Heading.FromDelta(0, 0, 10f, 0));
    }

    [Fact]
    public void 射线穿出凹多边形时取最近的交点()
    {
        // L 形。从内凹的那一侧射出去，会穿过两条边——必须取近的那个，
        // 取远的会以为还能开很久。
        List<Point2D> lShape =
        [
            new(0, 0), new(100, 0), new(100, 40), new(40, 40), new(40, 100), new(0, 100),
        ];

        var d = Polygon.RayExitDistance(lShape, 20, 20, 1, 0);

        Assert.NotNull(d);
        Assert.Equal(80f, d!.Value, precision: 1);   // 到 x=100，不是到 x=40
    }

    [Fact]
    public void 射线起点在多边形外时返回空()
    {
        var d = Polygon.RayExitDistance(Square().Boundary, 120, 0, -1, 0);

        Assert.Null(d);
    }
}
