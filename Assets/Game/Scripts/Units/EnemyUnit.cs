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

    [Header("移动")]
    [Tooltip("射程够不到玩家时，这一回合最多朝玩家走几格")]
    public int maxStepsPerTurn = 3;

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
        currentSkillGroup = Random.Range(0,data.skillGroups.Count);
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
        currentSkillGroup = Random.Range(0,data.skillGroups.Count);
        
        if (data == null || data.skillGroups[currentSkillGroup].cards.Count == 0) return new List<CardData>();

        return data.skillGroups[currentSkillGroup].cards;
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

        for (int i = 0; i < group.Count; i++)
            plannedSkills.Add(group[i]);

        // 按"怪物行动顺序"给预告技能排序（文档：瞄准 → 带位移的攻击 → 位移 → 特技 → 纯攻击）
        ReorderByPriority();

        // 打印带序号的预告，方便实战对照 UI 上的行动顺序数字
        string orderText = "";
        for (int i = 0; i < plannedSkills.Count; i++)
            orderText += $"{i + 1}.{plannedSkills[i].cardName} ";
        Debug.Log($"{data.unitName} 行动顺序：{orderText}");
    }

    /// <summary>
    /// 按卡牌的 SkillPriority 给预告技能排序；同一优先级内部随机打乱。
    /// 做法：按优先级从 0 到 4 分组，每组内部先洗牌再依次拼接——
    /// 这样既保证优先级顺序，又实现"同种情况随机"。
    /// </summary>
    protected virtual void ReorderByPriority()
    {
        List<CardData> ordered = new List<CardData>();

        for (int priority = 0; priority <= 4; priority++)
        {
            List<CardData> samePriority = new List<CardData>();
            foreach (var card in plannedSkills)
            {
                if (card != null && card.SkillPriority == priority)
                    samePriority.Add(card);
            }

            ShuffleList(samePriority);
            ordered.AddRange(samePriority);
        }

        plannedSkills.Clear();
        plannedSkills.AddRange(ordered);
    }

    /// <summary>Fisher-Yates 洗牌（同优先级随机用）</summary>
    protected static void ShuffleList(List<CardData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            CardData temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
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

            if (!card.IsAttack)
            {
                var defenseSelection = new List<HexCoord>();
                if (card.defenseTargetRule != DefenseTargetRule.自身 ||
                    (card.defenseEffectRule == DefenseEffectRule.神圣祝福 && HasNamedBuff("神圣")))
                {
                    List<HexCoord> legal = DefenseSkillTargeting.GetSelectableCells(card, this,
                        defenseSelection);
                    if (legal.Count == 0) continue;
                    if (card.defenseTargetRule == DefenseTargetRule.多个单位)
                    {
                        int limit = Mathf.Min(Mathf.Max(1, card.maxDefenseTargets), legal.Count);
                        for (int i = 0; i < limit; i++) defenseSelection.Add(legal[i]);
                    }
                    else defenseSelection.Add(legal[Random.Range(0, legal.Count)]);
                }
                if (!DefenseSkillResolver.CanResolve(card, this, defenseSelection)) continue;
                EventManager.Trigger("EnemySkillExecuting", this, card);
                if (!DefenseSkillResolver.Resolve(card, this, defenseSelection)) continue;
                lastTurnUsedSkills.Add(card);
                yield return new WaitForSeconds(perSkillDelay);
                continue;
            }

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
            // 走 CardData 的统一判定，位移类技能会自动算上敏捷/残废
            bool needsPrimaryTarget = card.attackTargetRule != AttackTargetRule.自身范围 &&
                card.attackTargetRule != AttackTargetRule.全场 &&
                card.attackTargetRule != AttackTargetRule.全场随机 &&
                card.attackTargetRule != AttackTargetRule.固定地图图案;
            if (target != this && needsPrimaryTarget)
            {
                // 够不到就先朝玩家走（最多 maxStepsPerTurn 格），走到能打为止。
                // 不接这一步的话，只会近战的怪会永远站在原地——第 1 关神庙鼠
                // 只有射程 1 的"啃咬"，开局距离 2，整局都不会动。
                if (!card.IsInEffectiveRange(this, GetCoord(), target.GetCoord()))
                {
                    yield return ApproachTarget(target, card);

                    // 走路途中可能被反伤/地形打死，或者战斗已经结束
                    if (GetCurrentHealth() <= 0) yield break;
                    if (GameManager.Instance != null && !GameManager.Instance.IsInBattle) yield break;
                }

                // 走完还是够不到（被柱子挡住 / 走满步数还不够）→ 本回合跳过这个技能
                if (!card.IsInEffectiveRange(this, GetCoord(), target.GetCoord()))
                {
                    int distance = GetCoord().Distance(target.GetCoord());
                    Debug.Log($"{data.unitName} 的【{card.cardName}】射程 {card.minRange}~{card.GetEffectiveMaxRange(this)}，" +
                              $"目标距离 {distance}，超出射程，跳过");
                    continue;
                }
                if (card.requiresStraightLine &&
                    AttackAreaGeometry.StraightDirection(GetCoord(), target.GetCoord()) < 0)
                    continue;
                if (card.IsAttack &&
                    card.attackTargetRule != AttackTargetRule.相邻地块定向扇形 &&
                    !card.CanSelectAttackCell(this, target.GetCoord()))
                    continue;

                // 视线校验：和玩家侧 PlayerCardSystem.IsTargetReachable 用同一条规则。
                // 原来敌人只查射程 + 可选中，能隔着柱子、别的单位或烟雾直接打到玩家，
                // 而玩家用同一张远程卡却打不了，攻防不对称。
                // 被挡住时不绕路（ApproachTarget 在射程内会立刻返回），本回合直接放弃这个技能。
                if (!HasLineOfSight(card, target))
                {
                    Debug.Log($"{data.unitName} 的【{card.cardName}】视线被阻挡（单位/障碍物/烟雾），本回合跳过");
                    continue;
                }
            }

            Debug.Log($"{data.unitName} 释放【{card.cardName}】");
            EventManager.Trigger("EnemySkillExecuting", this, card);  // UI/特效监听此事件

            // 范围技能先确定格子，再由同一张卡结算。轰击的空格是有效选格，
            // 即使该格当前没人，仍会列入本次攻击范围。
            if (card.attackTargetRule == AttackTargetRule.多个单位)
            {
                var selected = new List<HexCoord> { target.GetCoord() };
                var emptyNeighbors = new List<HexCoord>();
                if (HexGrid.Instance != null)
                    foreach (HexCoord coord in target.GetCoord().GetAllNeighbors())
                    {
                        HexCell cell = HexGrid.Instance.GetCell(coord);
                        if (cell != null && cell.occupyUnit == null && cell.obstacle == null)
                            emptyNeighbors.Add(coord);
                    }
                for (int i = 0; i < 2 && emptyNeighbors.Count > 0; i++)
                {
                    int pick = Random.Range(0, emptyNeighbors.Count);
                    selected.Add(emptyNeighbors[pick]);
                    emptyNeighbors.RemoveAt(pick);
                }
                EventManager.Trigger("EnemyAttackCellsChosen", this, card, selected);
                var area = new List<AttackAreaCell>();
                foreach (HexCoord coord in selected)
                    area.Add(new AttackAreaCell(coord, card.damage));
                card.ResolveArea(this, area);
            }
            else if (card.attackTargetRule == AttackTargetRule.相邻地块定向扇形)
            {
                HexCoord chosen = GetCoord().GetNeighbor(0);
                int bestDistance = int.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    HexCoord neighbor = GetCoord().GetNeighbor(i);
                    int distance = neighbor.Distance(target.GetCoord());
                    if (distance < bestDistance)
                    {
                        chosen = neighbor;
                        bestDistance = distance;
                    }
                }
                EventManager.Trigger("EnemyAttackCellsChosen", this, card, new List<HexCoord> { chosen });
                card.ResolveArea(this, AttackAreaGeometry.GetCone(GetCoord(), chosen));
            }
            else if (card.attackTargetRule == AttackTargetRule.固定地图图案)
            {
                int patternIndex = Random.Range(0, 4);
                IReadOnlyList<HexCoord> pattern = AttackAreaGeometry.GetFixedBombardmentPattern(patternIndex);
                var selected = new List<HexCoord>(pattern);
                EventManager.Trigger("EnemyAttackCellsChosen", this, card, selected);
                var area = new List<AttackAreaCell>();
                foreach (HexCoord coord in selected)
                    area.Add(new AttackAreaCell(coord, card.damage));
                card.ResolveArea(this, area);
            }
            else if (card.attackTargetRule == AttackTargetRule.自身范围 ||
                     card.attackTargetRule == AttackTargetRule.全场 ||
                     card.attackTargetRule == AttackTargetRule.全场随机 ||
                     card.attackTargetRule == AttackTargetRule.地块 ||
                     card.attackTargetRule == AttackTargetRule.穿透单位 ||
                     card.attackTargetRule == AttackTargetRule.连锁单位)
            {
                HexCoord chosen = target == null ? GetCoord() : target.GetCoord();
                card.ResolveArea(this, card.GetAttackArea(this, chosen));
            }
            else
                card.Resolve(this, target);

            // 记录"实际放出"的技能（只有走到这里的才算，中途死了没放的不算）
            lastTurnUsedSkills.Add(card);

            yield return new WaitForSeconds(perSkillDelay);
        }

        // 本回合预告已全部执行完，清空预告队列
        plannedSkills.Clear();
    }

    // ============================================================
    //                       移动靠近（行动中）
    // ============================================================

    /// <summary>
    /// 敌人版视线判定，规则与玩家侧的 PlayerCardSystem.IsTargetReachable 完全一致：
    ///   · 只有带「远程」标签、且没有勾"无视遮挡"的技能才受限制；
    ///   · 施法者与目标之间有其他活着的单位或挡视线的障碍物 → 看不见；
    ///   · 目标站在烟雾格 → 锁不住。
    /// 近战技能不受影响（相邻本来就没有中间格）。
    /// </summary>
    protected virtual bool HasLineOfSight(CardData card, Unit target)
    {
        if (card == null || target == null || HexGrid.Instance == null) return true;
        if (!card.tags.Contains(CardTag.远程) || card.ignoresLineBlock) return true;

        if (HexGrid.Instance.IsLineBlocked(GetCoord(), target.GetCoord())) return false;
        if (HexGrid.Instance.IsCellSmoky(target.GetCoord())) return false;
        return true;
    }

    /// <summary>
    /// 朝目标走，直到这个技能的射程够得着为止（最多 maxStepsPerTurn 格）。
    ///
    /// 这是"敌人也会走过来打你"的关键：ExecutePlannedSkills 原来只放技能不移动，
    /// 只会近战的怪在距离 > 1 时整局站着不动。寻路复用 HexPathfinding.FindNextStep
    /// （它已经把障碍物、不可行走格、占位单位都算进去了），每走一格用 MoveTo，
    /// 这样格子占用、"移动时" buff、UnitMoved 事件都和正常移动一致。
    /// </summary>
    protected virtual IEnumerator ApproachTarget(Unit target, CardData card)
    {
        if (target == null || target.IsDead) yield break;
        if (HexGrid.Instance == null) yield break;

        for (int step = 0; step < maxStepsPerTurn; step++)
        {
            // 已经在射程里就不用再走了
            if (card.IsInEffectiveRange(this, GetCoord(), target.GetCoord())) yield break;

            HexCoord next = HexPathfinding.FindNextStep(GetCoord(), target.GetCoord());
            if (next == GetCoord()) yield break;   // 没有可走的下一步（被围住 / 已相邻）

            HexCell nextCell = HexGrid.Instance.GetCell(next);
            if (nextCell == null || !nextCell.CanEnter(this)) yield break;

            MoveTo(next);
            yield return new WaitForSeconds(stepDelay);
        }
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
        target.TakeDamage(damage, DamageType.物理, this);

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
    //                       死亡扩展
    // ============================================================

    /// <summary>
    /// 敌人死亡额外逻辑。
    /// 走基类的 OnDeath() 扩展点而不是覆写 Die()——基类保证这里
    /// 只会在"首次死亡"时被调用一次，不会重复判"全员胜利"。
    /// </summary>
    protected override void OnDeath()
    {
        // 敌人死亡：检查是否所有敌人都死了
        EnemyUnit[] allEnemies = FindObjectsOfType<EnemyUnit>();
        bool allDead = true;
        foreach (var e in allEnemies)
        {
            // 判 IsDead 而不是只看血量：Destroy 延迟到帧末，同帧内刚死的同伴对象还在
            if (e != this && !e.IsDead && e.GetCurrentHealth() > 0)
            {
                allDead = false;
                break;
            }
        }

        if (allDead && GameManager.Instance != null)
            GameManager.Instance.OnPlayerVictory();
    }
}
