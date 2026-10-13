using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 敌人单位——继承 Unit，由 AI 控制
/// 挂在敌人单位的 GameObject 上
///
/// 技能相关的两条时间线：
/// - 回合开场：TurnManager 调 PreparePlannedSkills() 决定这回合放什么（展示给玩家）；
/// - 敌方行动：TurnManager 调 ExecutePlannedSkills() 按预告逐个放技能，
///   实际放出的技能会存进 lastTurnUsedSkills，供玩家"下一回合"窃取。
/// </summary>
public class EnemyUnit : Unit
{
    [Header("AI 设置")]
    [Tooltip("AI 思考间隔（秒）")]
    public float thinkDelay = 0.8f;
    [Tooltip("每步移动的间隔（秒）")]
    public float stepDelay = 0.3f;

    [Header("技能预告")]
    [Tooltip("每回合预告并释放的技能数量（题目举例的'上回合用了 5 个技能'就把这里设成 5）")]
    public int skillsPerTurn = 1;

    // 当前使用哪个技能组（1/2/3，对应 UnitData 上配置的三组技能；后续可做阶段切换）
    protected int currentSkillGroup = 1;
    protected bool hasActedThisTurn = false;

    // 本回合"预告"将要释放的技能——开场生成，行动时照此执行，行动完清空
    protected readonly List<CardData> plannedSkills = new List<CardData>();

    // 上回合"实际"释放过的技能。保留重复（同一技能放两次就是两条），
    // 玩家窃取时 PlayerCardSystem 会再做去重和敌方特有过滤
    protected readonly List<CardData> lastTurnUsedSkills = new List<CardData>();

    /// <summary>
    /// 是否已经行动过
    /// </summary>
    public bool HasActed => hasActedThisTurn;

    /// <summary>
    /// 本回合计划释放的技能（回合开始时展示给玩家看）。只读暴露。
    /// </summary>
    public IReadOnlyList<CardData> PlannedSkills => plannedSkills;

    /// <summary>
    /// 上回合实际使用过的技能（玩家窃取灌注的来源）。只读暴露。
    /// </summary>
    public IReadOnlyList<CardData> LastTurnUsedSkills => lastTurnUsedSkills;

    // ============================================================
    //                       初始化
    // ============================================================

    public override void Initialize(UnitData unitData, HexCoord startCoord)
    {
        base.Initialize(unitData, startCoord);
        currentSkillGroup = 1;
        hasActedThisTurn = false;
    }

    // ============================================================
    //                     回合生命周期
    // ============================================================

    public override void OnTurnStart()
    {
        base.OnTurnStart();
        hasActedThisTurn = false;
    }

    public override void OnTurnEnd()
    {
        base.OnTurnEnd();
    }

    // ============================================================
    //                    技能预告 / 技能释放
    // ============================================================

    /// <summary>
    /// 获取当前激活的技能组——根据 currentSkillGroup 的编号，
    /// 从 UnitData 配置的三组技能里返回对应那组。
    /// </summary>
    public virtual List<CardData> GetActiveSkillGroup()
    {
        if (data == null) return new List<CardData>();

        switch (currentSkillGroup)
        {
            case 2: return data.skillGroup2;
            case 3: return data.skillGroup3;
            default: return data.skillGroup1;   // 1 或其他异常值都用第一组
        }
    }

    /// <summary>
    /// 【回合开场调用】生成本回合将要释放的技能（先预告、后执行）。
    /// 从当前技能组里有放回地随机抽 skillsPerTurn 个（允许同一个技能重复出现）。
    /// </summary>
    public virtual void PreparePlannedSkills()
    {
        plannedSkills.Clear();

        // 复制一份技能组并过滤掉空引用，避免直接操作 UnitData 的原始列表
        List<CardData> group = new List<CardData>();
        foreach (var card in GetActiveSkillGroup())
            if (card != null) group.Add(card);

        if (group.Count == 0)
        {
            // 策划没配技能，给警告但不崩溃
            Debug.LogWarning($"{data.unitName} 的技能组 {currentSkillGroup} 没有配置技能");
            return;
        }

        for (int i = 0; i < skillsPerTurn; i++)
            plannedSkills.Add(group[Random.Range(0, group.Count)]);
    }

