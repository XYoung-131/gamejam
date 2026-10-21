using System.Collections.Generic;
using UnityEngine;

public enum LindwormForm
{
    完整体 = 0,
    躯体 = 1,
    尾巴 = 2
}

[CreateAssetMenu(fileName = "Passive_林德沃姆", menuName = "Game/Passive Abilities/林德沃姆")]
public sealed class LindwormPassiveAbilityData : PassiveAbilityData
{
    public LindwormForm form;

    [Header("完整体")]
    [Min(1)] public int allyKillThreshold = 3;
    public UnitData bodyData;
    public UnitData tailData;

    [Header("共享资源")]
    public BuffData strengthBuff;
    public BuffData agilityBuff;
    public BuffData poisonBuff;
    public BuffData stunBuff;
    public BuffData invincibleBuff;
    public CardData smallStepSkill;

    public override PassiveRuntime CreateRuntime(Unit owner)
    {
        return new LindwormPassiveRuntime(this, owner);
    }
}

public sealed class LindwormPassiveRuntime : PassiveRuntime
{
    private readonly LindwormPassiveAbilityData config;
    private int alliedKillCount;
    private bool killRewardGranted;
    private bool friendlyFireThisDecision;
    private EnemyUnit linkedTail;

    private EnemyUnit EnemyOwner => Owner as EnemyUnit;

    public LindwormPassiveRuntime(LindwormPassiveAbilityData data, Unit owner) : base(data, owner)
    {
        config = data;
    }

    public override void BeforeDecision()
    {
        friendlyFireThisDecision = false;
    }

    public override void AfterDecision(EnemyDecision decision)
    {
        EnemyUnit owner = EnemyOwner;
        if (owner == null || decision == null || config.form == LindwormForm.尾巴) return;

        AddApproachStepWhenNeeded(decision);
        SelectFriendlyTargetWhenPlayerCannotBeHit(owner, decision);
    }

    public override bool CanTargetFriendlyWithAttack(Unit target)
    {
        return friendlyFireThisDecision && target is EnemyUnit && target != Owner;
    }

    public override void OnUnitDied(Unit dead, Unit killer)
    {
        if (dead == null || dead == Owner) return;

        if (config.form == LindwormForm.躯体 && dead == linkedTail)
        {
            linkedTail = null;
            if (config.invincibleBuff != null) Owner.RemoveBuff(config.invincibleBuff);
            return;
        }

        if (config.form != LindwormForm.完整体 || !(dead is EnemyUnit)) return;
        bool killedByOwner = killer == Owner;
        bool diedPoisoned = config.poisonBuff != null && dead.HasBuff(config.poisonBuff);
        if (!killedByOwner && !diedPoisoned) return;

        alliedKillCount++;
        if (killRewardGranted || alliedKillCount < Mathf.Max(1, config.allyKillThreshold)) return;
        killRewardGranted = true;
        if (config.strengthBuff != null) Owner.ApplyBuff(config.strengthBuff, 1);
        if (config.agilityBuff != null) Owner.ApplyBuff(config.agilityBuff, 1);
    }

    public override void OnTurnStart()
    {
        if (config.form != LindwormForm.躯体 || config.invincibleBuff == null) return;
        if (linkedTail != null && !linkedTail.IsDead && linkedTail.GetCurrentHealth() > 0)
        {
            if (!Owner.HasBuff(config.invincibleBuff)) Owner.ApplyBuff(config.invincibleBuff, 1);
        }
        else
            Owner.RemoveBuff(config.invincibleBuff);
    }

    public override void OnOwnerDied(Unit killer)
    {
        if (config.form != LindwormForm.完整体) return;
        SplitIntoBodyAndTail();
    }

    public void LinkTail(EnemyUnit tail)
    {
        if (config.form != LindwormForm.躯体) return;
        linkedTail = tail;
        if (linkedTail != null && !linkedTail.IsDead && config.invincibleBuff != null)
            Owner.ApplyBuff(config.invincibleBuff, 1);
    }

    private void AddApproachStepWhenNeeded(EnemyDecision decision)
    {
        if (config.smallStepSkill == null || decision.SkillGroupIndex != 0 ||
            decision.PrimaryTarget == null ||
            Owner.GetCoord().Distance(decision.PrimaryTarget.GetCoord()) <= 3) return;

        var skills = new List<CardData>();
        skills.Add(config.smallStepSkill);
        foreach (CardData skill in decision.Skills)
            if (skill != null) skills.Add(skill);
        decision.SetSkills(skills)
                .SetMovement(EnemyMovementIntent.靠近目标, decision.PrimaryTarget, 1)
                .SetPreserveSkillOrder();
    }

