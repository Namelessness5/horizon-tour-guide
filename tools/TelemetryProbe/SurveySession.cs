using System.Text.Json;
using System.Text.Json.Serialization;
using HorizonGuide.Core.Locations;
using HorizonGuide.Forza;

namespace HorizonGuide.Tools.TelemetryProbe;

/// <summary>
/// 一个采集草稿。人工检查、补上 id / name / parentId 之后再合并进 locations.json。
/// </summary>
public sealed class LocationDraft
{
    public string Id { get; set; } = "";

    /// <summary>纯地名，**会直接显示在玩家屏幕上**。别往里塞说明。</summary>
    public string Name { get; set; } = "";
    public Dictionary<string, string>? Names { get; set; }

    /// <summary>
    /// 给取材用的备注：这地方在现实里是什么、原型是谁、哪里对不上。
    ///
    /// 和 <see cref="Name"/> 分开是因为 Name 是**给玩家看的标签**，而这些话是
    /// **给 gather 看的线索**。写进 Name 的话，玩家开过去会看到屏幕上弹出
    /// "雷鸟酒店(实际不存在，可能参考了富山县…)"。
    ///
    /// gather 会拿它当 --hint 的默认值。运行时不读这个字段。
    /// </summary>
    public string? Note { get; set; }

    public string Type { get; set; } = "landmark";
    public string? ParentId { get; set; }
    public int Priority { get; set; } = 100;
    public Point2D? Center { get; set; }

    /// <summary>记录中心点时的海拔，只作参考，第一版不参与判断。</summary>
    public float? CenterY { get; set; }

    public List<Point2D> Boundary { get; set; } = [];
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}

public static class DraftStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<LocationDraft> Load(string path)
    {
        if (!File.Exists(path))
            return [];

        return JsonSerializer.Deserialize<List<LocationDraft>>(File.ReadAllText(path), JsonOptions)
            ?? [];
    }

    public static void Save(string path, List<LocationDraft> drafts)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(drafts, JsonOptions));
    }
}

/// <summary>
/// 勘景模式的状态机：记中心点、加边界点、撤销、完成。
/// </summary>
public sealed class SurveySession
{
    private readonly Lock _gate = new();
    private readonly string _draftsPath;
    private readonly List<LocationDraft> _completed;

    private LocationDraft _current = new();
    private LocationDraft? _awaitingName;
    private string _message = "F8 记中心点   F9 加边界点   F7 撤销上一个边界点   F10 完成";

    public SurveySession(string draftsPath)
    {
        _draftsPath = draftsPath;
        _completed = DraftStore.Load(draftsPath);
    }

    public LocationDraft Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>F10 之后、输入名字之前，采集暂停，控制台让给 ReadLine。</summary>
    public bool AwaitingName
    {
        get { lock (_gate) return _awaitingName is not null; }
    }

    public IReadOnlyList<LocationDraft> Completed
    {
        get { lock (_gate) return _completed.ToList(); }
    }

    public string Message
    {
        get { lock (_gate) return _message; }
    }

    public void MarkCenter(VehicleState? vehicle)
    {
        lock (_gate)
        {
            if (!Require(vehicle, out var v))
                return;

            _current.Center = new Point2D(v.PositionX, v.PositionZ);
            _current.CenterY = v.PositionY;
            _message = $"中心点：X {v.PositionX:F1}  Z {v.PositionZ:F1}  （海拔 {v.PositionY:F0}）";
        }
    }

    public void AddBoundaryPoint(VehicleState? vehicle)
    {
        lock (_gate)
        {
            if (!Require(vehicle, out var v))
                return;

            _current.Boundary.Add(new Point2D(v.PositionX, v.PositionZ));
            _message = $"边界点 #{_current.Boundary.Count}：X {v.PositionX:F1}  Z {v.PositionZ:F1}";
        }
    }

    public void UndoBoundaryPoint()
    {
        lock (_gate)
        {
            if (_current.Boundary.Count == 0)
            {
                _message = "没有可撤销的边界点。";
                return;
            }

            _current.Boundary.RemoveAt(_current.Boundary.Count - 1);
            _message = $"已撤销，剩 {_current.Boundary.Count} 个边界点。";
        }
    }

    /// <summary>
    /// 完成当前地点：校验几何，然后等待在控制台输入名字。
    ///
    /// 边界点保持打点顺序不变——按顺序绕边界打点可以画出凹形，重排会把凹形拉平。
    /// 只有在打点顺序绕出自交时才按角度重排兜底。
    /// </summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (_awaitingName is not null)
                return;

            if (_current.Center is null && _current.Boundary.Count == 0)
            {
                _message = "当前草稿是空的，没什么可保存。";
                return;
            }

