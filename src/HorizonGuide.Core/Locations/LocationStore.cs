using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorizonGuide.Core.Locations;

/// <summary>
/// 从 JSON 加载地点。勘景草稿和正式地点数据是同一套字段，所以两个文件都能直接读。
/// </summary>
public static class LocationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed class LocationJson
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Dictionary<string, string> Names { get; set; } = [];
        public string Type { get; set; } = "landmark";
        public string? ParentId { get; set; }
        public int Priority { get; set; } = 100;
        public Point2D? Center { get; set; }
        public List<Point2D> Boundary { get; set; } = [];
    }

    public static List<Location> Load(string path)
    {
        var raw = JsonSerializer.Deserialize<List<LocationJson>>(File.ReadAllText(path), JsonOptions)
            ?? [];

        return raw.Select(l => new Location
        {
            Id = l.Id,
            Name = string.IsNullOrWhiteSpace(l.Name) ? l.Id : l.Name,
            Names = l.Names,
            Type = l.Type.Equals("region", StringComparison.OrdinalIgnoreCase)
                ? LocationType.Region
                : LocationType.Landmark,
            ParentId = l.ParentId,
            Priority = l.Priority,
            Center = l.Center,
            Boundary = l.Boundary,
        }).ToList();
    }
}
