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
    private const string PebbleBugName = "飞砾虫";
    private const string LindwormName = "林德沃姆";

    [Header("AI 设置")]
    [Tooltip("AI 思考间隔（秒）")]
    public float thinkDelay = 0.8f;
    [Tooltip("每步移动的间隔（秒）")]
    public float stepDelay = 0.3f;

    [Tooltip("按顺序判断，第一条全部满足的规则决定技能组；没有命中时随机选择")]
    public List<EnemyDecisionRule> decisionRules = new List<EnemyDecisionRule>();

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

    // 冷却和循环游标都属于单个敌人实例，不能写回共享的 CardData/规则配置。
    private readonly Dictionary<CardData, int> skillCooldowns = new Dictionary<CardData, int>();
    private readonly Dictionary<EnemyDecisionAction, int> skillGroupCyclePositions =
        new Dictionary<EnemyDecisionAction, int>();
    private readonly Dictionary<EnemyDecisionAction, int> skillGroupOpeningPositions =
        new Dictionary<EnemyDecisionAction, int>();
    private readonly List<PassiveRuntime> passiveRuntimes = new List<PassiveRuntime>();

    // 玩家看到的预告与敌方实际执行共享这份决策快照。
    protected EnemyDecision currentDecision;

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

    /// <summary>本回合已经确定的完整决策，供预告UI和后续AI规则只读查看。</summary>
    public EnemyDecision CurrentDecision => currentDecision;
    public IReadOnlyList<PassiveRuntime> PassiveRuntimes => passiveRuntimes;

    public T FindPassiveRuntime<T>() where T : PassiveRuntime
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime is T matched) return matched;
        return null;
    }

    /// <summary>
    /// 立即替换当前回合已经生成的技能预告。
    /// 用于“受伤后改变本回合技能”这类被动；预告 UI 每帧读取 PlannedSkills，
    /// 敌方行动也读取同一个 EnemyDecision，因此显示和实际执行会保持一致。
    /// </summary>
    public void OverridePlannedSkills(IEnumerable<CardData> skills, int skillGroupIndex,
        string reason = null)
    {
        if (currentDecision == null)
            currentDecision = new EnemyDecision().SetPrimaryTarget(FindNearestPlayer());

        var replacement = new List<CardData>();
        if (skills != null)
            foreach (CardData skill in skills)
                if (skill != null) replacement.Add(skill);

        currentSkillGroup = skillGroupIndex;
        currentDecision.SetSkillGroup(skillGroupIndex)
                       .SetSkipSkills(replacement.Count == 0, reason)
                       .SetSkills(replacement);

        plannedSkills.Clear();
        plannedSkills.AddRange(replacement);
        EventManager.Trigger("EnemyIntentChanged", this);
    }

    // ============================================================
    //                       初始化
    // ============================================================

    public override void Initialize(UnitData unitData, HexCoord startCoord)
    {
        DisposePassiveRuntimes();
        base.Initialize(unitData, startCoord);
        currentSkillGroup = 0;
        currentDecision = null;
        hasActedThisTurn = false;
        skillCooldowns.Clear();
        skillGroupCyclePositions.Clear();
        skillGroupOpeningPositions.Clear();
        RebuildPassiveRuntimes();
    }

    // ============================================================
    //                     回合生命周期
    // ============================================================

    public override void OnTurnStart()
    {
        base.OnTurnStart();
        hasActedThisTurn = false;
        TickSkillCooldowns();
        NotifyPassivesTurnStart();
    }

    public override void OnTurnEnd()
    {
        base.OnTurnEnd();
        NotifyPassivesTurnEnd();
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
        if (data == null || data.skillGroups == null || data.skillGroups.Count == 0)
            return new List<CardData>();

        currentSkillGroup = Mathf.Clamp(currentSkillGroup, 0, data.skillGroups.Count - 1);
        CardGroup group = data.skillGroups[currentSkillGroup];
        if (group == null || group.cards == null || group.cards.Count == 0)
            return new List<CardData>();

        return group.cards;
    }

    /// <summary>
    /// 【回合开场调用】生成本回合将要释放的技能（先预告、后执行）。
    /// 随机确定一个技能组，并把其中的有效技能按行动优先级写入决策快照。
    /// </summary>
    public virtual void PreparePlannedSkills()
    {
        plannedSkills.Clear();

        NotifyPassivesBeforeDecision();
        currentDecision = BuildDefaultDecision();
        NotifyPassivesAfterDecision(currentDecision);
        currentSkillGroup = currentDecision.SkillGroupIndex;

        // 特殊撤退也在预告阶段改成位移牌，让玩家看到的技能与实际执行保持一致。
        if (IsPebbleBug() && TryFindNearestLivingLindworm(out EnemyUnit lindworm))
        {
            OverrideWithPebbleBugRetreatSkill(lindworm);
            return;
        }

        if (currentDecision.SkipSkills)
        {
            currentDecision.SetSkills(null);
            return;
        }

        // 复制一份技能组并过滤掉空引用，避免直接操作 UnitData 的原始列表
        List<CardData> group = new List<CardData>();
        foreach (CardData card in currentDecision.Skills)
            if (card != null && !IsSkillOnCooldown(card)) group.Add(card);

        if (group.Count == 0)
        {
            // 技能组为空或全部处于冷却时仍保留方向意图，但不会在技能牌之外移动。
            currentDecision.SetSkills(null);
            string enemyName = data != null ? data.unitName : name;
            Debug.LogWarning($"{enemyName} 的技能组 {currentSkillGroup} 没有可用技能（未配置或正在冷却）");
            return;
        }

        for (int i = 0; i < group.Count; i++)
            plannedSkills.Add(group[i]);

        // 按"怪物行动顺序"给预告技能排序（文档：瞄准 → 带位移的攻击 → 位移 → 特技 → 纯攻击）
        if (!currentDecision.PreserveSkillOrder)
            ReorderByPriority();
        currentDecision.SetSkills(plannedSkills);

        // 打印带序号的预告，方便实战对照 UI 上的行动顺序数字
        string orderText = "";
        for (int i = 0; i < plannedSkills.Count; i++)
            orderText += $"{i + 1}.{plannedSkills[i].cardName} ";
        Debug.Log($"{data.unitName} 行动顺序：{orderText}");
    }

    /// <summary>
    /// 旧敌人的默认决策：随机选一个技能组，并以最近玩家为主要目标。
    /// 靠近/远离意图只指导位移技能选落点，不会直接移动单位。
    /// 后续 EnemyAIData/规则系统只需覆写或替换这个方法产出的决策。
    /// </summary>
    protected virtual EnemyDecision BuildDefaultDecision()
    {
        var decision = new EnemyDecision();
        PlayerUnit target = FindNearestPlayer();
        decision.SetPrimaryTarget(target)
                .SetMovement(EnemyMovementIntent.无);

        if (data == null || data.skillGroups == null || data.skillGroups.Count == 0)
            return decision.SetSkipSkills(true, "没有配置技能组");

        int selectedGroup = Random.Range(0, data.skillGroups.Count);
        int matchedRule = FindMatchingDecisionRule(target);
        if (matchedRule >= 0)
            ApplyDecisionActions(decisionRules[matchedRule], decision, target, ref selectedGroup);
        currentSkillGroup = selectedGroup;
        string reason = matchedRule >= 0
            ? $"命中决策规则：{GetDecisionRuleName(matchedRule)}"
            : "没有规则命中，随机选择技能组";
        return decision.SetRule(matchedRule, reason)
                       .SetSkillGroup(selectedGroup)
                       .SetSkills(GetActiveSkillGroup());
    }

    private int FindMatchingDecisionRule(Unit primaryTarget)
    {
        if (decisionRules != null)
        {
            for (int i = 0; i < decisionRules.Count; i++)
            {
                EnemyDecisionRule rule = decisionRules[i];
                if (rule == null || rule.actions == null || rule.actions.Count == 0 ||
                    !rule.Matches(this, primaryTarget)) continue;
                return i;
            }
        }
        return -1;
    }

    private void ApplyDecisionActions(EnemyDecisionRule rule, EnemyDecision decision,
        Unit primaryTarget, ref int selectedGroup)
    {
        if (rule.actions == null) return;
        foreach (EnemyDecisionAction action in rule.actions)
        {
            if (action == null) continue;
            switch (action.actionType)
            {
                case EnemyDecisionActionType.选择技能组:
                    if (action.skillGroupIndex >= 0 && action.skillGroupIndex < data.skillGroups.Count)
                        selectedGroup = action.skillGroupIndex;
                    else
                        Debug.LogWarning($"{data.unitName} 的决策规则指向不存在的技能组 " +
                                         $"{action.skillGroupIndex}，继续使用技能组 {selectedGroup}");
                    break;
                case EnemyDecisionActionType.固定技能组循环:
                    TrySelectCycledSkillGroup(action, ref selectedGroup);
                    break;
                case EnemyDecisionActionType.靠近主要目标:
                    decision.SetMovement(EnemyMovementIntent.靠近目标, primaryTarget,
                        Mathf.Max(1, action.movementDistance));
                    break;
                case EnemyDecisionActionType.远离主要目标:
                    decision.SetMovement(EnemyMovementIntent.远离目标, primaryTarget,
                        Mathf.Max(1, action.movementDistance));
                    break;
                case EnemyDecisionActionType.跳过行动:
                    decision.SetMovement(EnemyMovementIntent.无)
                            .SetSkipSkills(true, "规则要求跳过行动");
                    break;
                case EnemyDecisionActionType.位移牌保持距离:
                    decision.SetMovement(EnemyMovementIntent.保持距离, primaryTarget,
                        Mathf.Max(1, action.movementDistance));
                    break;
                case EnemyDecisionActionType.保留技能组顺序:
                    decision.SetPreserveSkillOrder();
                    break;
            }
        }
    }

    private void TrySelectCycledSkillGroup(EnemyDecisionAction action, ref int selectedGroup)
    {
        if (action.skillGroupOpening != null && action.skillGroupOpening.Count > 0)
        {
            skillGroupOpeningPositions.TryGetValue(action, out int openingPosition);
            while (openingPosition < action.skillGroupOpening.Count)
            {
                int candidate = action.skillGroupOpening[openingPosition++];
                skillGroupOpeningPositions[action] = openingPosition;
                if (candidate < 0 || candidate >= data.skillGroups.Count) continue;
                selectedGroup = candidate;
                return;
            }
        }

        if (action.skillGroupCycle == null || action.skillGroupCycle.Count == 0)
        {
            Debug.LogWarning($"{data.unitName} 的固定技能组循环没有配置任何索引");
            return;
        }

        skillGroupCyclePositions.TryGetValue(action, out int position);
        for (int offset = 0; offset < action.skillGroupCycle.Count; offset++)
        {
            int listIndex = (position + offset) % action.skillGroupCycle.Count;
            int candidate = action.skillGroupCycle[listIndex];
            if (candidate < 0 || candidate >= data.skillGroups.Count) continue;

            selectedGroup = candidate;
            skillGroupCyclePositions[action] = (listIndex + 1) % action.skillGroupCycle.Count;
            return;
        }

        Debug.LogWarning($"{data.unitName} 的固定技能组循环中没有有效的技能组索引");
    }

    public int GetSkillCooldownRemaining(CardData card)
    {
        return card != null && skillCooldowns.TryGetValue(card, out int turns) ? turns : 0;
    }

    private bool IsSkillOnCooldown(CardData card)
    {
        return GetSkillCooldownRemaining(card) > 0;
    }

    private void StartSkillCooldown(CardData card)
    {
        if (card == null || card.enemyCooldownTurns <= 0) return;
        skillCooldowns[card] = card.enemyCooldownTurns;
    }

    private void TickSkillCooldowns()
    {
        if (skillCooldowns.Count == 0) return;
        var cards = new List<CardData>(skillCooldowns.Keys);
        foreach (CardData card in cards)
        {
            int remaining = skillCooldowns[card] - 1;
            if (remaining <= 0) skillCooldowns.Remove(card);
            else skillCooldowns[card] = remaining;
        }
    }

    private void RebuildPassiveRuntimes()
    {
        DisposePassiveRuntimes();
        if (data == null || data.passiveAbilities == null) return;
        foreach (PassiveAbilityData passiveData in data.passiveAbilities)
        {
            if (passiveData == null) continue;
            PassiveRuntime runtime = passiveData.CreateRuntime(this);
            if (runtime == null)
            {
                Debug.LogWarning($"{data.unitName} 的被动【{passiveData.name}】没有创建运行时实例");
                continue;
            }
            passiveRuntimes.Add(runtime);
            runtime.Activate();
        }
    }

    private void DisposePassiveRuntimes()
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            runtime?.Deactivate();
        passiveRuntimes.Clear();
    }

    private void NotifyPassivesTurnStart()
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive) runtime.OnTurnStart();
    }

    private void NotifyPassivesTurnEnd()
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive) runtime.OnTurnEnd();
    }

    private void NotifyPassivesBeforeDecision()
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive) runtime.BeforeDecision();
    }

    private void NotifyPassivesAfterDecision(EnemyDecision decision)
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive) runtime.AfterDecision(decision);
    }

    private bool PassivesAllowSkill(CardData card)
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive && !runtime.CanUseSkill(card)) return false;
        return true;
    }

    /// <summary>
    /// 某些怪物被动允许在特定决策中把同阵营单位作为攻击目标。
    /// 默认所有被动都返回 false，不改变其他敌人的阵营判定。
    /// </summary>
    public bool CanTargetFriendlyWithAttack(Unit target)
    {
        if (target == null || target == this || !(target is EnemyUnit)) return false;
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive && runtime.CanTargetFriendlyWithAttack(target))
                return true;
        return false;
    }

    /// <summary>供被动在生成预告时复用执行前的射程、视线和选格检查。</summary>
    public bool CanHitTargetWithSkill(CardData card, Unit target)
    {
        if (card == null || !card.IsAttack) return false;
        bool needsPrimaryTarget = card.attackTargetRule != AttackTargetRule.自身范围 &&
            card.attackTargetRule != AttackTargetRule.全场 &&
            card.attackTargetRule != AttackTargetRule.全场随机 &&
            card.attackTargetRule != AttackTargetRule.固定地图图案;
        return CanExecutePlannedAttack(card, target, needsPrimaryTarget, out _);
    }

    private void NotifyPassivesSkillUsed(CardData card)
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive) runtime.OnSkillUsed(card);
    }

    protected override bool CanBeDisplaced(DisplacementType type)
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive && !runtime.CanBeDisplaced(type)) return false;
        return true;
    }

    public override bool CanBeAffectedByTerrain(TerrainData terrain)
    {
        foreach (PassiveRuntime runtime in passiveRuntimes)
            if (runtime != null && runtime.IsActive && !runtime.CanBeAffectedByTerrain(terrain))
                return false;
        return true;
    }

    protected virtual void OnDestroy()
    {
        DisposePassiveRuntimes();
    }

    private string GetDecisionRuleName(int index)
    {
        if (decisionRules == null || index < 0 || index >= decisionRules.Count ||
            decisionRules[index] == null || string.IsNullOrWhiteSpace(decisionRules[index].ruleName))
            return $"#{index + 1}";
        return decisionRules[index].ruleName;
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

        // 飞砾虫的特殊行动：只要场上仍有存活的林德沃姆，就把本次行动改成
        // 使用自身配置的位移技能远离林德沃姆。移动仍由技能牌结算，不直接 MoveTo。
        if (IsPebbleBug() && TryFindNearestLivingLindworm(out EnemyUnit lindworm))
            OverrideWithPebbleBugRetreatSkill(lindworm);

        if (currentDecision != null && currentDecision.SkipSkills)
        {
            plannedSkills.Clear();
            yield break;
        }

        // 执行时复制快照。若反伤在行动中触发“替换下次技能”的被动，
        // 被动可以安全更新 currentDecision，而不会修改正在 foreach 的集合。
        var skillsToExecute = new List<CardData>(currentDecision != null
            ? currentDecision.Skills
            : plannedSkills);
        foreach (var card in skillsToExecute)
        {
            // 每放一个技能前都检查：自己可能已被前面的反伤/队友流弹打死，
            // 或者战斗已经因胜负结束——两种情况都立刻终止
            if (GetCurrentHealth() <= 0) yield break;
            if (GameManager.Instance != null && !GameManager.Instance.IsInBattle)
                yield break;

            if (card == null) continue;
            if (IsSkillOnCooldown(card)) continue;
            if (!PassivesAllowSkill(card)) continue;

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
                    else defenseSelection.Add(ChooseDefenseCell(card, legal));
                }
                if (!DefenseSkillResolver.CanResolve(card, this, defenseSelection)) continue;
                EventManager.Trigger("EnemySkillExecuting", this, card);
                if (!DefenseSkillResolver.Resolve(card, this, defenseSelection)) continue;
                lastTurnUsedSkills.Add(card);
                StartSkillCooldown(card);
                NotifyPassivesSkillUsed(card);
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
                target = currentDecision != null ? currentDecision.PrimaryTarget : null;
                if (target == null || target.IsDead || target.GetCurrentHealth() <= 0)
                    target = FindNearestPlayer();
                if (target == null) continue;   // 玩家不存在（已死亡），跳过
            }
            else
                target = this;

            // 攻击在真正执行前按当前位置重新检查。玩家走出射程、阻断视线或使目标
            // 失去合法性后，敌人只会跳过该攻击，不会为了攻击而自动寻路靠近。
            bool needsPrimaryTarget = card.attackTargetRule != AttackTargetRule.自身范围 &&
                card.attackTargetRule != AttackTargetRule.全场 &&
                card.attackTargetRule != AttackTargetRule.全场随机 &&
                card.attackTargetRule != AttackTargetRule.固定地图图案;
            if (!CanExecutePlannedAttack(card, target, needsPrimaryTarget, out string failureReason))
            {
                Debug.Log($"{data.unitName} 的【{card.cardName}】无法命中：{failureReason}，跳过攻击");
                continue;
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
            StartSkillCooldown(card);
            NotifyPassivesSkillUsed(card);

            yield return new WaitForSeconds(perSkillDelay);
        }

        // 本回合预告已全部执行完，清空预告队列
        plannedSkills.Clear();
    }

    private HexCoord ChooseDefenseCell(CardData card, List<HexCoord> legal)
    {
        if (card == null || legal == null || legal.Count == 0) return GetCoord();
        Unit movementReference = currentDecision != null
            ? currentDecision.MovementReference ?? currentDecision.PrimaryTarget
            : null;
        if (card.defenseEffectRule != DefenseEffectRule.步行 || movementReference == null)
            return legal[Random.Range(0, legal.Count)];

        HexCoord target = movementReference.GetCoord();
        bool moveAway = currentDecision.Movement == EnemyMovementIntent.远离目标;
        bool keepDistance = currentDecision.Movement == EnemyMovementIntent.保持距离;
        int desiredDistance = Mathf.Max(0, currentDecision.MovementDistance);
        int best = moveAway ? int.MinValue : int.MaxValue;
        var candidates = new List<HexCoord>();
        foreach (HexCoord coord in legal)
        {
            int distance = coord.Distance(target);
            int score = keepDistance ? Mathf.Abs(distance - desiredDistance) : distance;
            bool better = moveAway ? distance > best : score < best;
            if (better)
            {
                best = moveAway ? distance : score;
                candidates.Clear();
            }
            if ((moveAway ? distance : score) == best) candidates.Add(coord);
        }
        return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : legal[0];
    }

    private bool IsPebbleBug()
    {
        return data != null && data.unitName == PebbleBugName;
    }

    private bool TryFindNearestLivingLindworm(out EnemyUnit nearest)
    {
        nearest = null;
        int nearestDistance = int.MaxValue;
        foreach (EnemyUnit enemy in FindObjectsOfType<EnemyUnit>())
        {
            if (enemy == null || enemy == this || enemy.IsDead || enemy.GetCurrentHealth() <= 0)
                continue;
            if (enemy.data == null || enemy.data.unitName != LindwormName)
                continue;

            int distance = currentCoord.Distance(enemy.GetCoord());
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = enemy;
        }
        return nearest != null;
    }

    /// <summary>
    /// 飞砾虫的特殊撤退仍通过它配置的位移牌完成。
    /// Movement 只负责告诉位移牌应该选择远离哪一方的落点，本身不直接移动单位。
    /// </summary>
    private void OverrideWithPebbleBugRetreatSkill(EnemyUnit lindworm)
    {
        CardData movementSkill = null;
        int movementGroup = -1;
        if (data != null && data.skillGroups != null)
        {
            for (int groupIndex = 0; groupIndex < data.skillGroups.Count && movementSkill == null;
                 groupIndex++)
            {
                CardGroup group = data.skillGroups[groupIndex];
                if (group == null || group.cards == null) continue;
                foreach (CardData candidate in group.cards)
                {
                    if (candidate == null || candidate.IsAttack || !candidate.IsMovementSkill ||
                        IsSkillOnCooldown(candidate)) continue;
                    movementSkill = candidate;
                    movementGroup = groupIndex;
                    break;
                }
            }
        }

        if (currentDecision == null) currentDecision = new EnemyDecision();
        currentDecision.SetPrimaryTarget(lindworm)
                       .SetMovement(EnemyMovementIntent.远离目标, lindworm, 1)
                       .SetSkillGroup(movementGroup)
                       .SetSkipSkills(movementSkill == null,
                           movementSkill == null ? "林德沃姆存在，但没有可用位移技能" : "林德沃姆存在")
                       .SetSkills(movementSkill == null ? null : new[] { movementSkill });

        plannedSkills.Clear();
        if (movementSkill != null)
        {
            currentSkillGroup = movementGroup;
            plannedSkills.Add(movementSkill);
        }
    }

    // ============================================================
    //                       攻击执行前检查
    // ============================================================

    private bool CanExecutePlannedAttack(CardData card, Unit target, bool needsPrimaryTarget,
        out string reason)
    {
        reason = null;
        if (card == null || !card.IsAttack)
        {
            reason = "不是有效的攻击技能";
            return false;
        }
        if (!CanAttack())
        {
            reason = "当前状态无法攻击";
            return false;
        }
        if (target == null || target.IsDead || target.GetCurrentHealth() <= 0)
        {
            reason = "没有存活目标";
            return false;
        }

        // 全场和固定图案以地图为目标；只要仍有存活目标就可以正常结算。
        if (!needsPrimaryTarget)
        {
            if (card.attackTargetRule != AttackTargetRule.自身范围) return true;
            foreach (AttackAreaCell cell in card.GetAttackArea(this, GetCoord()))
                if (cell.Coord == target.GetCoord()) return true;
            reason = "目标已经离开自身范围";
            return false;
        }

        int distance = GetCoord().Distance(target.GetCoord());
        if (!card.IsInEffectiveRange(this, GetCoord(), target.GetCoord()))
        {
            reason = $"目标距离 {distance}，超出射程 {card.minRange}~{card.GetEffectiveMaxRange(this)}";
            return false;
        }
        if (card.requiresStraightLine &&
            AttackAreaGeometry.StraightDirection(GetCoord(), target.GetCoord()) < 0)
        {
            reason = "目标不在要求的直线上";
            return false;
        }
        if (card.attackTargetRule != AttackTargetRule.相邻地块定向扇形 &&
            !card.CanSelectAttackCell(this, target.GetCoord()))
        {
            reason = "目标或攻击路径不合法";
            return false;
        }
        if (!HasLineOfSight(card, target))
        {
            reason = "视线被单位、障碍物或烟雾阻挡";
            return false;
        }
        return true;
    }

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

        // 旧入口也统一走技能决策与技能结算，禁止在技能牌之外自动寻路或普通攻击。
        PreparePlannedSkills();
        yield return ExecutePlannedSkills(stepDelay);

        // 行动结束
        yield return new WaitForSeconds(0.3f);
        EndTurn();
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
