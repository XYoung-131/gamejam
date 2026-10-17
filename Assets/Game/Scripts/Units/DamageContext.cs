// ============================================================
//  DamageContext——一次"伤害结算"的完整上下文
//
//  为什么需要它：
//  遗物里有一批效果要按"伤害来源"来判定，例如
//  - 柔顺布料：受到的非物理伤害减 1、免疫爆炸；
//  - 防弹背心：受到的远程伤害减 1；
//  - 黄铜指虎：对相邻格目标造成的伤害 +1（只有近战贴身才成立）；
//  - 回归本质：所有稀有度为"普通"的技能伤害 +1；
//  - 焚心火：处在灼烧状态的敌人造成伤害 -1。
//  这些光靠一个 int amount 是判断不出来的，所以把"谁打的、用什么牌打的、
//  近战还是远程、是不是爆炸"打包成一个对象一起传给 TakeDamage。
//
//  向后兼容：
//  Unit.TakeDamage(int, DamageType) 这个老重载仍然保留，内部会自动包一个空的
//  DamageContext，所以老的调用点（位移碰撞、持续伤害、地形伤害）不需要改。
// ============================================================

/// <summary>一次伤害结算的上下文（谁打的、什么牌、近战/远程/爆炸等）</summary>
public class DamageContext
{
    /// <summary>伤害来源单位（打出攻击的一方）。持续伤害 / 碰撞 / 地形伤害时为 null</summary>
    public Unit attacker;

    /// <summary>本次伤害的数值</summary>
    public int amount;

    /// <summary>伤害类型（物理 / 真实 / Buff）</summary>
    public DamageType type = DamageType.物理;

    /// <summary>造成这次伤害的卡牌（持续伤害 / 碰撞 / 爆炸时可为 null）</summary>
    public CardData card;

    /// <summary>是否属于近战伤害（卡牌带「近战」标签，或普通平A）</summary>
    public bool isMelee;

    /// <summary>是否属于远程伤害（卡牌带「远程」标签）</summary>
    public bool isRanged;

    /// <summary>是否属于爆炸伤害（炸药桶等）。柔顺布料会免疫爆炸</summary>
    public bool isExplosion;

    public DamageContext() { }

    public DamageContext(int amount, DamageType type)
    {
        this.amount = amount;
        this.type = type;
    }

    /// <summary>从一张攻击卡生成上下文（自动推断近战 / 远程标签）</summary>
    public static DamageContext FromCard(Unit attacker, CardData card, int amount, DamageType type)
    {
        DamageContext ctx = new DamageContext(amount, type);
        ctx.attacker = attacker;
        ctx.card = card;
        if (card != null)
        {
            ctx.isMelee = card.tags.Contains(CardTag.近战);
            ctx.isRanged = card.tags.Contains(CardTag.远程);
        }
        return ctx;
    }
}
