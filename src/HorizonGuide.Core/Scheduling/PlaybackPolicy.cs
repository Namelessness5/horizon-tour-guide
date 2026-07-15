using HorizonGuide.Core.Locations;

namespace HorizonGuide.Core.Scheduling;

/// <summary>车辆的行驶方向（单位向量，XZ 平面）。</summary>
public readonly record struct Heading(float X, float Z)
{
    /// <summary>
    /// 从两帧之间的位移算方向。遥测包里没有朝向字段，只能这么推。
    ///
    /// 位移太小时（几乎没动、或者两帧间隔太短）方向是噪声，返回 null——
    /// 宁可说"不知道"，也不要给一个随机方向，那会让时间预算彻底失真。
    /// </summary>
    public static Heading? FromDelta(float fromX, float fromZ, float toX, float toZ)
    {
        var dx = toX - fromX;
        var dz = toZ - fromZ;
        var length = MathF.Sqrt(dx * dx + dz * dz);

        // 0.5 米：低于这个距离，位移里的噪声占比太大，方向不可信。
        if (length < 0.5f)
            return null;

        return new Heading(dx / length, dz / length);
    }
}

/// <summary>
/// 还能播多长的内容。
///
/// <see cref="Seconds"/> 为 null 表示不限时长——车停着或者慢到不会很快离开，
/// 爱播多长播多长。
/// </summary>
public readonly record struct TimeBudget(float? Seconds, string Reason)
{
    public bool Unlimited => Seconds is null;

    public bool Fits(float clipSeconds) => Seconds is not { } s || clipSeconds <= s;

    public override string ToString() =>
        Unlimited ? $"不限（{Reason}）" : $"{Seconds:F1}s（{Reason}）";
}

/// <summary>
/// 决定"现在还能播多长的内容"。
///
/// 这是整个播放逻辑里最会长的地方——以后要加的东西很多：路口减速的预判、
/// 玩家在地点里绕圈、掉头、多个地点重叠时按哪个算预算、玩家停车拍照……
/// 所以这个文件只干这一件事，不掺内容选择、不掺播放、不碰 I/O，
/// 全是纯函数，改起来不会牵动别的模块。
///
/// 核心估算：
///
///     从车的位置沿行驶方向射出去，打到多边形边界的距离 ÷ 车速 = 还能待几秒
///
/// 为什么不用"多边形尺寸 ÷ 车速"：车可能正贴着边界开出去，也可能刚从一头
/// 扎进最长的对角线。同样的地点、同样的车速，剩余时间能差十倍。方向是必须的。
/// </summary>
public sealed class PlaybackPolicy
{
    /// <summary>低于这个速度（米/秒）就认为车基本停着，时间预算不设限。约 7 km/h。</summary>
    public float ParkedSpeed { get; init; } = 2f;

    /// <summary>
    /// 估算出来的可用时长要乘以这个系数。
    ///
    /// **它现在大于 1，是故意的。** 片段化之前它是 0.75（故意保守）——那时候超预算
    /// 意味着内容被硬切在半句话中间，所以宁可少说。
    ///
    /// 片段化之后这个理由不成立了：**一段一旦开始就会放完**，哪怕玩家已经开出了
    /// 这个地点。超预算的后果只是"话在你离开之后才说完"，这完全可以接受——
    /// 反而是"明明有话要说却憋着不说"更糟。
    ///
    /// 所以现在故意高估，让电台更愿意开口。实机调参用 --margin。
    /// </summary>
    public float SafetyMargin { get; init; } = 1.6f;

    /// <summary>
    /// 预算低于这个（秒）就干脆别开口。
    ///
    /// 一句话刚起个头就被打断，比全程沉默更糟——玩家会觉得程序坏了。
    /// </summary>
    public float MinBudget { get; init; } = 3f;

    /// <summary>
    /// 估算在这个地点还能待多久。
    ///
    /// 方向未知时（刚起步、位移太小）不猜，退回按"到最远边界点的距离"算——
    /// 这是个上界，会高估，所以只在没有更好信息时用。
    /// </summary>
    public TimeBudget Estimate(Location location, float x, float z, float speed, Heading? heading)
    {
        if (location.HasPolygon && !location.Contains(x, z))
            return new TimeBudget(0f, "已离开地点");

        if (speed <= ParkedSpeed)
            return new TimeBudget(null, "车停着");

        float? distance = null;

        if (heading is { } h)
            distance = Polygon.RayExitDistance(location.Boundary, x, z, h.X, h.Z);

        if (distance is null)
        {
            // 不知道往哪开。退回一个粗略的上界：到最远边界点的距离。
            // 会高估——但 SafetyMargin 会砍掉一部分，而且方向很快就能算出来。
            var farthest = 0f;
            foreach (var p in location.Boundary)
                farthest = MathF.Max(farthest, p.DistanceTo(x, z));

            if (farthest <= 0f)
                return new TimeBudget(null, "地点没有边界");

            distance = farthest;
        }

        var seconds = distance.Value / speed * SafetyMargin;
        var reason = heading is null ? "方向未知，按最远边界估" : "沿当前方向到边界";

        return seconds < MinBudget
            ? new TimeBudget(0f, $"只剩 {seconds:F1}s，太短了不开口")
            : new TimeBudget(seconds, reason);
    }
}
