using System.Text.Json;
using System.Text.Json.Serialization;

namespace HorizonGuide.Core.Content;

/// <summary>
/// 从 content/content-index.json 加载内容。
///
/// 运行时**只**读这一个文件。content/facts/ 和 content/scripts/ 是内容制作的中间产物，
/// 字段会随 pipeline 演进而变，运行时不该碰。内容格式改版时，只有这个文件需要改
/// （设计文档 §12）。
/// </summary>
public sealed class ContentStore
{
    private readonly Dictionary<string, List<PlayableContent>> _byLocation;
    private readonly Dictionary<(string PlaybackId, string Lang), PlayableContent> _byPlaybackAndLang;

    public ContentStore(IReadOnlyList<PlayableContent> content)
    {
        All = content;
        _byLocation = content
            .GroupBy(c => c.LocationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        _byPlaybackAndLang = content
            .GroupBy(c => (c.PlaybackId, c.Lang))
            .ToDictionary(g => g.Key, g => g.First());
    }

    public IReadOnlyList<PlayableContent> All { get; }

    /// <summary>某个地点的全部内容（所有语言）。没有内容的地点返回空。</summary>
    public IReadOnlyList<PlayableContent> ForLocation(string locationId) =>
        _byLocation.TryGetValue(locationId, out var list) ? list : [];

    public PlayableContent? VariantFor(PlayableContent content, string lang) =>
        _byPlaybackAndLang.TryGetValue((content.PlaybackId, lang), out var variant)
            ? variant
            : null;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static ContentStore Load(string path)
    {
        var file = JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(path), Options)
                   ?? throw new InvalidDataException($"内容索引读不出来：{path}");

        return new ContentStore(file.Content);
    }

    private sealed class IndexFile
    {
        [JsonPropertyName("content")]
        public List<PlayableContent> Content { get; init; } = [];
    }
}
