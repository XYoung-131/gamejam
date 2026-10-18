using System.Collections.Generic;
using UnityEngine;

/// <summary>玩家与敌人共用的防御牌预验证和结算。</summary>
public static class DefenseSkillResolver
{
    public static bool CanResolve(CardData card, Unit caster, IReadOnlyList<HexCoord> selected)
    {
        if (card == null || card.IsAttack || caster == null || !caster.CanAttack() ||
            !caster.DefenseState.CanUse(card) ||
            !DefenseSkillTargeting.IsValidSelection(card, caster, selected)) return false;
        switch (card.defenseEffectRule)
        {
            case DefenseEffectRule.步行:
                return DefenseSkillMath.IsPathWithinSteps(WalkingPath(caster, selected[0]),
                    card.GetEffectiveMaxRange(caster));
            case DefenseEffectRule.错乱传送:
                return DefenseSkillTargeting.GetRandomTeleportLandings(card, caster).Count > 0;
            case DefenseEffectRule.换位:
                Unit other = HexGrid.Instance.GetCell(selected[0]).occupyUnit;
                HexCell ownCell = HexGrid.Instance.GetCell(caster.GetCoord());
                HexCell otherCell = HexGrid.Instance.GetCell(selected[0]);
                return other != null && ownCell != null && otherCell != null &&
                    ownCell.occupyUnit == caster && ownCell.isWalkable && otherCell.isWalkable &&
                    !ownCell.IsBlockedByObstacle() && !otherCell.IsBlockedByObstacle();
            case DefenseEffectRule.神圣祝福:
                return !HasNamedBuff(caster, "神圣") || selected.Count == 1;
            case DefenseEffectRule.投掷织毯:
                return card.generatedTerrain != null;
            default:
                return true;
        }
    }

    public static bool Resolve(CardData card, Unit caster, IReadOnlyList<HexCoord> selected)
    {
        if (!CanResolve(card, caster, selected)) return false;
        HexCoord origin = caster.GetCoord();
        switch (card.defenseEffectRule)
        {
            case DefenseEffectRule.步行:
                List<HexCoord> path = WalkingPath(caster, selected[0]);
                for (int i = 1; i < path.Count; i++) caster.MoveTo(path[i]);
                break;
            case DefenseEffectRule.跳跃:
                caster.TeleportTo(selected[0]);
                break;
            case DefenseEffectRule.毒遁:
                caster.TeleportTo(selected[0]);
                foreach (HexCoord neighbor in origin.GetAllNeighbors())
                {
                    HexCell cell = HexGrid.Instance.GetCell(neighbor);
                    Unit target = cell != null ? cell.occupyUnit : null;
                    if (IsEnemy(caster, target)) ApplyNamedBuff(card, target, "中毒", 2);
                }
                break;
            case DefenseEffectRule.错乱传送:
                List<HexCoord> landings = DefenseSkillTargeting.GetRandomTeleportLandings(card, caster);
                caster.TeleportTo(landings[Random.Range(0, landings.Count)]);
                break;
            case DefenseEffectRule.换位:
                if (!caster.TrySwapWith(HexGrid.Instance.GetCell(selected[0]).occupyUnit)) return false;
                break;
            default:
                ResolveStationary(card, caster, selected);
                break;
        }
        // 在真实执行结果确定后才记本回合移动/坚守状态。
        caster.DefenseState.RecordUse(card);
        return true;
    }

    private static List<HexCoord> WalkingPath(Unit caster, HexCoord destination)
    {
        return HexPathfinding.FindPath(caster.GetCoord(), destination,
            new HexPathfinding.PathOptions { mover = caster });
    }

    public static bool HasNamedBuff(Unit unit, string name)
    {
        foreach (BuffInstance buff in unit.GetActiveBuffs())
            if (buff != null && buff.data != null && buff.data.buffName == name) return true;
        return false;
    }

