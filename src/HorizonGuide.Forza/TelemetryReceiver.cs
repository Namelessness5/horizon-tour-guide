using System.Net;
using System.Net.Sockets;

namespace HorizonGuide.Forza;

/// <summary>
/// 接收 FH6 Data Out 的 UDP 包，只维护一份最新车辆状态。
/// 不排队、不保存历史帧；上层每 200ms 读一次 <see cref="LatestState"/> 即可。
/// </summary>
public sealed class TelemetryReceiver : IDisposable
{
    private readonly int _port;
    private readonly TimeSpan _staleAfter;
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    private volatile VehicleState? _latest;
    private long _packetCount;
    private volatile ForzaPacketLayout? _layout;

    public TelemetryReceiver(int port = 5300, TimeSpan? staleAfter = null)
    {
        _port = port;
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(2);
    }

    public VehicleState? LatestState => _latest;

    public long PacketCount => Interlocked.Read(ref _packetCount);

    /// <summary>已识别出的包布局；收到第一个已知长度的包之后才有值。</summary>
    public ForzaPacketLayout? Layout => _layout;

    public bool IsStale =>
        _latest is null || DateTime.UtcNow - _latest.UpdatedAt > _staleAfter;

    /// <summary>收到长度无法识别的包时触发（每种长度只报一次）。</summary>
    public event Action<int>? UnknownPacketLength;

    /// <summary>收到并成功解析一个包。探针工具用它做原始数据落盘。</summary>
    public event Action<byte[]>? PacketReceived;

    public void Start()
    {
        if (_loop is not null)
            throw new InvalidOperationException("接收器已经启动。");

        _client = new UdpClient(_port);
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var reportedLengths = new HashSet<int>();

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _client!.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            var packet = result.Buffer;
            Interlocked.Increment(ref _packetCount);
            PacketReceived?.Invoke(packet);

            var layout = ForzaPacketLayout.TryFromPacketLength(packet.Length);
            if (layout is null)
            {
                if (reportedLengths.Add(packet.Length))
                    UnknownPacketLength?.Invoke(packet.Length);
                continue;
            }

            _layout = layout;
            _latest = ForzaPacketParser.Parse(packet, layout);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _client?.Dispose();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // 关闭时的取消异常，忽略。
        }
        _cts?.Dispose();
    }

    /// <summary>本机所有可用 IPv4 地址，用于提示游戏该往哪个 IP 发。</summary>
    public static IEnumerable<string> LocalIPv4Addresses() =>
        Dns.GetHostAddresses(Dns.GetHostName())
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString());
}
