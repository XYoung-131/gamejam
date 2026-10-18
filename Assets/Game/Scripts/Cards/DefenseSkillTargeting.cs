using System.Collections.Generic;

/// <summary>防御技能第二步的合法格；玩家输入和敌人自动选格共用。</summary>
public static class DefenseSkillTargeting
{
    public static List<HexCoord> GetSelectableCells(CardData card, Unit caster,
        IReadOnlyList<HexCoord> selected)
    {
        var result = new List<HexCoord>();
        if (card == null || caster == null || HexGrid.Instance == null ||
            card.defenseTargetRule == DefenseTargetRule.自身) return result;

        foreach (HexCell cell in HexGrid.Instance.GetAllCells())
            if (cell != null && IsSelectableCell(card, caster, cell))
                result.Add(cell.coord);
        return result;
    }

    public static bool IsValidSelection(CardData card, Unit caster,
        IReadOnlyList<HexCoord> selected)
    {
        if (card == null || caster == null || selected == null) return false;
        if (card.defenseTargetRule == DefenseTargetRule.自身)
            return selected.Count == 0;

        int max = card.defenseTargetRule == DefenseTargetRule.多个单位
            ? System.Math.Max(1, card.maxDefenseTargets) : 1;
        if (!DefenseSkillMath.HasDistinctCount(selected, 1, max)) return false;
        if (HexGrid.Instance == null) return false;
        foreach (HexCoord coord in selected)
        {
            HexCell cell = HexGrid.Instance.GetCell(coord);
            if (cell == null || !IsSelectableCell(card, caster, cell)) return false;
        }
        return true;
    }

    private static bool IsSelectableCell(CardData card, Unit caster, HexCell cell)
    {
        int min = card.minRange;
        int max = card.GetEffectiveMaxRange(caster);
        if (!DefenseSkillMath.IsDistanceAllowed(caster.GetCoord(), cell.coord, min, max))
            return false;

        if (card.defenseTargetRule == DefenseTargetRule.地块)
        {
            if (card.defenseEffectRule == DefenseEffectRule.投掷织毯) return true;
            return cell.occupyUnit == null && cell.CanStopOn(caster);
        }

        Unit target = cell.occupyUnit;
        if (target == null || target == caster || target.IsDead || target.GetCurrentHealth() <= 0)
            return false;

        bool sameSide = (caster is PlayerUnit) == (target is PlayerUnit);
        if (card.defenseEffectRule == DefenseEffectRule.换位) return true;
        if (card.defenseEffectRule == DefenseEffectRule.神圣祝福)
            return sameSide && target.data != null && target.data.unitName == "小天使";
        return !sameSide;
    }
}
