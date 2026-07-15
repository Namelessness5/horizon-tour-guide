using HorizonGuide.Core.Locations;

namespace HorizonGuide.Tools.TelemetryProbe;

/// <summary>
/// 离线检查草稿几何：自交、面积、中心点是否在多边形内、地点之间的包含关系。
/// --check 会顺便把自交的多边形按角度重排修好（--check --write 才写回文件）。
/// </summary>
public static class DraftChecker
{
    public static int Run(string draftsPath, bool write)
    {
        var drafts = DraftStore.Load(draftsPath);
        if (drafts.Count == 0)
        {
            Console.WriteLine($"{Path.GetFullPath(draftsPath)} 里没有草稿。");
            return 1;
        }

        Console.WriteLine($"{Path.GetFullPath(draftsPath)}：{drafts.Count} 个草稿");
        Console.WriteLine();

        var repaired = 0;

        foreach (var draft in drafts)
        {
            Console.WriteLine($"{draft.Id}  {draft.Name}  [{draft.Type}]");

            if (draft.Boundary.Count < 3)
            {
                Console.WriteLine($"  [!] 只有 {draft.Boundary.Count} 个边界点，构不成多边形。");
                Console.WriteLine();
                continue;
            }

            // (0,0) 是暂停 / 菜单 / 读盘时的全零遥测包，不是地图上的点。
            var origins = draft.Boundary.Count(p => p.X == 0 && p.Z == 0);
            if (origins > 0)
            {
                Console.WriteLine(
                    $"  [!] 有 {origins} 个边界点是 (0, 0) —— 那是游戏暂停时的空坐标，不是地图上的点。" +
                    "\n      这个点会把多边形拽到地图原点。删掉它，或者重采这个地点。");
            }

            var before = Polygon.Area(draft.Boundary);
            var crossing = Polygon.FindSelfIntersection(draft.Boundary);

            if (crossing is { } edges)
            {
                draft.Boundary = Polygon.SortByAngle(draft.Boundary);
                repaired++;
                Console.WriteLine(
                    $"  [修复] 第 {edges.EdgeA} 条边和第 {edges.EdgeB} 条边交叉，已按角度重排：" +
                    $"面积 {before:N0} → {Polygon.Area(draft.Boundary):N0} m²");
            }

            var area = Polygon.Area(draft.Boundary);
            Console.WriteLine($"  {draft.Boundary.Count} 个边界点   面积 {area:N0} m²");

            if (draft.Center is { } center)
            {
                var inside = Polygon.Contains(draft.Boundary, center);
                var centroid = Polygon.Centroid(draft.Boundary);
                Console.WriteLine(
                    $"  中心点 ({center.X:F0}, {center.Z:F0})  " +
                    (inside ? "在多边形内" : $"[!] 在多边形外（形心是 ({centroid.X:F0}, {centroid.Z:F0})）"));
            }
            else
            {
                Console.WriteLine("  [!] 没有中心点。");
            }

            Console.WriteLine();
        }

        var reparented = ResolveParents(drafts);

        var changes = repaired + reparented;
        if (changes > 0)
        {
            Console.WriteLine();
            if (write)
            {
                DraftStore.Save(draftsPath, drafts);
                Console.WriteLine($"已写回 {draftsPath}：修复 {repaired} 个多边形，推断 {reparented} 个 parentId。");
            }
            else
            {
                Console.WriteLine($"待写入：修复 {repaired} 个多边形，推断 {reparented} 个 parentId。加 --write 写回文件。");
            }
        }

        return 0;
    }

    /// <summary>
    /// 推断 parentId：子地点的所有顶点都在某个多边形内即视为被它包含，
    /// 有多个候选时取面积最小的那个（最近的一层）。
    ///
    /// 部分重叠不算包含 —— 那说明父多边形画小了，报出来让人工重采，
    /// 不去猜，猜错了位置栈会是错的。
    /// </summary>
    private static int ResolveParents(List<LocationDraft> drafts)
    {
        Console.WriteLine("── 嵌套关系 ──");
        var assigned = 0;
        var any = false;

        foreach (var inner in drafts)
        {
            if (inner.Boundary.Count < 3)
                continue;

            LocationDraft? parent = null;
            var partial = new List<LocationDraft>();

            foreach (var outer in drafts)
            {
                if (ReferenceEquals(inner, outer) || outer.Boundary.Count < 3)
                    continue;

                var inside = inner.Boundary.Count(p => Polygon.Contains(outer.Boundary, p));
                if (inside == 0)
                    continue;

                if (inside < inner.Boundary.Count)
                {
                    partial.Add(outer);
                    continue;
                }

                if (parent is null || Polygon.Area(outer.Boundary) < Polygon.Area(parent.Boundary))
                    parent = outer;
            }

            foreach (var outer in partial)
            {
                any = true;
                var inside = inner.Boundary.Count(p => Polygon.Contains(outer.Boundary, p));
                Console.WriteLine(
                    $"  [!] {inner.Id} 只有 {inside}/{inner.Boundary.Count} 个顶点在 {outer.Id} 内 —— " +
                    "不算包含。父多边形画大一圈再重采，否则嵌套认不出来。");
            }

            if (parent is null)
                continue;

            any = true;
            if (inner.ParentId == parent.Id)
            {
                Console.WriteLine($"  {inner.Id} ⊂ {parent.Id}（parentId 已经是对的）");
            }
            else
            {
                Console.WriteLine($"  {inner.Id} ⊂ {parent.Id}  → parentId = {parent.Id}");
                inner.ParentId = parent.Id;
                assigned++;
            }
        }

        if (!any)
            Console.WriteLine("  没有互相包含的地点。");

        return assigned;
    }
}
