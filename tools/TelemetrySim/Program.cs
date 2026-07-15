using System.Net.Sockets;
using System.Text.Json;
using HorizonGuide.Core.Locations;

namespace HorizonGuide.Tools.TelemetrySim;

/// <summary>
/// 假的 Data Out：按 FH6 的包格式往 5300 端口发遥测，模拟开车穿过某个地点。
///
/// 为什么需要它：整条播放链路（定位 → 时间预算 → 选内容 → 放音频 → 字幕）
/// 只有开着游戏才能验，而调一次参数就要进一次游戏、开到那个地点。
/// 有了它就能在桌面上把链路跑通，游戏里只用来验最后一公里。
///
/// 用法：
///     dotnet run --project tools/TelemetrySim -- SHIBUYA_CROSSING --speed 60
///
///     --speed  km/h。默认 60。
///              涩谷那个多边形只有 150-200 米宽，150 km/h 过去只有 4 秒——
///              拿不同速度跑，看程序会不会挑不同长度的内容。
/// </summary>
public static class Program
{
    private const int Port = 5300;

    // Horizon V2 布局：324 字节，坐标在 244/248/252，速度在 256。
    private const int PacketSize = 324;
    private const int IsRaceOnOffset = 0;
    private const int TimestampOffset = 4;
    private const int PositionXOffset = 244;
    private const int PositionYOffset = 248;
    private const int PositionZOffset = 252;
    private const int SpeedOffset = 256;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var id = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "SHIBUYA_CROSSING";
        var kmh = ArgFloat(args, "--speed", 60f);

        var root = FindProjectRoot();
        var locations = LocationStore.Load(Path.Combine(root, "data", "survey-drafts.json"));
        var target = locations.FirstOrDefault(l => l.Id == id);

        if (target is null)
        {
            Console.Error.WriteLine($"没有这个地点：{id}");
            Console.Error.WriteLine("已有：" + string.Join(", ", locations.Select(l => l.Id)));
            return 1;
        }

        // 穿过多边形的直线：从形心两侧各退 300 米，保证起点和终点都在外面
        // ——这样能测到"进入"和"离开"两次状态变化，而不只是在里面打转。
        var center = Polygon.Centroid(target.Boundary);
        var (dx, dz) = (1f, 0.35f);
        var length = MathF.Sqrt(dx * dx + dz * dz);
        dx /= length;
        dz /= length;

        const float lead = 300f;
        var startX = center.X - dx * lead;
        var startZ = center.Z - dz * lead;

        var speed = kmh / 3.6f;               // 包里的速度是 m/s
        const float hz = 60f;                 // 游戏大约 60Hz 发包
        var step = speed / hz;                // 每帧走多远
        var totalDistance = lead * 2;
        var frames = (int)(totalDistance / step);

        Console.WriteLine($"目标：{target.Name} ({target.Id})");
        Console.WriteLine($"车速：{kmh:F0} km/h（{speed:F1} m/s）");
        Console.WriteLine($"路径：穿过形心，全长 {totalDistance:F0} 米，{frames} 帧");

        var inside = 0;
        for (var i = 0; i < frames; i++)
        {
            var x = startX + dx * step * i;
            var z = startZ + dz * step * i;
            if (target.Contains(x, z))
                inside++;
        }

        Console.WriteLine($"其中 {inside} 帧在多边形内 = 停留 {inside / hz:F1} 秒");
        Console.WriteLine($"发往 127.0.0.1:{Port} …\n");

        using var udp = new UdpClient();
        udp.Connect("127.0.0.1", Port);

        var packet = new byte[PacketSize];
        var frameDelay = TimeSpan.FromSeconds(1.0 / hz);
        var next = DateTime.UtcNow;
        var wasInside = false;

        for (var i = 0; i < frames; i++)
        {
            var x = startX + dx * step * i;
            var z = startZ + dz * step * i;

            BitConverter.TryWriteBytes(packet.AsSpan(IsRaceOnOffset), 1);
            BitConverter.TryWriteBytes(packet.AsSpan(TimestampOffset), (uint)(i * 1000 / hz));
            BitConverter.TryWriteBytes(packet.AsSpan(PositionXOffset), x);
            BitConverter.TryWriteBytes(packet.AsSpan(PositionYOffset), 113f);
            BitConverter.TryWriteBytes(packet.AsSpan(PositionZOffset), z);
            BitConverter.TryWriteBytes(packet.AsSpan(SpeedOffset), speed);

            udp.Send(packet);

            var nowInside = target.Contains(x, z);
            if (nowInside != wasInside)
            {
                Console.WriteLine($"  [{i / hz,5:F1}s] {(nowInside ? "进入" : "离开")} "
                                  + $"{target.Name}  ({x:F0}, {z:F0})");
                wasInside = nowInside;
            }

            next += frameDelay;
            var wait = next - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                Thread.Sleep(wait);
        }

        Console.WriteLine("\n跑完了。");
        return 0;
    }

    private static float ArgFloat(string[] args, string name, float fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], out var v)
            ? v
            : fallback;
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "data")))
                return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return Directory.GetCurrentDirectory();
    }
}