    /// <summary>
    /// 【敌方行动阶段调用】按预告顺序释放全部技能。是一个协程，
    /// 每放一个技能就等待 perSkillDelay 秒，由 TurnManager 用 yield return 驱动。
    /// 每成功释放一个技能，都会记入 lastTurnUsedSkills，供玩家下回合窃取。
    /// </summary>
    public virtual IEnumerator ExecutePlannedSkills(float perSkillDelay)
    {
        // 清空上回合的记录，开始记录本回合实际释放的技能
        lastTurnUsedSkills.Clear();
        hasActedThisTurn = true;

        foreach (var card in plannedSkills)
        {
            // 每放一个技能前都检查：自己可能已被前面的反伤/队友流弹打死，
            // 或者战斗已经因胜负结束——两种情况都立刻终止
            if (GetCurrentHealth() <= 0) yield break;
            if (GameManager.Instance != null && !GameManager.Instance.IsInBattle)
                yield break;

            if (card == null) continue;

            // 确定目标：
            // - 攻击卡 / 对敌方用的防御卡（精神压制等）→ 最近的玩家
            // - 对自身用的防御卡（格挡等）→ 自己
            Unit target;
            bool targetsPlayer = card.IsAttack ||
                                 (!card.IsAttack && card.CanDefenseEnemy && !card.CanDefenseSelf);
            if (targetsPlayer)
            {
                target = FindNearestPlayer();
                if (target == null) continue;   // 玩家不存在（已死亡），跳过
            }
            else
                target = this;

            // 攻击/对敌技能射程校验：目标是玩家（不是自己）时检查距离，超出就跳过
            if (target != this)
            {
                int distance = GetCoord().Distance(target.GetCoord());
                if (distance < card.minRange || distance > card.maxRange)
                {
                    Debug.Log($"{data.unitName} 的【{card.cardName}】射程 {card.minRange}~{card.maxRange}，" +
                              $"目标距离 {distance}，超出射程，跳过");
                    continue;
                }
            }

            Debug.Log($"{data.unitName} 释放【{card.cardName}】");
            EventManager.Trigger("EnemySkillExecuting", this, card);  // UI/特效监听此事件

            // 和玩家出牌走的是同一个结算方法（同一套伤害/buff 规则）
            card.Resolve(this, target);

            // 记录"实际放出"的技能（只有走到这里的才算，中途死了没放的不算）
            lastTurnUsedSkills.Add(card);

            yield return new WaitForSeconds(perSkillDelay);
        }

        // 本回合预告已全部执行完，清空预告队列
        plannedSkills.Clear();
    }

    // ============================================================
    //                       AI 行动
    // ============================================================

    /// <summary>
    /// 开始这个敌人的回合行动
    /// 由 TurnManager 或 BattleManager 调用
    /// </summary>
    public void TakeTurn()
    {
        if (GameManager.Instance != null && GameManager.Instance.isPlayerTurn)
            return;

        StartCoroutine(AITurnRoutine());
    }

    /// <summary>
    /// AI 行动协程
    /// </summary>
    protected virtual IEnumerator AITurnRoutine()
    {
        hasActedThisTurn = false;

        // 思考一会
        yield return new WaitForSeconds(thinkDelay);

        // 1. 找最近的玩家单位
        PlayerUnit target = FindNearestPlayer();
        if (target == null)
        {
            EndTurn();
            yield break;
        }

        int distance = currentCoord.Distance(target.GetCoord());

        // 2. 判断：相邻就攻击，否则移动靠近
        if (distance == 1)
        {
            // 相邻 → 攻击
            yield return new WaitForSeconds(0.3f);
            AttackTarget(target);
        }
        else
        {
            // 不相邻 → 移动靠近
            yield return MoveTowardsTarget(target);

            // 移动完看看是不是到攻击范围了
            int newDistance = currentCoord.Distance(target.GetCoord());
            if (newDistance == 1)
            {
                yield return new WaitForSeconds(0.3f);
                AttackTarget(target);
            }
        }

        // 行动结束
        yield return new WaitForSeconds(0.3f);
        EndTurn();
    }

    // ============================================================
    //                       移动 AI
    // ============================================================

