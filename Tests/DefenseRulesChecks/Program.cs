using System;
using System.Collections.Generic;

static class Program
{
    static void Main()
    {
        var origin = new HexCoord(0, 0);
        Expect(DefenseSkillMath.IsDistanceAllowed(origin, new HexCoord(1, 0), 1, 1), "adjacent target");
        Expect(!DefenseSkillMath.IsDistanceAllowed(origin, new HexCoord(2, 0), 1, 1), "outside adjacent range");
        Expect(DefenseSkillMath.IsDistanceAllowed(origin, new HexCoord(2, 0), 2, 2), "exact two-cell jump");
        Expect(!DefenseSkillMath.IsDistanceAllowed(origin, new HexCoord(1, 0), 2, 2), "short jump excluded");
        Expect(DefenseSkillMath.HasDistinctCount(new List<HexCoord> { new HexCoord(1, 0) }, 1, 3), "single target");
        Expect(DefenseSkillMath.HasDistinctCount(new List<HexCoord> { new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0) }, 1, 3), "three targets");
        Expect(!DefenseSkillMath.HasDistinctCount(new List<HexCoord> { new HexCoord(1, 0), new HexCoord(1, 0) }, 1, 3), "duplicate target rejected");
        Expect(!DefenseSkillMath.HasDistinctCount(new List<HexCoord>(), 1, 3), "empty selection rejected");
        TargetingRejectsWrongSideAndDeadUnits();
        TargetingRejectsOccupiedAndMissingLandings();
        Console.WriteLine("Defense rules checks passed.");
    }

    static void TargetingRejectsWrongSideAndDeadUnits()
    {
        var grid = new HexGrid(); HexGrid.Instance = grid;
        var caster = new PlayerUnit { coord = new HexCoord(0, 0) };
        var enemy = new Unit { coord = new HexCoord(1, 0) };
        var ally = new PlayerUnit { coord = new HexCoord(0, 1) };
        grid.cells[enemy.coord] = new HexCell { coord = enemy.coord, occupyUnit = enemy };
        grid.cells[ally.coord] = new HexCell { coord = ally.coord, occupyUnit = ally };
        var card = new CardData { defenseTargetRule = DefenseTargetRule.单位, maxRange = 1 };
        Expect(DefenseSkillTargeting.IsValidSelection(card, caster, new[] { enemy.coord }), "enemy target");
        Expect(!DefenseSkillTargeting.IsValidSelection(card, caster, new[] { ally.coord }), "ally excluded");
        enemy.IsDead = true;
        Expect(!DefenseSkillTargeting.IsValidSelection(card, caster, new[] { enemy.coord }), "dead target excluded");
    }

    static void TargetingRejectsOccupiedAndMissingLandings()
    {
        var grid = new HexGrid(); HexGrid.Instance = grid;
        var caster = new PlayerUnit { coord = new HexCoord(0, 0) };
        var occupied = new HexCoord(1, 0);
        var empty = new HexCoord(0, 1);
        grid.cells[occupied] = new HexCell { coord = occupied, occupyUnit = new Unit() };
        grid.cells[empty] = new HexCell { coord = empty };
        var card = new CardData { defenseTargetRule = DefenseTargetRule.地块, minRange = 1, maxRange = 1 };
        Expect(!DefenseSkillTargeting.IsValidSelection(card, caster, new[] { occupied }), "occupied landing excluded");
        Expect(DefenseSkillTargeting.IsValidSelection(card, caster, new[] { empty }), "empty landing");
        Expect(!DefenseSkillTargeting.IsValidSelection(card, caster, new[] { new HexCoord(-1, 0) }), "missing grid cell excluded");
    }

    static void Expect(bool actual, string message)
    {
        if (!actual) throw new Exception(message);
    }
}
