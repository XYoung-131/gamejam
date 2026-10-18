using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

static class Program
{
    static void Main()
    {
        ConeHasThreeDamageBandsInEveryDirection();
        FixedBombardmentPatternsMatchReferenceImages();
        FixedBombardmentPatternsFitConceptMap();
        StraightLineAndPierceRules();
        EveryAttackAssetHasAnExplicitTargetRule();
        AttackEffectNumbersFollowSkillDescriptions();
        Console.WriteLine("Attack area geometry checks passed.");
    }

    static void ConeHasThreeDamageBandsInEveryDirection()
    {
        var origin = new HexCoord(0, 0);
        for (int direction = 0; direction < 6; direction++)
        {
            var cells = AttackAreaGeometry.GetCone(origin, origin.GetNeighbor(direction));
            Equal(9, cells.Count, "cone cell count");
            Equal(9, cells.Select(x => x.Coord).Distinct().Count(), "cone duplicates");
            for (int distance = 1; distance <= 3; distance++)
            {
                var band = cells.Where(x => x.Coord.Distance(origin) == distance).ToList();
                Equal(2 * distance - 1, band.Count, "cone width");
                True(band.All(x => x.Damage == 4 - distance), "cone damage band");
            }
        }
        Equal(0, AttackAreaGeometry.GetCone(origin, new HexCoord(2, 0)).Count,
            "nonadjacent direction must be rejected");
    }

    static void FixedBombardmentPatternsMatchReferenceImages()
    {
        int[] counts = { 30, 31, 34, 26 };
        for (int i = 0; i < counts.Length; i++)
        {
            IReadOnlyList<HexCoord> pattern = AttackAreaGeometry.GetFixedBombardmentPattern(i);
            Equal(counts[i], pattern.Count, "pattern cell count " + i);
            Equal(pattern.Count, pattern.Distinct().Count(), "pattern duplicates " + i);
        }
        True(AttackAreaGeometry.GetFixedBombardmentPattern(1).Contains(new HexCoord(-5, 1)),
            "second image left edge");
        True(AttackAreaGeometry.GetFixedBombardmentPattern(2).Contains(new HexCoord(0, 0)),
            "third image center");
        True(!AttackAreaGeometry.GetFixedBombardmentPattern(3).Contains(new HexCoord(0, 0)),
            "fourth image clear center");
    }

