using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyConditionType
{
    距离 = 0,
    血量 = 1,
    Buff = 2,
    指定单位存在 = 3,
    视线畅通 = 4,
    上回合使用技能 = 5,
    护盾 = 6
}

public enum EnemyConditionSubject
{
    自身 = 0,
    主要目标 = 1
}

public enum EnemyNumberComparison
{
    小于 = 0,
    小于等于 = 1,
    等于 = 2,
    大于等于 = 3,
    大于 = 4
}

public enum EnemyHealthValueType
{
    当前血量 = 0,
    血量百分比 = 1
}

public enum EnemyUnitRelation
{
    任意 = 0,
    友方 = 1,
    敌方 = 2
}

public enum EnemyDecisionActionType
{
    选择技能组 = 0,
    靠近主要目标 = 1,
    远离主要目标 = 2,
    跳过行动 = 3,
    固定技能组循环 = 4,
    位移牌保持距离 = 5,
    保留技能组顺序 = 6
}

/// <summary>
/// 规则命中后依次写入 EnemyDecision 的行为。
/// 靠近/远离只为位移技能提供选点方向，不会让敌人在技能牌之外移动。
/// </summary>
[Serializable]
public sealed class EnemyDecisionAction
{
    public EnemyDecisionActionType actionType;

    [Min(0)]
    [Tooltip("选择技能组时填写 UnitData.skillGroups 索引，从 0 开始")]
    public int skillGroupIndex;

    [Tooltip("固定技能组循环使用的索引顺序，例如 0,1,1,2；每次该规则命中时前进一步")]
    public List<int> skillGroupCycle = new List<int>();

    [Tooltip("固定循环开始前只执行一次的技能组索引；留空时直接进入循环。例如开场 4,2，随后 1,3,2 循环，可填写 opening=3,1，cycle=0,2,1")]
    public List<int> skillGroupOpening = new List<int>();

    [Min(1)]
    [Tooltip("“保持距离”时填写期望格数；实际移动上限始终由位移技能牌决定")]
    public int movementDistance = 1;
}

/// <summary>一条可在 Inspector 配置的敌人决策基础条件。</summary>
[Serializable]
public sealed class EnemyDecisionCondition
{
    [Tooltip("要检查的基础条件类型")]
    public EnemyConditionType conditionType;

    [Tooltip("血量、Buff 和护盾条件检查谁；距离固定检查自身与主要目标")]
    public EnemyConditionSubject subject = EnemyConditionSubject.自身;

    [Tooltip("距离、血量和护盾使用的比较方式")]
    public EnemyNumberComparison comparison = EnemyNumberComparison.小于等于;

    [Tooltip("距离格数、当前血量、护盾值，或 0~100 的血量百分比")]
    [Min(0)]
    public int value;

    [Tooltip("血量条件使用当前血量还是百分比")]
    public EnemyHealthValueType healthValueType = EnemyHealthValueType.血量百分比;

    [Tooltip("Buff 条件要检查的 BuffData")]
    public BuffData buff;

    [Tooltip("“上回合使用技能”条件要检查的 CardData")]
    public CardData skill;

    [Tooltip("指定单位存在条件优先按 UnitData 引用匹配")]
    public UnitData specifiedUnit;

    [Tooltip("未设置 UnitData 时，按 UnitData.unitName 精确匹配")]
    public string specifiedUnitName;

    [Tooltip("指定单位相对决策者的阵营")]
    public EnemyUnitRelation unitRelation = EnemyUnitRelation.任意;

    [Tooltip("指定单位存在的检查是否包含决策者自己")]
    public bool includeSelf;

    [Tooltip("反转最终结果，可表示没有 Buff、指定单位不存在等条件")]
    public bool invert;

