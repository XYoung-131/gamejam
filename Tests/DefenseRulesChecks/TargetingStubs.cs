using System.Collections.Generic;

public enum DefenseTargetRule { 自身, 单位, 地块, 多个单位 }
public enum DefenseEffectRule { 普通, 换位, 神圣祝福, 投掷织毯, 坚守, 步行, 跳跃, 毒遁, 错乱传送, 自动屏障, 炎障 }
public enum CardPool { 攻击, 防御 }
public enum Rarity { 普通, 固有, 稀有, 传奇, 敌方特有 }

public class CardData
{
    public DefenseTargetRule defenseTargetRule;
    public DefenseEffectRule defenseEffectRule;
    public int minRange;
    public int maxRange;
    public int maxDefenseTargets = 1;
    public int actionCost;
    public CardPool library;
    public Rarity rarity;
    public bool initialOnly;
    public int GetEffectiveMaxRange(Unit caster) => maxRange;
    public int GetPlayActionCost(Unit caster) => actionCost;
}

public class UnitData { public string unitName; }
public class BuffData { }
public enum DamageType { 物理, 真实, Buff }
public class DamageContext { public Unit attacker; public DamageType type; }
public class Unit
{
    public HexCoord coord;
    public UnitData data;
    public bool IsDead;
    public DefenseCombatState DefenseState { get; } = new DefenseCombatState();
    public readonly HashSet<string> namedBuffs = new HashSet<string>();
    public int health = 1;
    public HexCoord GetCoord() => coord;
    public int GetCurrentHealth() => health;
    public bool HasNamedBuff(string name) => namedBuffs.Contains(name);
    public void ApplyBuff(BuffData buff, int stacks) { }
}
public class PlayerUnit : Unit { }
public class HexCell
{
    public HexCoord coord;
    public Unit occupyUnit;
    public bool walkable = true;
    public TerrainData terrain;
    public bool CanStopOn(Unit caster) => walkable && occupyUnit == null;
    public bool isWalkable => walkable;
    public bool IsBlockedByObstacle() => false;
}
public class TerrainData { }
public class HexGrid
{
    public static HexGrid Instance;
    public readonly Dictionary<HexCoord, HexCell> cells = new Dictionary<HexCoord, HexCell>();
    public IEnumerable<HexCell> GetAllCells() => cells.Values;
    public HexCell GetCell(HexCoord coord) => cells.TryGetValue(coord, out var cell) ? cell : null;
}