    static void FixedBombardmentPatternsFitConceptMap()
    {
        string prefab = System.IO.File.ReadAllText("Assets/Game/Prefabs/Board/ConceptMap.prefab");
        var mapCells = new HashSet<HexCoord>();
        foreach (Match match in Regex.Matches(prefab, @"(?m)^  - q: (-?\d+)\r?\n    r: (-?\d+)"))
            mapCells.Add(new HexCoord(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)));
        Equal(63, mapCells.Count, "concept map cell count");
        for (int index = 0; index < 4; index++)
            True(AttackAreaGeometry.GetFixedBombardmentPattern(index).All(mapCells.Contains),
                "pattern has cells outside concept map: " + index);
    }

    static void StraightLineAndPierceRules()
    {
        var origin = new HexCoord(0, 0);
        for (int direction = 0; direction < 6; direction++)
        {
            var adjacent = origin.GetNeighbor(direction);
            Equal(direction, AttackAreaGeometry.StraightDirection(origin, adjacent), "adjacent direction");
            var pierced = AttackAreaGeometry.GetPierce(origin, adjacent, 2);
            Equal(3, pierced.Count, "pierce length");
            True(pierced[1] == adjacent.GetNeighbor(direction), "first cell behind target");
            True(pierced[2] == pierced[1].GetNeighbor(direction), "second cell behind target");
        }
        Equal(-1, AttackAreaGeometry.StraightDirection(origin, origin), "self is not a line target");
        Equal(-1, AttackAreaGeometry.StraightDirection(origin, new HexCoord(1, 1)),
            "off-axis target is not a line target");
    }

    static void EveryAttackAssetHasAnExplicitTargetRule()
    {
        string folder = "Assets/Game/ScriptableObjects/技能/攻击技能";
        string[] assets = System.IO.Directory.GetFiles(folder, "*.asset");
        Equal(42, assets.Length, "attack asset count");
        foreach (string asset in assets)
        {
            string yaml = System.IO.File.ReadAllText(asset);
            Match rule = Regex.Match(yaml, @"(?m)^  attackTargetRule: (\d+)\r?$");
            True(rule.Success, "missing target rule: " + asset);
            int value = int.Parse(rule.Groups[1].Value);
            True(value >= 0 && value <= 9, "unknown target rule: " + asset);
            Match effect = Regex.Match(yaml, @"(?m)^  attackEffectRule: (\d+)\r?$");
            True(effect.Success, "missing effect rule: " + asset);
            True(int.Parse(effect.Groups[1].Value) <= 18, "unknown effect rule: " + asset);
        }
        CheckRule("落英缤纷", 5); CheckRule("光辉打击", 4);
        CheckRule("思维折跃", 9); CheckRule("荧光爆", 6);
        CheckRule("雷霆轰击", 7); CheckRule("长距戳刺", 8);
        CheckRule("圣域打击", 8); CheckRule("惊喜礼炮", 4);
        CheckEffect("暴揍", 1); CheckEffect("愤怒之拳", 3);
        CheckEffect("落英缤纷", 5); CheckEffect("飞身刺击", 14);
        CheckEffect("光辉打击", 18);
    }

    static void CheckRule(string name, int expected)
    {
        string yaml = System.IO.File.ReadAllText(
            "Assets/Game/ScriptableObjects/技能/攻击技能/" + name + ".asset");
        Match match = Regex.Match(yaml, @"(?m)^  attackTargetRule: (\d+)\r?$");
        True(match.Success, "missing rule: " + name);
        Equal(expected, int.Parse(match.Groups[1].Value), name + " rule");
    }

    static void CheckEffect(string name, int expected)
    {
        string yaml = System.IO.File.ReadAllText(
            "Assets/Game/ScriptableObjects/技能/攻击技能/" + name + ".asset");
        Match match = Regex.Match(yaml, @"(?m)^  attackEffectRule: (\d+)\r?$");
        True(match.Success, "missing effect rule: " + name);
        Equal(expected, int.Parse(match.Groups[1].Value), name + " effect");
    }

    static void AttackEffectNumbersFollowSkillDescriptions()
    {
        Equal(0, AttackEffectMath.MissingHealthQuarters(100, 100), "full health quarters");
        Equal(0, AttackEffectMath.MissingHealthQuarters(76, 100), "above first quarter");
        Equal(1, AttackEffectMath.MissingHealthQuarters(75, 100), "first lost quarter");
        Equal(2, AttackEffectMath.MissingHealthQuarters(50, 100), "half health quarters");
        Equal(3, AttackEffectMath.MissingHealthQuarters(25, 100), "three lost quarters");
        Equal(6, AttackEffectMath.HitCount(4, 1, 100, 50), "rage adjacent hits");
        Equal(4, AttackEffectMath.HitCount(2, 1, 100, 50), "rage distance-two hits");
        Equal(1, AttackEffectMath.HitCount(1, 0, 100, 100), "base hits");
        Equal(3, AttackEffectMath.RepeatsForPriorUses(0, 2, 3), "repeat cap");
        Equal(6, AttackEffectMath.DiscountedStamina(6, 0, 3), "first fist cost");
        Equal(5, AttackEffectMath.DiscountedStamina(6, 1, 3), "second fist cost");
        Equal(3, AttackEffectMath.DiscountedStamina(6, 10, 3), "fist discount cap");
        Equal(8, AttackEffectMath.HalfRefund(15), "half stamina rounds up");
        True(AttackEffectMath.RollSucceeds(0.249f, 0.25f), "25 percent success edge");
        True(!AttackEffectMath.RollSucceeds(0.25f, 0.25f), "25 percent failure edge");
    }

    static void Equal(int expected, int actual, string name)
    {
        if (expected != actual)
            throw new Exception($"{name}: expected {expected}, got {actual}");
    }

    static void True(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
    }
}
