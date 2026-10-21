using System;
using System.Collections.Generic;

/// <summary>
/// 敌人的位移牌选点意图。它只决定技能应该朝向或远离谁，不能直接移动单位。
/// 实际移动必须由带位移效果的 CardData 结算。
/// </summary>
public enum EnemyMovementIntent
{
    无 = 0,
    靠近目标 = 1,
    远离目标 = 2,
    随机移动 = 3,
    保持距离 = 4
}

/// <summary>
/// 敌人本回合的运行时决策快照。
/// 决策在玩家回合开始时生成，技能预告和敌方实际行动共用同一份数据，
/// 避免预告完成后再次随机导致显示与执行不一致。
/// </summary>
[Serializable]
public sealed class EnemyDecision
{
    public int RuleIndex { get; private set; } = -1;
    public int SkillGroupIndex { get; private set; } = -1;
    public IReadOnlyList<CardData> Skills => skills;
    public Unit PrimaryTarget { get; private set; }
    public EnemyMovementIntent Movement { get; private set; }
    public Unit MovementReference { get; private set; }
    public int MovementDistance { get; private set; }
    public bool SkipSkills { get; private set; }
    public bool PreserveSkillOrder { get; private set; }
    public string Reason { get; private set; }

    private readonly List<CardData> skills = new List<CardData>();

    public EnemyDecision SetRule(int index, string reason = null)
    {
        RuleIndex = index;
        Reason = reason;
        return this;
    }

    public EnemyDecision SetSkillGroup(int index)
    {
        SkillGroupIndex = index;
        return this;
    }

    public EnemyDecision SetSkills(IEnumerable<CardData> cards)
    {
        skills.Clear();
        if (cards == null) return this;
        foreach (CardData card in cards)
            if (card != null) skills.Add(card);
        return this;
    }

    public EnemyDecision SetPrimaryTarget(Unit target)
    {
        PrimaryTarget = target;
        return this;
    }

    public EnemyDecision SetMovement(EnemyMovementIntent intent, Unit reference = null, int distance = 0)
    {
        Movement = intent;
        MovementReference = reference;
        MovementDistance = Math.Max(0, distance);
        return this;
    }

    public EnemyDecision SetSkipSkills(bool skip, string reason = null)
    {
        SkipSkills = skip;
        Reason = reason;
        return this;
    }

    public EnemyDecision SetPreserveSkillOrder(bool preserve = true)
    {
        PreserveSkillOrder = preserve;
        return this;
    }
}
