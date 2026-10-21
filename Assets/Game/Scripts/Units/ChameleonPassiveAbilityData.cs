using UnityEngine;

[CreateAssetMenu(fileName = "Passive_变色龙", menuName = "Game/Passive Abilities/变色龙")]
public sealed class ChameleonPassiveAbilityData : PassiveAbilityData
{
    [Tooltip("首次受到生命伤害后获得的无敌 Buff；应配置为持续 1 回合")]
    public BuffData invincibleBuff;

    [Tooltip("首次受到生命伤害后，替换当前预告的技能")]
    public CardData confusedTeleportSkill;

    [Min(0)]
    [Tooltip("错乱传送所在的技能组索引，从 0 开始")]
    public int confusedTeleportGroupIndex = 1;

    public override PassiveRuntime CreateRuntime(Unit owner)
    {
        return new ChameleonPassiveRuntime(this, owner);
    }
}

public sealed class ChameleonPassiveRuntime : PassiveRuntime
{
    private readonly ChameleonPassiveAbilityData config;
    private bool hasTriggered;
    private bool teleportPending;

    public ChameleonPassiveRuntime(ChameleonPassiveAbilityData data, Unit owner) : base(data, owner)
    {
        config = data;
    }

    public override void OnOwnerDamaged(Unit source, int healthDamage)
    {
        if (hasTriggered || healthDamage <= 0 || Owner == null || Owner.IsDead) return;

        hasTriggered = true;
        teleportPending = config.confusedTeleportSkill != null;

        // Unit 在发出 UnitDamageResolved 后才做最终死亡判定。
        // 因此首次伤害即使把变色龙打到 0，及时施加“免疫死亡”也能将其保留在 1 血。
        if (config.invincibleBuff != null)
            Owner.ApplyBuff(config.invincibleBuff);

        // 敌人预告在玩家行动前已经生成，所以这里必须同步替换当前预告，
        // 不能只等待下一次 AfterDecision。
        if (teleportPending && Owner is EnemyUnit enemy)
            enemy.OverridePlannedSkills(new[] { config.confusedTeleportSkill },
                config.confusedTeleportGroupIndex, "首次受伤：改用错乱传送");
    }

    public override void AfterDecision(EnemyDecision decision)
    {
        if (!teleportPending || decision == null || config.confusedTeleportSkill == null) return;

        decision.SetSkillGroup(config.confusedTeleportGroupIndex)
                .SetMovement(EnemyMovementIntent.随机移动)
                .SetSkipSkills(false, "首次受伤后等待使用错乱传送")
                .SetSkills(new[] { config.confusedTeleportSkill });
    }

    public override void OnSkillUsed(CardData skill)
    {
        if (teleportPending && skill == config.confusedTeleportSkill)
            teleportPending = false;
    }
}
