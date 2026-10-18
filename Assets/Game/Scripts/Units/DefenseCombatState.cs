/// <summary>只保存单场战斗中的防御技能回合状态，不写入卡牌资产。</summary>
public sealed class DefenseCombatState
{
    private bool usedSelfMovement;
    private bool usedGuard;
    private bool usedAutoBarrier;
    private bool fireBarrier;
    private BuffData fireBuff;
    private bool untargetableThisTurn;

    public bool UntargetableThisTurn => untargetableThisTurn;
    public bool CanMoveSelf => !usedGuard;
    public void MarkSelfMovement() => usedSelfMovement = true;

    public void ProtectNextTurn() => untargetableThisTurn = true;

    public void SetFireBarrier(BuffData burn)
    {
        fireBarrier = true;
        fireBuff = burn;
    }

    public void OnAttacked(Unit victim, DamageContext context)
    {
        if (!fireBarrier || fireBuff == null || context == null ||
            context.attacker == null || context.attacker.IsDead ||
            context.type == DamageType.Buff ||
            (victim is PlayerUnit) == (context.attacker is PlayerUnit)) return;
        context.attacker.ApplyBuff(fireBuff, 2);
    }

    public int GetStaminaCost(CardData card, Unit caster)
    {
        if (card == null) return 0;
        int cost = card.GetPlayActionCost(caster);
        return card.defenseEffectRule == DefenseEffectRule.自动屏障 && !usedAutoBarrier
            ? System.Math.Max(0, cost - 3) : cost;
    }

    public void BeginTurn()
    {
        usedSelfMovement = false;
        usedGuard = false;
        usedAutoBarrier = false;
        fireBarrier = false;
        fireBuff = null;
        untargetableThisTurn = false;
    }

    public bool CanUse(CardData card)
    {
        if (card == null) return false;
        if (card.defenseEffectRule == DefenseEffectRule.坚守 && usedSelfMovement)
            return false;
        if (IsSelfMovement(card.defenseEffectRule) && usedGuard)
            return false;
        return true;
    }

    public void RecordUse(CardData card)
    {
        if (card == null) return;
        if (card.defenseEffectRule == DefenseEffectRule.坚守) usedGuard = true;
        if (card.defenseEffectRule == DefenseEffectRule.自动屏障) usedAutoBarrier = true;
        if (IsSelfMovement(card.defenseEffectRule)) usedSelfMovement = true;
    }

    private static bool IsSelfMovement(DefenseEffectRule rule) =>
        rule == DefenseEffectRule.步行 || rule == DefenseEffectRule.跳跃 ||
        rule == DefenseEffectRule.换位 ||
        rule == DefenseEffectRule.错乱传送 || rule == DefenseEffectRule.毒遁;
}
