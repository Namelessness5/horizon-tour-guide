namespace HorizonGuide.Forza;

/// <summary>
/// Forza Data Out 的包布局。
///
/// 三种：
///   Sled       232 字节，没有坐标；
///   CarDash    311 字节（FM7），Dash 段紧接 Sled；
///   HorizonV2  324 字节（FH4 / FH5 / FH6），Sled 之后有 12 字节填充，Dash 段从 244 开始。
///
/// FH6 已实机确认沿用 HorizonV2：324 字节，坐标在 244/248/252，速度在 256。
/// 单位是米，X 约 -7200..5000，Z 约 -9500..9200，Y 是海拔（海平面约 100，不是 0）。
/// </summary>
public sealed record ForzaPacketLayout(
    string Name,
    int PacketLength,
    int DashOffset)
{
    /// <summary>Dash 段内的相对偏移。</summary>
    private const int PositionXInDash = 0;
    private const int PositionYInDash = 4;
    private const int PositionZInDash = 8;
    private const int SpeedInDash = 12;

    public int PositionXOffset => DashOffset + PositionXInDash;
    public int PositionYOffset => DashOffset + PositionYInDash;
    public int PositionZOffset => DashOffset + PositionZInDash;
    public int SpeedOffset => DashOffset + SpeedInDash;

    public const int IsRaceOnOffset = 0;
    public const int TimestampMsOffset = 4;

    public static readonly ForzaPacketLayout CarDash =
        new("CarDash (FM7)", PacketLength: 311, DashOffset: 232);

    public static readonly ForzaPacketLayout HorizonV2 =
        new("Horizon V2 (FH4/FH5/FH6)", PacketLength: 324, DashOffset: 244);

    public static ForzaPacketLayout? TryFromPacketLength(int length) => length switch
    {
        311 => CarDash,
        324 => HorizonV2,
        _ => null,
    };
}