    public bool Evaluate(EnemyUnit owner, Unit primaryTarget)
    {
        bool result;
        switch (conditionType)
        {
            case EnemyConditionType.距离:
                result = owner != null && IsAlive(primaryTarget) &&
                         Compare(owner.GetCoord().Distance(primaryTarget.GetCoord()), value);
                break;
            case EnemyConditionType.血量:
                Unit healthUnit = ResolveSubject(owner, primaryTarget);
                if (!IsAlive(healthUnit)) result = false;
                else
                {
                    int actual = healthValueType == EnemyHealthValueType.当前血量
                        ? healthUnit.GetCurrentHealth()
                        : Mathf.RoundToInt(healthUnit.GetCurrentHealth() * 100f /
                                           Mathf.Max(1, healthUnit.GetMaxHealth()));
                    result = Compare(actual, value);
                }
                break;
            case EnemyConditionType.Buff:
                Unit buffUnit = ResolveSubject(owner, primaryTarget);
                result = buff != null && IsAlive(buffUnit) && buffUnit.HasBuff(buff);
                break;
            case EnemyConditionType.指定单位存在:
                result = HasSpecifiedLivingUnit(owner);
                break;
            case EnemyConditionType.视线畅通:
                result = owner != null && IsAlive(primaryTarget) && HexGrid.Instance != null &&
                         !HexGrid.Instance.IsLineBlocked(owner.GetCoord(), primaryTarget.GetCoord()) &&
                         !HexGrid.Instance.IsCellSmoky(primaryTarget.GetCoord());
                break;
            case EnemyConditionType.上回合使用技能:
                result = owner != null && skill != null && UsedSkillLastTurn(owner, skill);
                break;
            case EnemyConditionType.护盾:
                Unit shieldUnit = ResolveSubject(owner, primaryTarget);
                result = IsAlive(shieldUnit) && Compare(shieldUnit.GetShield(), value);
                break;
            default:
                result = false;
                break;
        }

        return invert ? !result : result;
    }

    private bool HasSpecifiedLivingUnit(EnemyUnit owner)
    {
        if (specifiedUnit == null && string.IsNullOrWhiteSpace(specifiedUnitName)) return false;

        foreach (Unit candidate in UnityEngine.Object.FindObjectsOfType<Unit>())
        {
            if (!IsAlive(candidate) || (!includeSelf && candidate == owner)) continue;
            if (!MatchesRelation(owner, candidate)) continue;
            if (specifiedUnit != null)
            {
                if (candidate.data == specifiedUnit) return true;
            }
            else if (candidate.data != null && candidate.data.unitName == specifiedUnitName)
                return true;
        }
        return false;
    }

    private static bool UsedSkillLastTurn(EnemyUnit owner, CardData expected)
    {
        foreach (CardData used in owner.LastTurnUsedSkills)
            if (used == expected) return true;
        return false;
    }

    private bool MatchesRelation(EnemyUnit owner, Unit candidate)
    {
        if (unitRelation == EnemyUnitRelation.任意) return true;
        bool sameSide = owner != null && candidate is EnemyUnit;
        return unitRelation == EnemyUnitRelation.友方 ? sameSide : !sameSide;
    }

    private bool Compare(int actual, int expected)
    {
        switch (comparison)
        {
            case EnemyNumberComparison.小于: return actual < expected;
            case EnemyNumberComparison.小于等于: return actual <= expected;
            case EnemyNumberComparison.等于: return actual == expected;
            case EnemyNumberComparison.大于等于: return actual >= expected;
            case EnemyNumberComparison.大于: return actual > expected;
            default: return false;
        }
    }

    private Unit ResolveSubject(EnemyUnit owner, Unit primaryTarget)
    {
        return subject == EnemyConditionSubject.自身 ? owner : primaryTarget;
    }

    private static bool IsAlive(Unit unit)
    {
        return unit != null && !unit.IsDead && unit.GetCurrentHealth() > 0;
    }
}

/// <summary>全部条件满足时选择指定技能组；EnemyUnit 按列表顺序使用首个命中的规则。</summary>
[Serializable]
public sealed class EnemyDecisionRule
{
    public string ruleName;

    [Tooltip("同一规则内所有条件都必须满足；空列表表示始终满足")]
    public List<EnemyDecisionCondition> conditions = new List<EnemyDecisionCondition>();

    [Tooltip("规则命中后按顺序执行；可同时选择技能组并设置移动行为")]
    public List<EnemyDecisionAction> actions = new List<EnemyDecisionAction>();

    public bool Matches(EnemyUnit owner, Unit primaryTarget)
    {
        if (conditions == null) return true;
        foreach (EnemyDecisionCondition condition in conditions)
            if (condition == null || !condition.Evaluate(owner, primaryTarget)) return false;
        return true;
    }
}
