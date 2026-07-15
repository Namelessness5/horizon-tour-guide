using HorizonGuide.Forza;

namespace HorizonGuide.Tools.TelemetryProbe;

public readonly record struct FloatSlot(int Offset, float Value, bool Changing);

/// <summary>
/// 把包里每个 4 字节偏移都读成 float，并记录它最近是否在变。
/// 包长度未知时靠这个人工识别坐标字段。
/// </summary>
public sealed class PacketScanner
{
    private readonly Lock _gate = new();
    private byte[]? _latest;
    private float[]? _previous;
    private int[]? _changedAtPacket;
    private int _packetIndex;

    public int PacketLength
    {
        get
        {
            lock (_gate)
                return _latest?.Length ?? 0;
        }
    }

    public void Observe(byte[] packet)
    {
        lock (_gate)
        {
            var slots = packet.Length / 4;

            if (_previous is null || _previous.Length != slots)
            {
                _previous = new float[slots];
                _changedAtPacket = new int[slots];
                for (var i = 0; i < slots; i++)
                    _changedAtPacket[i] = int.MinValue;
            }

            _packetIndex++;

            for (var i = 0; i < slots; i++)
            {
                var value = ForzaPacketParser.ReadSingle(packet, i * 4);
                if (!NearlyEqual(value, _previous[i]))
                    _changedAtPacket![i] = _packetIndex;
                _previous[i] = value;
            }

            _latest = packet;
        }
    }

    /// <summary>最近 30 个包内变过的，标记为“在变”。</summary>
    public FloatSlot[] Snapshot()
    {
        lock (_gate)
        {
            if (_latest is null || _previous is null || _changedAtPacket is null)
                return [];

            var slots = _previous.Length;
            var result = new FloatSlot[slots];
            for (var i = 0; i < slots; i++)
            {
                var changing = _packetIndex - _changedAtPacket[i] <= 30;
                result[i] = new FloatSlot(i * 4, _previous[i], changing);
            }
            return result;
        }
    }

    private static bool NearlyEqual(float a, float b)
    {
        if (float.IsNaN(a) && float.IsNaN(b))
            return true;
        return a.Equals(b);
    }
}
