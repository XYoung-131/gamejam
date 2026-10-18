using System.Collections.Generic;

/// <summary>不依赖场景的防御技能距离与多目标规则。</summary>
public static class DefenseSkillMath
{
    public static bool IsDistanceAllowed(HexCoord from, HexCoord to, int min, int max)
    {
        int distance = from.Distance(to);
        return distance >= min && distance <= max;
    }

    public static bool HasDistinctCount(IReadOnlyList<HexCoord> cells, int min, int max)
    {
        if (cells == null || cells.Count < min || cells.Count > max) return false;
        var unique = new HashSet<HexCoord>();
        foreach (HexCoord cell in cells)
            if (!unique.Add(cell)) return false;
        return true;
    }
}
