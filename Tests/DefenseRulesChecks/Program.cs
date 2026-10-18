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
        GuardAndMovementExcludeEachOtherForOneTurn();
        RandomTeleportPrefersOrdinaryTerrain();
        WalkingPathMustFitSkillRange();
        AutoBarrierDiscountResetsEachTurn();
        AllDefenseAssetsConfigured();
        SacredBlessingRequiresAngelAfterFirstUse();
        HiddenEnemyCannotBeSelected();
        SwapNeedsTwoWalkableCells();
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

    static void GuardAndMovementExcludeEachOtherForOneTurn()
    {
        var guard = new CardData { defenseEffectRule = DefenseEffectRule.坚守 };
        var walk = new CardData { defenseEffectRule = DefenseEffectRule.步行 };
        var blink = new CardData { defenseEffectRule = DefenseEffectRule.毒遁 };
        var swap = new CardData { defenseEffectRule = DefenseEffectRule.换位 };
        var state = new DefenseCombatState();
        Expect(state.CanUse(guard), "guard initially allowed");
        state.RecordUse(walk);
        Expect(!state.CanUse(guard), "movement blocks guard");
        state.BeginTurn();
        state.RecordUse(guard);
        Expect(!state.CanUse(walk) && !state.CanUse(blink), "guard blocks movement and teleport");
        Expect(!state.CanUse(swap), "guard blocks swapping self");
        state.BeginTurn();
        Expect(state.CanUse(walk) && state.CanUse(guard), "next turn clears guard lock");
        state.RecordUse(swap);
        Expect(!state.CanUse(guard), "swap blocks guard later in same turn");
    }

    static void RandomTeleportPrefersOrdinaryTerrain()
    {
        var grid = new HexGrid(); HexGrid.Instance = grid;
        var caster = new PlayerUnit { coord = new HexCoord(0, 0) };
        var normal = new HexCoord(3, 0);
        var special = new HexCoord(0, 3);
        grid.cells[normal] = new HexCell { coord = normal };
        grid.cells[special] = new HexCell { coord = special, terrain = new TerrainData() };
        var card = new CardData { minRange = 3, maxRange = 3 };
        var choices = DefenseSkillTargeting.GetRandomTeleportLandings(card, caster);
        Expect(choices.Count == 1 && choices[0] == normal, "ordinary terrain preferred");
        grid.cells[normal].occupyUnit = new Unit();
        choices = DefenseSkillTargeting.GetRandomTeleportLandings(card, caster);
        Expect(choices.Count == 1 && choices[0] == special, "special terrain fallback");
    }

    static void WalkingPathMustFitSkillRange()
    {
        var shortPath = new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0) };
        var detour = new[] { new HexCoord(0, 0), new HexCoord(0, 1), new HexCoord(1, 1), new HexCoord(2, 0) };
        Expect(DefenseSkillMath.IsPathWithinSteps(shortPath, 2), "two step walk accepted");
        Expect(!DefenseSkillMath.IsPathWithinSteps(detour, 2), "detour beyond two steps rejected");
    }

    static void AutoBarrierDiscountResetsEachTurn()
    {
        var barrier = new CardData { defenseEffectRule = DefenseEffectRule.自动屏障, actionCost = 9 };
        var state = new DefenseCombatState();
        var caster = new PlayerUnit();
        Expect(state.GetStaminaCost(barrier, caster) == 6, "first barrier costs six stamina");
        state.RecordUse(barrier);
        Expect(state.GetStaminaCost(barrier, caster) == 9, "later barrier costs nine stamina");
        state.BeginTurn();
        Expect(state.GetStaminaCost(barrier, caster) == 6, "barrier discount resets next turn");
    }

    static void AllDefenseAssetsConfigured()
    {
        var expected = new Dictionary<string, (int target, int effect)> {
            ["寻觅"]=(0,1), ["缝补"]=(0,2), ["羽翼加护"]=(0,3),
            ["荧光汇聚"]=(0,4), ["抱抱喷射"]=(0,5), ["换位"]=(1,6),
            ["坚守"]=(0,7), ["抱抱"]=(1,8), ["伤痕填补"]=(0,9),
            ["肌肉突变"]=(0,10), ["自动屏障"]=(0,11), ["精神摧毁"]=(3,12),
            ["跳"]=(2,14), ["格挡"]=(0,0), ["远跳"]=(2,14),
            ["快走"]=(2,13), ["小步走"]=(2,13), ["绊倒"]=(1,15),
            ["投掷织毯"]=(2,16), ["群体疗愈"]=(0,17),
            ["克勒妮的呼唤"]=(0,18), ["风飑"]=(0,19), ["岚匿"]=(0,20),
            ["飔垣"]=(0,21), ["错乱传送"]=(0,22), ["精神压制"]=(1,23),
            ["毒遁"]=(2,24), ["炎障"]=(0,25), ["神圣祝福"]=(0,26),
        };
        const string folder = "Assets/Game/ScriptableObjects/技能/防御技能";
        string[] paths = System.IO.Directory.GetFiles(folder, "*.asset");
        Expect(paths.Length == 29, "all 29 defense assets exist");
        int enemyOnly = 0;
        foreach (string path in paths)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            Expect(expected.TryGetValue(name, out var rules), "known defense asset: " + name);
            string yaml = System.IO.File.ReadAllText(path);
            Expect(yaml.Contains("defenseTargetRule: " + rules.target), "target rule: " + name);
            Expect(yaml.Contains("defenseEffectRule: " + rules.effect), "effect rule: " + name);
            Expect(yaml.Contains("library: 1"), "defense pool: " + name);
            if (yaml.Contains("rarity: 4")) enemyOnly++;
        }
        Expect(enemyOnly == 13, "exactly 13 enemy-only defenses");
    }

    static void SacredBlessingRequiresAngelAfterFirstUse()
    {
        var grid = new HexGrid(); HexGrid.Instance = grid;
        var caster = new PlayerUnit { coord = new HexCoord(0, 0) };
        var angel = new PlayerUnit { coord = new HexCoord(2, 0),
            data = new UnitData { unitName = "小天使" } };
        grid.cells[angel.coord] = new HexCell { coord = angel.coord, occupyUnit = angel };
        var blessing = new CardData { defenseTargetRule = DefenseTargetRule.自身,
            defenseEffectRule = DefenseEffectRule.神圣祝福, maxRange = 2 };
        Expect(DefenseSkillTargeting.IsValidSelection(blessing, caster, Array.Empty<HexCoord>()),
            "first blessing targets self");
        caster.namedBuffs.Add("神圣");
        Expect(!DefenseSkillTargeting.IsValidSelection(blessing, caster, Array.Empty<HexCoord>()),
            "blessed caster cannot self bless again");
        Expect(DefenseSkillTargeting.IsValidSelection(blessing, caster, new[] { angel.coord }),
            "blessed caster can bless angel within two cells");
    }

    static void HiddenEnemyCannotBeSelected()
    {
        var grid = new HexGrid(); HexGrid.Instance = grid;
        var caster = new PlayerUnit { coord = new HexCoord(0, 0) };
        var enemy = new Unit { coord = new HexCoord(1, 0) };
        grid.cells[enemy.coord] = new HexCell { coord = enemy.coord, occupyUnit = enemy };
        var debuff = new CardData { defenseTargetRule = DefenseTargetRule.单位, maxRange = 1 };
        Expect(DefenseSkillTargeting.IsValidSelection(debuff, caster, new[] { enemy.coord }),
            "visible enemy selectable");
        enemy.DefenseState.ProtectNextTurn();
        Expect(!DefenseSkillTargeting.IsValidSelection(debuff, caster, new[] { enemy.coord }),
            "hidden enemy excluded from active targeting");
        enemy.DefenseState.BeginTurn();
        Expect(DefenseSkillTargeting.IsValidSelection(debuff, caster, new[] { enemy.coord }),
            "protection expires on enemy next turn");
    }

    static void SwapNeedsTwoWalkableCells()
    {
        var grid = new HexGrid(); HexGrid.Instance = grid;
        var caster = new PlayerUnit { coord = new HexCoord(0, 0) };
        var neighbor = new Unit { coord = new HexCoord(1, 0) };
        grid.cells[caster.coord] = new HexCell { coord = caster.coord, occupyUnit = caster };
        grid.cells[neighbor.coord] = new HexCell { coord = neighbor.coord,
            occupyUnit = neighbor, walkable = false };
        var swap = new CardData { defenseTargetRule = DefenseTargetRule.单位,
            defenseEffectRule = DefenseEffectRule.换位, minRange = 1, maxRange = 1 };
        Expect(!DefenseSkillTargeting.IsValidSelection(swap, caster, new[] { neighbor.coord }),
            "swap rejects target on blocked terrain");
        grid.cells[neighbor.coord].walkable = true;
        Expect(DefenseSkillTargeting.IsValidSelection(swap, caster, new[] { neighbor.coord }),
            "swap accepts two walkable occupied cells");
    }

    static void Expect(bool actual, string message)
    {
        if (!actual) throw new Exception(message);
    }
}
