namespace HorizonGuide.Core.Locations;

/// <summary>
/// 位置栈：从范围较大的地区排到更具体的景观。
/// 最后一个是当前主要地点，内容选择从它开始往上找。
/// </summary>
public sealed class LocationStack
{
    public static readonly LocationStack Empty = new([]);

    public LocationStack(IReadOnlyList<Location> locations) => Locations = locations;

    public IReadOnlyList<Location> Locations { get; }

    /// <summary>最具体的地点。不在任何地点内时为 null。</summary>
    public Location? Primary => Locations.Count > 0 ? Locations[^1] : null;

    public bool IsEmpty => Locations.Count == 0;

    public bool SameAs(LocationStack other)
    {
        if (Locations.Count != other.Locations.Count)
            return false;

        for (var i = 0; i < Locations.Count; i++)
        {
            if (Locations[i].Id != other.Locations[i].Id)
                return false;
        }

        return true;
    }

    public override string ToString() =>
        IsEmpty ? "（不在任何地点内）" : string.Join(" → ", Locations.Select(l => l.Name));
}