    private static void ResolveStationary(CardData card, Unit caster,
        IReadOnlyList<HexCoord> selected)
    {
        Unit target = selected.Count > 0 && HexGrid.Instance != null
            ? HexGrid.Instance.GetCell(selected[0]).occupyUnit : null;
        switch (card.defenseEffectRule)
        {
            case DefenseEffectRule.寻觅:
                break;
            case DefenseEffectRule.普通:
                if (card.defenseEffectType == DefenseEffectType.治疗) caster.Heal(card.defenseValue);
                else caster.AddShield(card.defenseValue);
                break;
            case DefenseEffectRule.指定治疗:
                foreach (Unit ally in AllLivingUnits())
                    if (IsAlly(caster, ally) && ally.data != null && ally.data.unitName == "普兰")
                    { ally.Heal(2); break; }
                break;
            case DefenseEffectRule.神圣护盾:
                caster.AddShield(caster.HasNamedBuff("神圣") ? 5 : 2);
                break;
            case DefenseEffectRule.荧光汇聚:
                ApplyNamedBuff(card, caster, "荧光", 2);
                break;
            case DefenseEffectRule.抱抱喷射:
                Summon(card.summonUnit, caster, 5, false);
                break;
            case DefenseEffectRule.坚守:
                caster.AddShield(8);
                break;
            case DefenseEffectRule.延迟亲密:
                if (target != null) target.QueueSkillBuffNextTurn(FindNamedBuff(card, "亲密"));
                break;
            case DefenseEffectRule.失血护盾:
                caster.AddShield(Mathf.Max(0, caster.GetMaxHealth() - caster.GetCurrentHealth()));
                break;
            case DefenseEffectRule.肌肉突变:
                ApplyNamedBuff(card, caster, "力量", 1);
                break;
            case DefenseEffectRule.自动屏障:
                caster.AddShield(3);
                break;
            case DefenseEffectRule.精神摧毁:
                foreach (HexCoord coord in selected)
                {
                    Unit victim = HexGrid.Instance.GetCell(coord).occupyUnit;
                    ApplyTemporaryPair(card, victim, selected.Count == 1 ? 6 : 3);
                }
                break;
            case DefenseEffectRule.临时残废:
                if (target != null) target.ApplyFixedStackBuff(FindNamedBuff(card, "残废"), 2, 2);
                break;
            case DefenseEffectRule.投掷织毯:
                ChangeTerrain(card, selected[0]);
                break;
            case DefenseEffectRule.群体疗愈:
                foreach (Unit ally in AllLivingUnits())
                    if (IsAlly(caster, ally)) ally.Heal(1);
                break;
            case DefenseEffectRule.克勒妮的呼唤:
                if (caster.GetCurrentHealth() * 2 < caster.GetMaxHealth())
                {
                    Summon(card.summonUnit, caster, 1, true);
                    Summon(card.alternateSummonUnit, caster, 1, true);
                }
                else Summon(card.summonUnit, caster, 2, true);
                break;
            case DefenseEffectRule.风飑:
                MoveAlliesTowardPlayer(caster);
                break;
            case DefenseEffectRule.岚匿:
                ProtectRandomAlly(caster);
                break;
            case DefenseEffectRule.飔垣:
                ShieldWeakestAlly(caster);
                break;
            case DefenseEffectRule.精神压制:
                ApplyTemporaryPair(card, target, 2);
                break;
            case DefenseEffectRule.炎障:
                caster.AddShield(3);
                caster.DefenseState.SetFireBarrier(FindNamedBuff(card, "灼烧"));
                break;
            case DefenseEffectRule.神圣祝福:
                (target ?? caster).ApplyBuff(FindNamedBuff(card, "神圣"));
                break;
        }
    }

    private static BuffData FindNamedBuff(CardData card, string name)
    {
        if (card == null) return null;
        if (card.specialBuff != null && card.specialBuff.buffName == name) return card.specialBuff;
        foreach (BuffData buff in card.buffs)
            if (buff != null && buff.buffName == name) return buff;
        return null;
    }

    private static void ApplyNamedBuff(CardData card, Unit target, string name, int stacks)
    {
        if (target != null) target.ApplyBuff(FindNamedBuff(card, name), stacks);
    }

    private static void ApplyTemporaryPair(CardData card, Unit target, int turns)
    {
        if (target == null) return;
        target.ApplyFixedStackBuff(FindNamedBuff(card, "无力"), 2, turns);
        target.ApplyFixedStackBuff(FindNamedBuff(card, "残废"), 2, turns);
    }

    private static bool IsAlly(Unit caster, Unit other) =>
        caster != null && other != null && !other.IsDead &&
        (caster is PlayerUnit) == (other is PlayerUnit);

