namespace HorizonGuide.Core.Locations;

public static class Polygon
{
    /// <summary>
    /// point-in-polygon（射线法）。顶点顺序无所谓，但多边形必须是不自交的。
    /// </summary>
    public static bool Contains(IReadOnlyList<Point2D> polygon, float x, float z)
    {
        if (polygon.Count < 3)
            return false;

        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];

            if ((a.Z > z) != (b.Z > z))
            {
                var crossX = a.X + (z - a.Z) / (b.Z - a.Z) * (b.X - a.X);
                if (x < crossX)
                    inside = !inside;
            }
        }

        return inside;
    }

    public static bool Contains(IReadOnlyList<Point2D> polygon, Point2D point) =>
        Contains(polygon, point.X, point.Z);

    /// <summary>
    /// 从多边形内的一点沿方向 (dx, dz) 射出，返回走到边界还有多远（米）。
    ///
    /// 用来估算"车还会在这个地点里待多久"——这个距离除以车速就是剩余时长，
    /// 决定了现在该播 8 秒的还是 40 秒的内容。
    ///
    /// 方向向量不必是单位向量。起点在多边形外、或者射线打不到任何边（数值退化）
    /// 时返回 null。
    /// </summary>
    public static float? RayExitDistance(
        IReadOnlyList<Point2D> polygon, float x, float z, float dx, float dz)
    {
        var length = MathF.Sqrt(dx * dx + dz * dz);
        if (polygon.Count < 3 || length < 1e-6f)
            return null;

        if (!Contains(polygon, x, z))
            return null;

        dx /= length;
        dz /= length;

        // 可能穿出多条边（凹多边形），取最近的那个交点。
        var nearest = float.MaxValue;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];

            var ex = b.X - a.X;
            var ez = b.Z - a.Z;

            // 射线 P + t*D 与边 A + u*E 求交；denom 为 0 表示平行。
            var denom = dx * ez - dz * ex;
            if (MathF.Abs(denom) < 1e-9f)
                continue;

            var t = ((a.X - x) * ez - (a.Z - z) * ex) / denom;
            var u = ((a.X - x) * dz - (a.Z - z) * dx) / denom;

            // t > 0：交点在射线前方（不要背后的）。0 <= u <= 1：落在这条边上。
            if (t > 1e-4f && u >= 0f && u <= 1f && t < nearest)
                nearest = t;
        }

        return nearest < float.MaxValue ? nearest : null;
    }

    /// <summary>有向面积。逆时针为正。</summary>
    public static float SignedArea(IReadOnlyList<Point2D> polygon)
    {
        if (polygon.Count < 3)
            return 0;

        var sum = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += (double)a.X * b.Z - (double)b.X * a.Z;
        }

        return (float)(sum / 2.0);
    }

    public static float Area(IReadOnlyList<Point2D> polygon) => MathF.Abs(SignedArea(polygon));

    /// <summary>面积形心。面积退化为 0 时退回顶点平均值。</summary>
    public static Point2D Centroid(IReadOnlyList<Point2D> polygon)
    {
        var area = SignedArea(polygon);
        if (polygon.Count < 3 || MathF.Abs(area) < 1e-3f)
            return VertexMean(polygon);

        double cx = 0, cz = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var cross = (double)a.X * b.Z - (double)b.X * a.Z;
            cx += (a.X + b.X) * cross;
            cz += (a.Z + b.Z) * cross;
        }

        return new Point2D((float)(cx / (6 * area)), (float)(cz / (6 * area)));
    }

    public static Point2D VertexMean(IReadOnlyList<Point2D> polygon)
    {
        if (polygon.Count == 0)
            return new Point2D(0, 0);

        double x = 0, z = 0;
        foreach (var p in polygon)
        {
            x += p.X;
            z += p.Z;
        }

        return new Point2D((float)(x / polygon.Count), (float)(z / polygon.Count));
    }

    /// <summary>
    /// 绕顶点重心按角度排序。得到的一定是不自交的多边形。
    ///
    /// 只在打点顺序绕出自交时才用它兜底：它把地点当成星形，会把凹进去的部分拉平，
    /// 顶点越多丢的形状越多。按边界顺序打的点应当原样保留。
    /// </summary>
    public static List<Point2D> SortByAngle(IReadOnlyList<Point2D> polygon)
    {
        var center = VertexMean(polygon);
        return polygon
            .OrderBy(p => MathF.Atan2(p.Z - center.Z, p.X - center.X))
            .ToList();
    }

    /// <summary>找出第一对相交的不相邻边，用于判断多边形是否自交。</summary>
    public static (int EdgeA, int EdgeB)? FindSelfIntersection(IReadOnlyList<Point2D> polygon)
    {
        var n = polygon.Count;
        if (n < 4)
            return null;

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                // 相邻边共享顶点，必然“相交”，跳过。
                if (j == i + 1 || (i == 0 && j == n - 1))
                    continue;

                if (SegmentsCross(
                        polygon[i], polygon[(i + 1) % n],
                        polygon[j], polygon[(j + 1) % n]))
                {
                    return (i, j);
                }
            }
        }

        return null;
    }

    private static bool SegmentsCross(Point2D a, Point2D b, Point2D c, Point2D d)
    {
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);

        return (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0);
    }

    private static float Cross(Point2D origin, Point2D p, Point2D q) =>
        (p.X - origin.X) * (q.Z - origin.Z) - (p.Z - origin.Z) * (q.X - origin.X);
}
