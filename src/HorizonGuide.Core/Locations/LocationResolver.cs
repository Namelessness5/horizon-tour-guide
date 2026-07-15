namespace HorizonGuide.Core.Locations;

/// <summary>
/// 根据车辆的 X/Z 判断当前位置栈。第一版不用高度参与判断。
/// </summary>
public sealed class LocationResolver
{
    private readonly IReadOnlyList<Location> _locations;
    private readonly Dictionary<string, Location> _byId;
    private readonly Dictionary<string, int> _depthById;

    public LocationResolver(IReadOnlyList<Location> locations)
    {
        _locations = locations;
        _byId = locations.ToDictionary(l => l.Id, StringComparer.Ordinal);
        _depthById = locations.ToDictionary(l => l.Id, ComputeDepth, StringComparer.Ordinal);
    }

    public IReadOnlyList<Location> Locations => _locations;

    /// <summary>沿 parentId 往上数几层。父地点缺失或成环时就地停下。</summary>
    private int ComputeDepth(Location location)
    {
        var depth = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal) { location.Id };
        var current = location;

        while (current.ParentId is { } parentId
               && _byId.TryGetValue(parentId, out var parent)
               && seen.Add(parentId))
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    public int DepthOf(Location location) =>
        _depthById.TryGetValue(location.Id, out var depth) ? depth : 0;

    /// <summary>
    /// 多个地点同时命中时的排序：层级更深 → priority 更高 → 离中心点更近。
    /// 排第一的是当前主要地点。
    /// </summary>
    public LocationStack Resolve(float x, float z)
    {
        var primary = _locations
            .Where(l => l.Contains(x, z))
            .OrderByDescending(DepthOf)
            .ThenByDescending(l => l.Priority)
            .ThenBy(l => l.DistanceFrom(x, z))
            .FirstOrDefault();

        if (primary is null)
            return LocationStack.Empty;

        // 沿 parentId 把父级补齐。父多边形不一定几何上包住车辆
        // （地区的多边形画得很粗，景观可能露在外面），所以不做几何校验。
        var chain = new List<Location> { primary };
        var seen = new HashSet<string>(StringComparer.Ordinal) { primary.Id };
        var current = primary;

        while (current.ParentId is { } parentId
               && _byId.TryGetValue(parentId, out var parent)
               && seen.Add(parentId))
        {
            chain.Add(parent);
            current = parent;
        }

        chain.Reverse();
        return new LocationStack(chain);
    }
}