    private void SelectFriendlyTargetWhenPlayerCannotBeHit(EnemyUnit owner, EnemyDecision decision)
    {
        var attacks = new List<CardData>();
        foreach (CardData skill in decision.Skills)
            if (skill != null && skill.IsAttack) attacks.Add(skill);
        if (attacks.Count == 0) return;

        Unit player = decision.PrimaryTarget;
        if (CanHitWithAny(owner, attacks, player)) return;

        friendlyFireThisDecision = true;
        EnemyUnit nearest = null;
        int nearestDistance = int.MaxValue;
        foreach (EnemyUnit candidate in Object.FindObjectsOfType<EnemyUnit>())
        {
            if (candidate == null || candidate == owner || candidate.IsDead ||
                candidate.GetCurrentHealth() <= 0 || !CanHitWithAny(owner, attacks, candidate)) continue;
            int distance = owner.GetCoord().Distance(candidate.GetCoord());
            if (distance >= nearestDistance) continue;
            nearest = candidate;
            nearestDistance = distance;
        }

        if (nearest != null)
            decision.SetPrimaryTarget(nearest);
        else
            friendlyFireThisDecision = false;
    }

    private static bool CanHitWithAny(EnemyUnit owner, List<CardData> attacks, Unit target)
    {
        if (target == null || target.IsDead || target.GetCurrentHealth() <= 0) return false;
        foreach (CardData attack in attacks)
            if (owner.CanHitTargetWithSkill(attack, target)) return true;
        return false;
    }

    private void SplitIntoBodyAndTail()
    {
        EnemyUnit owner = EnemyOwner;
        if (owner == null || HexGrid.Instance == null) return;

        int strengthStacks = config.strengthBuff != null ? Owner.GetBuffStacks(config.strengthBuff) : 0;
        int agilityStacks = config.agilityBuff != null ? Owner.GetBuffStacks(config.agilityBuff) : 0;
        List<HexCell> cells = FindNearestEmptyCells(Owner.GetCoord(), 2);

        EnemyUnit body = cells.Count > 0 ? SpawnPart(config.bodyData, cells[0].coord) : null;
        EnemyUnit tail = cells.Count > 1 ? SpawnPart(config.tailData, cells[1].coord) : null;

        if (body != null)
        {
            if (strengthStacks > 0 && config.strengthBuff != null)
                body.ApplyBuff(config.strengthBuff, strengthStacks);
            if (config.stunBuff != null) body.ApplyBuff(config.stunBuff, 1);
            body.FindPassiveRuntime<LindwormPassiveRuntime>()?.LinkTail(tail);
        }
        if (tail != null)
        {
            if (agilityStacks > 0 && config.agilityBuff != null)
                tail.ApplyBuff(config.agilityBuff, agilityStacks);
            if (config.stunBuff != null) tail.ApplyBuff(config.stunBuff, 1);
        }

        if (body == null || tail == null)
            Debug.LogWarning("林德沃姆死亡时没有足够的可用格子，未能完整生成躯体和尾巴");
    }

    private static List<HexCell> FindNearestEmptyCells(HexCoord origin, int count)
    {
        var result = new List<HexCell>();
        if (HexGrid.Instance == null || count <= 0) return result;
        foreach (HexCell cell in HexGrid.Instance.GetAllCells())
            if (cell != null && cell.IsEmpty()) result.Add(cell);
        result.Sort((left, right) =>
        {
            int distance = origin.Distance(left.coord).CompareTo(origin.Distance(right.coord));
            if (distance != 0) return distance;
            int q = left.coord.q.CompareTo(right.coord.q);
            return q != 0 ? q : left.coord.r.CompareTo(right.coord.r);
        });
        if (result.Count > count) result.RemoveRange(count, result.Count - count);
        return result;
    }

    private static EnemyUnit SpawnPart(UnitData partData, HexCoord coord)
    {
        if (partData == null || partData.prefab == null || HexGrid.Instance == null) return null;
        GameObject instance = Object.Instantiate(partData.prefab, HexGrid.Instance.HexToWorld(coord),
            Quaternion.identity);
        instance.name = "Enemy_" + partData.unitName + "_" + coord;
        EnemyUnit enemy = instance.GetComponent<EnemyUnit>();
        if (enemy == null) enemy = instance.AddComponent<EnemyUnit>();
        enemy.Initialize(partData, coord);
        return enemy;
    }
}