            if (_current.Boundary.Count is > 0 and < 3)
            {
                _message = $"只有 {_current.Boundary.Count} 个边界点，构不成多边形。继续加点，或 F7 撤销清空后只保存中心点。";
                return;
            }

            var repaired = "";
            if (Polygon.FindSelfIntersection(_current.Boundary) is { } edges)
            {
                var before = Polygon.Area(_current.Boundary);
                _current.Boundary = Polygon.SortByAngle(_current.Boundary);
                repaired =
                    $"\n[修复] 第 {edges.EdgeA} 条边和第 {edges.EdgeB} 条边交叉，已按角度重排：" +
                    $"面积 {before:N0} → {Polygon.Area(_current.Boundary):N0} m²。" +
                    "\n       重排会把凹形拉平。如果这个地点是凹的，取消保存后按边界顺序重打。";
            }

            _awaitingName = _current;
            _message = $"{_current.Boundary.Count} 个边界点，面积 {Polygon.Area(_current.Boundary):N0} m²{repaired}";
        }
    }

    /// <summary>
    /// 把「名字(说明)」拆成 Name 和 Note。
    ///
    /// 采集时是一行输入，把原型、哪里对不上随手写在括号里最顺手——但 Name 会
    /// **直接显示在玩家屏幕上**，整段说明跟着弹出来就不像话了。所以在这里拆：
    /// 括号外的给玩家看，括号里的给 gather 当消歧线索。
    ///
    /// 全角半角括号都认——中文输入法打出来的是全角。
    /// </summary>
    public static (string Name, string? Note) SplitNote(string text)
    {
        text = text.Trim();

        var open = text.IndexOfAny(['(', '（']);
        var close = text.LastIndexOfAny([')', '）']);
        if (open < 0 || close < open)
            return (text, null);

        var name = text[..open].Trim();
        var note = text[(open + 1)..close].Trim();

        // 括号打在最前面，那整个名字都在括号里，没什么可拆的
        if (name.Length == 0)
            return (text, null);

        return (name, note.Length > 0 ? note : null);
    }

    /// <summary>
    /// 用控制台输入的一行给待保存的草稿命名并落盘。
    /// 格式：<c>id 名称[(说明)] [region|landmark]</c>，例如
    /// <c>hotel_thunderbird 雷鸟酒店(虚构，原型是立山黑部的室堂) landmark</c>。
    /// 括号里的说明会拆进 <see cref="LocationDraft.Note"/>，不进 Name。
    /// 空行则用自动 id。输入 <c>cancel</c> 退回继续采集。
    /// </summary>
    public void ApplyName(string? line)
    {
        lock (_gate)
        {
            if (_awaitingName is not { } draft)
                return;

            line = line?.Trim() ?? "";

            if (line.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                _awaitingName = null;
                _message = "已取消保存，可以继续加点。";
                return;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var type = "landmark";
            if (parts.Length > 1 && parts[^1] is "region" or "landmark")
            {
                type = parts[^1];
                parts = parts[..^1];
            }

            draft.Id = parts.Length > 0 ? parts[0] : $"draft_{_completed.Count + 1:D3}";
            (draft.Name, draft.Note) = SplitNote(
                parts.Length > 1 ? string.Join(' ', parts[1..]) : draft.Id);
            draft.Type = type;

            _completed.Add(draft);
            DraftStore.Save(_draftsPath, _completed);

            var centerOutside = draft.Center is { } c && !Polygon.Contains(draft.Boundary, c)
                ? "（中心点在多边形外——如果多边形是触发区、中心点是被讲的东西，这是对的）"
                : "";

            var note = draft.Note is null ? "" : $"\n       备注（取材用，不显示给玩家）：{draft.Note}";

            _message = $"已保存 {draft.Id}「{draft.Name}」[{draft.Type}] {centerOutside}{note}";
            _awaitingName = null;
            _current = new LocationDraft();
        }
    }

    /// <summary>
    /// 只接受真正在开车时的坐标。
    ///
    /// 暂停、菜单和读盘时游戏照样发包，但坐标是 (0, 0, 0)。把这种点记进边界，
    /// 多边形会被拽到地图原点，面积和形状全错。
    /// </summary>
    private bool Require(VehicleState? vehicle, out VehicleState value)
    {
        value = vehicle!;

        if (vehicle is null)
        {
            _message = "还没收到遥测数据，先开起来。";
            return false;
        }

        if (DateTime.UtcNow - vehicle.UpdatedAt > StaleAfter)
        {
            _message = "遥测已经过期，游戏还在发包吗？";
            return false;
        }

        if (!vehicle.IsRaceOn || (vehicle.PositionX == 0 && vehicle.PositionZ == 0))
        {
            _message = "[!] 游戏不在驾驶状态（暂停 / 菜单 / 读盘），坐标是 (0, 0)，这个点没记。回到开车状态再按。";
            return false;
        }

        return true;
    }

    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);
}
