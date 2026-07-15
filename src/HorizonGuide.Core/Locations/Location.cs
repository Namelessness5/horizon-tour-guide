namespace HorizonGuide.Core.Locations;

public enum LocationType
{
    Region,
    Landmark,
}

/// <summary>
/// 一个地区或景观。只描述空间关系，不含故事正文。
/// 和勘景草稿是同一套 JSON 字段，草稿检查通过后可以直接当地点数据加载。
/// </summary>
public sealed class Location
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Names { get; init; } = new Dictionary<string, string>();
    public LocationType Type { get; init; } = LocationType.Landmark;

    /// <summary>父地点。位置栈靠它建立，不要求父多边形几何上包住本地点。</summary>
    public string? ParentId { get; init; }

    public int Priority { get; init; } = 100;

    /// <summary>
    /// 中心点。用于距离排序和调试。
    /// 允许在多边形之外：多边形是能开车经过的触发区，中心点是要讲的那个东西
    /// （比如海里的鸟居开不进去，多边形画在岸边的路上）。
    /// </summary>
    public Point2D? Center { get; init; }

    public IReadOnlyList<Point2D> Boundary { get; init; } = [];

    public bool HasPolygon => Boundary.Count >= 3;

    public bool Contains(float x, float z) => HasPolygon && Polygon.Contains(Boundary, x, z);

    public string DisplayName(string? language)
    {
        static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var lang = (language ?? "")
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(lang)
            && Names.TryGetValue(lang, out var localized)
            && Clean(localized) is { } display)
            return display;

        if (Names.TryGetValue("zh", out var zh) && Clean(zh) is { } chinese)
            return chinese;

        return Clean(Name) ?? Id;
    }

    /// <summary>到中心点的距离；没有中心点时退回多边形形心。</summary>
    public float DistanceFrom(float x, float z)
    {
        var reference = Center ?? (HasPolygon ? Polygon.Centroid(Boundary) : new Point2D(x, z));
        return reference.DistanceTo(new Point2D(x, z));
    }
}
