using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Passive_克勒妮玛塔", menuName = "Game/Passive Abilities/克勒妮玛塔")]
public sealed class MataPassiveAbilityData : PassiveAbilityData
{
    public List<UnitData> oneHealthAllies = new List<UnitData>();
    public UnitData twoHealthAlly;
    public BuffData stunBuff;
    public CardData fastWalkSkill;

    public override PassiveRuntime CreateRuntime(Unit owner)
    {
        return new MataPassiveRuntime(this, owner);
    }
}

public sealed class MataPassiveRuntime : PassiveRuntime
{
    private readonly MataPassiveAbilityData config;
    private readonly Dictionary<Unit, int> healthBonuses = new Dictionary<Unit, int>();
    private bool damagedSinceLastDecision;
    private int retreatCooldown;

    public MataPassiveRuntime(MataPassiveAbilityData data, Unit owner) : base(data, owner)
    {
        config = data;
    }

    public override void OnActivated()
    {
        EventManager.AddListener<Unit>("UnitInitialized", HandleUnitInitialized);
        ApplyAuraToExistingAllies();
    }

    public override void OnDeactivated()
    {
        EventManager.RemoveListener<Unit>("UnitInitialized", HandleUnitInitialized);
        foreach (KeyValuePair<Unit, int> pair in healthBonuses)
            if (pair.Key != null && !pair.Key.IsDead) pair.Key.ModifyMaxHealth(-pair.Value, false);
        healthBonuses.Clear();
    }

    public override void OnOwnerAboutToTakeDamage(DamageContext context)
    {
        if (context.amount <= 0) return;
        if (context.attacker != null) damagedSinceLastDecision = true;
        List<Unit> candidates = FindLivingAffectedAllies();
        if (candidates.Count == 0) return;
        Unit recipient = candidates[Random.Range(0, candidates.Count)];
        int transferred = Mathf.Min(2, context.amount);
        context.amount -= transferred;
        recipient.TakeDamage(transferred, context.type, context.attacker);
    }

    public override void OnOwnerDamaged(Unit source, int healthDamage)
    {
        damagedSinceLastDecision = true;
    }

    public override void OnTurnStart()
    {
        ApplyAuraToExistingAllies();
        if (FindLivingAffectedAllies().Count == 0 && config.stunBuff != null)
            Owner.ApplyBuff(config.stunBuff, 1);
    }

    public override void AfterDecision(EnemyDecision decision)
    {
        if (decision == null) return;
        if (FindLivingAffectedAllies().Count == 0)
        {
            damagedSinceLastDecision = false;
            decision.SetMovement(EnemyMovementIntent.无)
                    .SetSkipSkills(true, "场上没有可用的鼠群随从");
            return;
        }
        bool playerNear = decision.PrimaryTarget != null &&
                          Owner.GetCoord().Distance(decision.PrimaryTarget.GetCoord()) <= 2;
        bool shouldRetreat = playerNear || damagedSinceLastDecision;
        damagedSinceLastDecision = false;

        if (retreatCooldown > 0)
        {
            retreatCooldown--;
            return;
        }
        if (!shouldRetreat || config.fastWalkSkill == null) return;

        var skills = new List<CardData>(decision.Skills);
        skills.Add(config.fastWalkSkill);
        decision.SetSkills(skills)
                .SetMovement(EnemyMovementIntent.远离目标, decision.PrimaryTarget, 0);
    }

    public override void OnSkillUsed(CardData skill)
    {
        if (skill == config.fastWalkSkill) retreatCooldown = 1;
    }

    public override void OnOwnerDied(Unit killer)
    {
        if (GameManager.Instance != null) GameManager.Instance.OnPlayerVictory();
    }

    private void HandleUnitInitialized(Unit unit)
    {
        TryApplyHealthBonus(unit);
    }

    private void ApplyAuraToExistingAllies()
    {
        foreach (EnemyUnit enemy in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
            TryApplyHealthBonus(enemy);
    }

    private void TryApplyHealthBonus(Unit unit)
    {
        if (unit == null || unit == Owner || unit.IsDead || unit.data == null ||
            healthBonuses.ContainsKey(unit)) return;
        int bonus = 0;
        if (unit.data == config.twoHealthAlly) bonus = 2;
        else if (config.oneHealthAllies != null && config.oneHealthAllies.Contains(unit.data)) bonus = 1;
        if (bonus <= 0) return;
        healthBonuses[unit] = bonus;
        unit.ModifyMaxHealth(bonus, true);
    }

    private List<Unit> FindLivingAffectedAllies()
    {
        var result = new List<Unit>();
        foreach (EnemyUnit enemy in UnityEngine.Object.FindObjectsOfType<EnemyUnit>())
        {
            if (enemy == null || enemy == Owner || enemy.IsDead || enemy.GetCurrentHealth() <= 0 ||
                enemy.data == null) continue;
            if (enemy.data == config.twoHealthAlly ||
                (config.oneHealthAllies != null && config.oneHealthAllies.Contains(enemy.data)))
                result.Add(enemy);
        }
        return result;
    }
}
