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
        InitialCardsAreExcludedFromOffers();
        InitialAssetFlagsMatchWorkbook();
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

    static void InitialCardsAreExcludedFromOffers()
    {
        var jump = new CardData { library = CardPool.防御, initialOnly = true };
        var block = new CardData { library = CardPool.防御, initialOnly = true };
        var regular = new CardData { library = CardPool.防御 };
        var enemyOnly = new CardData { library = CardPool.防御, rarity = Rarity.敌方特有 };
        var wrongPool = new CardData { library = CardPool.攻击 };
        Expect(!CardPoolRules.IsOfferEligible(jump, CardPool.防御), "initial-only excluded");
        Expect(CardPoolRules.IsOfferEligible(regular, CardPool.防御), "regular defense offered");
        Expect(!CardPoolRules.IsOfferEligible(enemyOnly, CardPool.防御), "enemy-only excluded");
        Expect(!CardPoolRules.IsOfferEligible(wrongPool, CardPool.防御), "wrong pool excluded");
        var initial = CardPoolRules.GetInitialCards(new[] { jump, block, jump, regular, enemyOnly });
        Expect(initial.Count == 2 && initial[0] == jump && initial[1] == block, "unique initial cards in order");
        var configured = CardPoolRules.MergeInitialCards(new[] { regular }, new[] { jump, block, jump });
        Expect(configured.Count == 3 && configured[0] == regular && configured[1] == jump &&
            configured[2] == block, "configured starter retained before generated starters");
    }

    static void InitialAssetFlagsMatchWorkbook()
    {
        string folder = "Assets/Game/ScriptableObjects/技能/防御技能";
        var expected = new HashSet<string> { "跳", "格挡", "远跳" };
        foreach (string path in System.IO.Directory.GetFiles(folder, "*.asset"))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            bool flagged = System.IO.File.ReadAllText(path).Contains("initialOnly: 1");
            Expect(flagged == expected.Contains(name), "initial-only asset flag: " + name);
        }
    }

    static void Expect(bool actual, string message)
    {
        if (!actual) throw new Exception(message);
    }
}
