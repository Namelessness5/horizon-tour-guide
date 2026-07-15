using System.Buffers.Binary;

namespace HorizonGuide.Forza;

public static class ForzaPacketParser
{
    public static VehicleState Parse(ReadOnlySpan<byte> packet, ForzaPacketLayout layout)
    {
        if (packet.Length < layout.PacketLength)
            throw new ArgumentException(
                $"包长度 {packet.Length} 小于布局 {layout.Name} 要求的 {layout.PacketLength}。",
                nameof(packet));

        return new VehicleState
        {
            IsRaceOn = ReadInt32(packet, ForzaPacketLayout.IsRaceOnOffset) != 0,
            TimestampMs = ReadUInt32(packet, ForzaPacketLayout.TimestampMsOffset),
            PositionX = ReadSingle(packet, layout.PositionXOffset),
            PositionY = ReadSingle(packet, layout.PositionYOffset),
            PositionZ = ReadSingle(packet, layout.PositionZOffset),
            Speed = ReadSingle(packet, layout.SpeedOffset),
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public static float ReadSingle(ReadOnlySpan<byte> packet, int offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(offset, 4));

    public static int ReadInt32(ReadOnlySpan<byte> packet, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset, 4));

    public static uint ReadUInt32(ReadOnlySpan<byte> packet, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(offset, 4));
}