    /// <summary>
    /// 朝目标移动（走一步）
    /// </summary>
    protected virtual IEnumerator MoveTowardsTarget(Unit target)
    {
        HexCoord nextStep = HexPathfinding.FindNextStep(currentCoord, target.GetCoord());

        if (nextStep == currentCoord)
        {
            yield break;  // 无法移动
        }

        // 检查目标格是否可走
        HexCell nextCell = HexGrid.Instance.GetCell(nextStep);
        if (nextCell == null || !nextCell.isWalkable || nextCell.occupyUnit != null)
        {
            yield break;
        }

        // 离开旧格子
        HexCell oldCell = HexGrid.Instance.GetCell(currentCoord);
        if (oldCell != null)
            oldCell.occupyUnit = null;

        // 站上新格子
        currentCoord = nextStep;
        nextCell.occupyUnit = this;

        // 动画延迟（以后改成为移动动画）
        yield return new WaitForSeconds(stepDelay);

        // 更新位置
        transform.position = HexGrid.Instance.HexToWorld(nextStep);

        OnMove();
    }

    // ============================================================
    //                       攻击 AI
    // ============================================================

    /// <summary>
    /// 攻击目标
    /// </summary>
    protected virtual void AttackTarget(Unit target)
    {
        if (!CanAttack())
            return;

        int damage = GetAttack();
        OnAttack();
        target.TakeDamage(damage);

        hasActedThisTurn = true;
        EventManager.Trigger("EnemyAttacked", this, target);
    }

    // ============================================================
    //                       找目标
    // ============================================================

    /// <summary>
    /// 找最近的玩家单位
    /// </summary>
    protected virtual PlayerUnit FindNearestPlayer()
    {
        PlayerUnit[] allPlayers = FindObjectsOfType<PlayerUnit>();
        PlayerUnit nearest = null;
        int minDist = int.MaxValue;

        foreach (var player in allPlayers)
        {
            if (player.GetCurrentHealth() <= 0) continue;

            int dist = currentCoord.Distance(player.GetCoord());
            if (dist < minDist)
            {
                minDist = dist;
                nearest = player;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 找所有可见的玩家单位（视线范围内）
    /// </summary>
    protected virtual List<PlayerUnit> FindVisiblePlayers(int range)
    {
        List<PlayerUnit> result = new List<PlayerUnit>();
        PlayerUnit[] allPlayers = FindObjectsOfType<PlayerUnit>();

        foreach (var player in allPlayers)
        {
            if (player.GetCurrentHealth() <= 0) continue;

            int dist = currentCoord.Distance(player.GetCoord());
            if (dist <= range)
                result.Add(player);
        }

        return result;
    }

    // ============================================================
    //                       回合结束
    // ============================================================

    /// <summary>
    /// 结束这个敌人的回合
    /// </summary>
    protected virtual void EndTurn()
    {
        hasActedThisTurn = true;
        EventManager.Trigger("EnemyTurnEnded", this);

        // 检查是否所有敌人都行动完了
        CheckAllEnemiesActed();
    }

    /// <summary>
    /// 检查是否所有敌人都行动完了，如果是就结束敌人回合
    /// </summary>
    protected virtual void CheckAllEnemiesActed()
    {
        EnemyUnit[] allEnemies = FindObjectsOfType<EnemyUnit>();
        foreach (var enemy in allEnemies)
        {
            if (enemy.GetCurrentHealth() > 0 && !enemy.HasActed)
                return;  // 还有敌人没行动
        }

        // 所有敌人都行动完了 → 结束敌人回合
        if (GameManager.Instance != null)
            GameManager.Instance.EndEnemyTurn();
    }

    // ============================================================
    //                       死亡重写
    // ============================================================

    protected override void Die()
    {
        base.Die();   // 先执行基类死亡逻辑

        // 敌人死亡：检查是否所有敌人都死了
        EnemyUnit[] allEnemies = FindObjectsOfType<EnemyUnit>();
        bool allDead = true;
        foreach (var e in allEnemies)
        {
            if (e != this && e.GetCurrentHealth() > 0)
            {
                allDead = false;
                break;
            }
        }

        if (allDead)
        {
            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerVictory();
        }
    }
}