    private static bool IsEnemy(Unit caster, Unit other) =>
        other != null && !other.IsDead && !IsAlly(caster, other);

    private static List<Unit> AllLivingUnits()
    {
        var units = new List<Unit>();
        foreach (Unit unit in Object.FindObjectsOfType<Unit>())
            if (unit != null && !unit.IsDead && unit.GetCurrentHealth() > 0) units.Add(unit);
        units.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        return units;
    }

    private static void ChangeTerrain(CardData card, HexCoord center)
    {
        foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(center, 1))
        {
            if (cell == null) continue;
            cell.terrain = card.generatedTerrain;
            cell.isSmoky = false;
            if (cell.occupyUnit != null) EventManager.Trigger("UnitMoved", cell.occupyUnit);
        }
    }

    private static void Summon(UnitData unitData, Unit caster, int count, bool adjacent)
    {
        if (unitData == null || unitData.prefab == null || HexGrid.Instance == null) return;
        var landings = new List<HexCell>();
        foreach (HexCell cell in HexGrid.Instance.GetAllCells())
            if (cell != null && cell.CanStopOn() &&
                (!adjacent || cell.coord.Distance(caster.GetCoord()) == 1)) landings.Add(cell);
        for (int i = 0; i < count && landings.Count > 0; i++)
        {
            int pick = Random.Range(0, landings.Count);
            HexCell cell = landings[pick];
            landings.RemoveAt(pick);
            if (!cell.CanStopOn()) continue;
            GameObject go = Object.Instantiate(unitData.prefab,
                HexGrid.Instance.HexToWorld(cell.coord), Quaternion.identity);
            EnemyUnit enemy = go.GetComponent<EnemyUnit>();
            if (enemy == null) enemy = go.AddComponent<EnemyUnit>();
            enemy.Initialize(unitData, cell.coord);
            LevelManager manager = Object.FindObjectOfType<LevelManager>();
            if (manager != null) manager.RegisterSummonedEnemy(enemy);
        }
    }

    private static void MoveAlliesTowardPlayer(Unit caster)
    {
        PlayerUnit player = Object.FindObjectOfType<PlayerUnit>();
        if (player == null || player.IsDead) return;
        foreach (Unit ally in AllLivingUnits())
        {
            if (!IsAlly(caster, ally) || (ally == caster && !caster.DefenseState.CanMoveSelf))
                continue;
            bool canAttack = false;
            if (ally is EnemyUnit enemy)
                foreach (CardData planned in enemy.PlannedSkills)
                    if (planned != null && planned.IsAttack &&
                        planned.IsInEffectiveRange(ally, ally.GetCoord(), player.GetCoord()) &&
                        (planned.attackTargetRule != AttackTargetRule.单位 ||
                         planned.CanSelectAttackCell(ally, player.GetCoord())) &&
                        (!planned.tags.Contains(CardTag.远程) || planned.ignoresLineBlock ||
                         (!HexGrid.Instance.IsLineBlocked(ally.GetCoord(), player.GetCoord()) &&
                          !HexGrid.Instance.IsCellSmoky(player.GetCoord()))))
                        { canAttack = true; break; }
            if (canAttack)
                continue;
            HexCoord best = HexPathfinding.FindNextStep(ally.GetCoord(), player.GetCoord());
            if (best != ally.GetCoord())
            {
                ally.MoveTo(best);
                if (ally == caster && ally.GetCoord() == best)
                    caster.DefenseState.MarkSelfMovement();
            }
        }
    }

    private static void ProtectRandomAlly(Unit caster)
    {
        var allies = new List<Unit>();
        foreach (Unit unit in AllLivingUnits())
            if (unit != caster && IsAlly(caster, unit)) allies.Add(unit);
        (allies.Count == 0 ? caster : allies[Random.Range(0, allies.Count)]).DefenseState.ProtectNextTurn();
    }

    private static void ShieldWeakestAlly(Unit caster)
    {
        Unit weakest = null;
        foreach (Unit unit in AllLivingUnits())
            if (unit != caster && IsAlly(caster, unit) &&
                (weakest == null || unit.GetCurrentHealth() < weakest.GetCurrentHealth())) weakest = unit;
        (weakest ?? caster).AddShield(3);
    }
}
