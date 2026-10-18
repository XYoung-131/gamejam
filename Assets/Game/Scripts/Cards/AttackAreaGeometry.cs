using System.Collections.Generic;

/// <summary>一个被攻击范围覆盖的格子及该格的基础伤害。</summary>
public struct AttackAreaCell
{
    public HexCoord Coord { get; }
    public int Damage { get; }

    public AttackAreaCell(HexCoord coord, int damage)
    {
        Coord = coord;
        Damage = damage;
    }
}

/// <summary>
/// 攻击范围的纯坐标规则，不依赖当前场景。返回的坐标由调用方再与当前地图求交集。
/// 固定图案按 ConceptMap 的轴向坐标记录，来自四张礼炮轰炸范围图。
/// </summary>
public static class AttackAreaGeometry
{
    private static readonly HexCoord[][] FixedBombardmentPatterns =
    {
        new[] {
            C(-4,1),C(-4,2),C(-3,-1),C(-3,0),C(-3,1),C(-3,2),
            C(-2,-2),C(-2,-1),C(-2,2),C(-2,3),C(-1,-2),C(-1,-1),C(-1,2),C(-1,3),
            C(0,-2),C(0,-1),C(0,2),C(1,-3),C(1,-2),C(1,1),C(1,2),
            C(2,-4),C(2,-3),C(2,0),C(3,-4),C(3,-3),C(3,-2),C(3,-1),C(4,-3),C(4,-2)
        },
        new[] {
            C(-5,1),C(-5,2),C(-5,3),C(-5,4),C(-4,1),C(-4,2),C(-3,1),C(-3,2),
            C(-2,0),C(-2,1),C(-1,-2),C(-1,2),C(-1,3),C(0,-3),C(0,-2),C(0,-1),C(0,1),C(0,2),
            C(1,-3),C(1,1),C(1,2),C(2,-1),C(3,-2),C(3,-1),C(4,-3),C(4,-2),C(4,-1),
            C(5,-4),C(5,-3),C(5,-2),C(5,-1)
        },
        new[] {
            C(-4,0),C(-4,3),C(-4,4),C(-3,-1),C(-3,0),C(-3,3),C(-3,4),
            C(-2,-2),C(-2,-1),C(-2,0),C(-2,1),C(-2,2),C(-2,3),
            C(-1,-2),C(-1,-1),C(-1,0),C(-1,1),C(0,-1),C(0,0),
            C(1,-3),C(1,-2),C(1,0),C(1,1),C(2,-3),C(2,-2),C(2,-1),C(2,0),C(2,1),
            C(3,-4),C(3,-3),C(3,-1),C(3,0),C(4,-4),C(4,-1)
        },
        new[] {
            C(-3,-1),C(-3,0),C(-3,3),C(-3,4),C(-2,-2),C(-2,-1),C(-2,0),C(-2,2),C(-2,3),
            C(-1,-2),C(-1,-1),C(-1,2),C(-1,3),C(1,-3),C(1,-2),C(1,1),C(1,2),
            C(2,-4),C(2,-3),C(2,-2),C(2,0),C(2,1),C(3,-4),C(3,-3),C(3,0),C(3,1)
        }
    };

    private static HexCoord C(int q, int r) => new HexCoord(q, r);

    public static int StraightDirection(HexCoord origin, HexCoord target)
    {
        int distance = origin.Distance(target);
        if (distance == 0) return -1;
        for (int direction = 0; direction < 6; direction++)
            if (Step(origin, direction, distance) == target) return direction;
        return -1;
    }

    public static IReadOnlyList<HexCoord> GetPierce(HexCoord origin, HexCoord target, int depth)
    {
        var result = new List<HexCoord> { target };
        int direction = StraightDirection(origin, target);
        if (direction < 0) return result;
        for (int distance = 1; distance <= depth; distance++)
            result.Add(Step(target, direction, distance));
        return result;
    }

    /// <summary>相邻地块决定方向；由近到远分别为 1/3/5 格、3/2/1 点基础伤害。</summary>
    public static IReadOnlyList<AttackAreaCell> GetCone(HexCoord origin, HexCoord adjacentTile)
    {
        int direction = -1;
        for (int i = 0; i < 6; i++)
            if (origin.GetNeighbor(i) == adjacentTile)
            {
                direction = i;
                break;
            }

        var result = new List<AttackAreaCell>(9);
        if (direction < 0) return result;

        for (int distance = 1; distance <= 3; distance++)
        {
            result.Add(new AttackAreaCell(Step(origin, direction, distance), 4 - distance));
            for (int offset = 1; offset < distance; offset++)
            {
                HexCoord baseCell = Step(origin, direction, distance - offset);
                result.Add(new AttackAreaCell(Step(baseCell, (direction + 5) % 6, offset), 4 - distance));
                result.Add(new AttackAreaCell(Step(baseCell, (direction + 1) % 6, offset), 4 - distance));
            }
        }
        return result;
    }

    public static IReadOnlyList<HexCoord> GetFixedBombardmentPattern(int index)
    {
        if (index < 0 || index >= FixedBombardmentPatterns.Length)
            return new HexCoord[0];
        return FixedBombardmentPatterns[index];
    }

    private static HexCoord Step(HexCoord start, int direction, int count)
    {
        for (int i = 0; i < count; i++) start = start.GetNeighbor(direction);
        return start;
    }
}
