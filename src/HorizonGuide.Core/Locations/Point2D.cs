namespace HorizonGuide.Core.Locations;

/// <summary>世界平面坐标，单位米。第一版不用高度参与判断。</summary>
public readonly record struct Point2D(float X, float Z)
{
    public float DistanceTo(Point2D other) => DistanceTo(other.X, other.Z);

    public float DistanceTo(float x, float z)
    {
        var dx = X - x;
        var dz = Z - z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
