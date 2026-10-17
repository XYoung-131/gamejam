using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 玩家单位——继承 Unit，玩家可以操控
/// 挂在玩家单位的 GameObject 上
/// </summary>
public class PlayerUnit : Unit
{
    [Header("玩家特有")]
    [Tooltip("思绪上限（文档：耐力和思绪上限都是 40）")]
    public int maxThought = 40;
    [Tooltip("耐力上限（耐力 = 行动资源，移动和出牌共用）")]
    public int maxStamina = 40;
    [Tooltip("战斗开始时的思绪（文档：战斗开始时有 20 点）")]
    public int startThought = 20;
    [Tooltip("战斗开始时的耐力（文档：战斗开始时有 20 点）")]
    public int startStamina = 20;
    [Tooltip("每回合结束时恢复的思绪（文档：每回合结束恢复 15 点）")]
    public int thoughtRegenPerTurnEnd = 15;
    [Tooltip("每回合结束时恢复的耐力（文档：每回合结束恢复 15 点）")]
    public int staminaRegenPerTurnEnd = 15;

    // 运行时状态
    protected int currentThought;
    protected int currentStamina;
    protected bool hasActedThisTurn = false;

    /// <summary>
    /// 当前思绪
    /// </summary>
    public int CurrentThought => currentThought;

    /// <summary>
    /// 当前耐力（即行动资源：移动、出牌都从这里扣）
    /// </summary>
    public int CurrentStamina => currentStamina;

    /// <summary>
    /// 是否被选中
    /// </summary>
    public bool isSelected = false;

    // ============================================================
    //                       初始化
    // ============================================================

    public override void Initialize(UnitData unitData, HexCoord startCoord)
    {
        base.Initialize(unitData, startCoord);
        // 战斗开始时按配置给初始资源（文档：开局 20 点）
        currentThought = startThought;
        currentStamina = startStamina;
        hasActedThisTurn = false;

        // 新的战斗，清空"每瓶饮料喝了几次"的计数
        // （否则上一场的第 3 瓶会带到这一场，一喝就触发副作用）
        potionUseCount.Clear();
    }

    // ============================================================
    //                     回合生命周期
    // ============================================================

    public override void OnTurnStart()
    {
        if (IsDead) return;   // 已死亡：不再参与回合结算

        // 只做 buff 结算和护盾清空，不再回满资源——
        // 资源恢复统一放在"回合结束"（文档：每回合结束恢复 15 点）
        base.OnTurnStart();
        hasActedThisTurn = false;
    }

    public override void OnTurnEnd()
    {
        if (IsDead) return;   // 已死亡：不再参与回合结算（含资源恢复）

        base.OnTurnEnd();

        // 回合结束恢复思绪/耐力，但不超过上限（文档：每回合结束恢复 15 点）。
        // 「力竭」耐力回复减半 0.5 /「混乱」思绪回复减半 0.5 在这里生效。
        // 查询走不限 trigger 的重载：没中这两个 debuff 时返回 1（不变），
        // 倍率型叠加规则见 Unit.GetTotalEffectValue（力竭 2 层 = 1 + (-0.5)×2 = 0，完全不回）。
        float staminaRate = GetTotalEffectValue(BuffEffectType.耐力回复倍率);
        float thoughtRate = GetTotalEffectValue(BuffEffectType.思绪回复倍率);

        int staminaGain = Mathf.FloorToInt(staminaRegenPerTurnEnd * staminaRate);
        int thoughtGain = Mathf.FloorToInt(thoughtRegenPerTurnEnd * thoughtRate);

        currentStamina = Mathf.Min(maxStamina, currentStamina + staminaGain);
        currentThought = Mathf.Min(maxThought, currentThought + thoughtGain);
        EventManager.Trigger("PlayerResourceChanged", this);

        isSelected = false;
    }

    // ============================================================
    //                  思绪 / 耐力消耗（卡牌用）
    // ============================================================

    /// <summary>
    /// 尝试消耗思绪，不足返回 false（灌注、抽卡用）
    /// </summary>
    public bool TrySpendThought(int amount)
    {
        if (amount < 0) return false;
        if (currentThought < amount)
        {
            Debug.Log($"思绪不足：需要 {amount}，当前 {currentThought}");
            return false;
        }

        currentThought -= amount;
        EventManager.Trigger("PlayerResourceChanged", this);
        return true;
    }

    /// <summary>
    /// 尝试消耗耐力，不足返回 false
    /// </summary>
    public bool TrySpendStamina(int amount)
    {
        if (amount < 0) return false;
        if (currentStamina < amount)
        {
            Debug.Log($"耐力不足：需要 {amount}，当前 {currentStamina}");
            return false;
        }

        currentStamina -= amount;
        EventManager.Trigger("PlayerResourceChanged", this);
        return true;
    }

    /// <summary>
    /// 同时消耗思绪 + 耐力（出牌用），任一不足则都不扣
    /// </summary>
    public bool TrySpendResource(int thoughtAmount, int staminaAmount)
    {
        if (thoughtAmount < 0 || staminaAmount < 0) return false;
        if (currentThought < thoughtAmount || currentStamina < staminaAmount)
        {
            Debug.Log($"资源不足：需要思绪 {thoughtAmount}/耐力 {staminaAmount}，" +
                      $"当前思绪 {currentThought}/耐力 {currentStamina}");
            return false;
        }

        currentThought -= thoughtAmount;
        currentStamina -= staminaAmount;
        EventManager.Trigger("PlayerResourceChanged", this);
        return true;
    }

    // ============================================================
    //              思绪 / 耐力 回复（药水、遗物用）
    // ============================================================

    /// <summary>
    /// 回复耐力，不会超过上限（"耐力饮料"等）
    /// </summary>
    public void RestoreStamina(int amount)
    {
        if (amount <= 0) return;

        currentStamina = Mathf.Min(currentStamina + amount, maxStamina);
        EventManager.Trigger("PlayerResourceChanged", this);
    }

    /// <summary>
    /// 回复思绪，不会超过上限（"提神饮料"等）
    /// </summary>
    public void RestoreThought(int amount)
    {
        if (amount <= 0) return;

        currentThought = Mathf.Min(currentThought + amount, maxThought);
        EventManager.Trigger("PlayerResourceChanged", this);
    }

    /// <summary>
    /// 回复全部耐力和思绪（"精力充沛"遗物）
    /// </summary>
    public void RestoreAllResources()
    {
        currentStamina = maxStamina;
        currentThought = maxThought;
        EventManager.Trigger("PlayerResourceChanged", this);
    }

    /// <summary>
    /// 扣光全部耐力和思绪（"劣质混合饮料"第 3 瓶的副作用）
    /// </summary>
    public void DrainAllResources()
    {
        currentStamina = 0;
        currentThought = 0;
        EventManager.Trigger("PlayerResourceChanged", this);
    }

    // ============================================================
    //                     移动（玩家操作）
    // ============================================================

    /// <summary>
    /// 玩家尝试移动到目标格
    /// </summary>
    public bool TryMoveTo(HexCoord target)
    {
        if (IsDead) return false;   // 已死亡：不能移动

        if (GameManager.Instance != null && !GameManager.Instance.IsInBattle)
            return false;

        if (GameManager.Instance != null && !GameManager.Instance.isPlayerTurn)
            return false;

        if (currentStamina <= 0)
        {
            Debug.Log("耐力不足");
            return false;
        }

        // 先算路径
        var path = HexPathfinding.FindPath(currentCoord, target);
        if (path == null || path.Count <= 1)
        {
            Debug.Log("无法到达目标");
            return false;
        }

        // 移动距离不能超过耐力（简化：1 耐力 = 走 1 格）
        int moveDistance = path.Count - 1;  // path 包含起点

        // 「沉重」：移动技能的耐力消耗增加 25%（倍率 1.25）。倍率型：没中时返回 1（不变），
        // 消耗向上取整（走 3 格 × 1.25 = 3.75 → 4 点）。「沉重」还写了"不叠加"，做 asset 时取消 canStack。
        float moveCostRate = GetTotalEffectValue(BuffEffectType.移动耐力消耗倍率);
        int moveCost = Mathf.CeilToInt(moveDistance * moveCostRate);

        // 考虑移动距离加成 buff
        int moveBonus = GetMoveBonus();
        int maxMove = currentStamina + moveBonus;

        if (moveCost > maxMove)
        {
            Debug.Log($"移动距离不够，需要 {moveCost} 耐力，当前 {currentStamina}（加成 {moveBonus}）");
            return false;
        }

        // 扣耐力
        currentStamina = Mathf.Max(0, currentStamina - moveCost);
        EventManager.Trigger("PlayerResourceChanged", this);

        // 开始移动（先简化为瞬移，以后加动画）
        MoveAlongPath(path);
        hasActedThisTurn = true;

        return true;
    }

    /// <summary>
    /// 沿路径移动（简化版：瞬移，以后加协程动画）
    /// </summary>
    protected void MoveAlongPath(List<HexCoord> path)
    {
        if (path == null || path.Count <= 1) return;

        HexCoord endCoord = path[path.Count - 1];

        // 离开旧格子
        HexCell oldCell = HexGrid.Instance.GetCell(currentCoord);
        if (oldCell != null)
            oldCell.occupyUnit = null;

        // 站上新格子
        currentCoord = endCoord;
        HexCell newCell = HexGrid.Instance.GetCell(endCoord);
        if (newCell != null)
            newCell.occupyUnit = this;

        // 更新位置
        transform.position = HexGrid.Instance.HexToWorld(endCoord);

        OnMove();
        EventManager.Trigger("PlayerUnitMoved", this);
    }

    // ============================================================
    //                     攻击（玩家操作）
    // ============================================================

    /// <summary>
    /// 玩家尝试攻击目标单位
    /// </summary>
    public bool TryAttack(Unit target)
    {
        if (IsDead || target == null) return false;   // 已死亡：不能攻击
        if (target.IsDead) return false;              // 目标已死：不再打尸体

        if (GameManager.Instance != null && !GameManager.Instance.isPlayerTurn)
            return false;

        if (currentStamina <= 0)
        {
            Debug.Log("耐力不足");
            return false;
        }

        // 检查距离（简化：近战 = 相邻，以后用卡牌系统）
        int distance = currentCoord.Distance(target.GetCoord());
        if (distance != 1)
        {
            Debug.Log("距离太远，无法攻击");
            return false;
        }

        if (!CanAttack())
        {
            Debug.Log("无法攻击（无力等状态）");
            return false;
        }

        // 扣耐力
        currentStamina--;
        EventManager.Trigger("PlayerResourceChanged", this);

        // 造成伤害
        int damage = GetAttack();
        OnAttack();
        target.TakeDamage(damage);

        hasActedThisTurn = true;
        EventManager.Trigger("PlayerAttacked", this, target);

        return true;
    }

    // ============================================================
    //                       选中状态
    // ============================================================

    /// <summary>
    /// 选中这个单位
    /// </summary>
    public void Select()
    {
        isSelected = true;
        HighlightMoveRange();
        EventManager.Trigger("UnitSelected", this);
    }

    /// <summary>
    /// 取消选中
    /// </summary>
    public void Deselect()
    {
        isSelected = false;
        HexGrid.Instance?.ClearAllHighlights();
        EventManager.Trigger("UnitDeselected", this);
    }

    /// <summary>
    /// 高亮可移动范围
    /// </summary>
    protected void HighlightMoveRange()
    {
        if (HexGrid.Instance == null) return;

        int moveRange = currentStamina + GetMoveBonus();
        var reachable = HexPathfinding.GetReachableCells(currentCoord, moveRange);

        foreach (var coord in reachable)
        {
            HexCell cell = HexGrid.Instance.GetCell(coord);
            if (cell != null && cell.isWalkable && cell.occupyUnit == null)
                cell.SetMoveRangeHighlight(true);   // 文档：可到达格子变绿
        }
    }

    // ============================================================
    //                    药水 / 遗物
    // ============================================================

    [Header("药水与遗物")]
    [Tooltip("药水持有上限（文档：上限 3 个）")]
    public int maxPotions = 3;

    // 持有的药水（按顺序排列）
    protected readonly List<PotionData> potions = new List<PotionData>();
    // 已获得的遗物（永久生效的 buff 已经挂到身上，这里只做记录）
    protected readonly List<RelicData> relics = new List<RelicData>();

    // 本场战斗中"每瓶饮料各自被喝了几次"。
    // 存在的意义：PotionData 是 ScriptableObject 资产，全场景共享一份，
    // 没法在它身上存运行时状态，所以计数放在玩家身上。
    // 用途：① "劣质混合饮料"靠它判断是不是第 3 瓶（触发副作用）；
    //       ② maxUsesPerBattle 的次数限制靠它判断。
    // 战斗开始时（Initialize）清空，跨战斗不会串味。
    protected readonly Dictionary<PotionData, int> potionUseCount = new Dictionary<PotionData, int>();

    /// <summary>当前持有的药水（UI 只读）</summary>
    public IReadOnlyList<PotionData> Potions => potions;

    /// <summary>已获得的遗物（UI 只读）</summary>
    public IReadOnlyList<RelicData> Relics => relics;

    /// <summary>
    /// 获得一瓶药水。背包已满则拿不到（返回 false）。
    /// </summary>
    public bool AddPotion(PotionData potion)
    {
        if (potion == null || potions.Count >= maxPotions) return false;

        potions.Add(potion);
        EventManager.Trigger("PotionChanged", this);
        return true;
    }

    /// <summary>
    /// 使用第 index 瓶药水。文档：只能在玩家的行动阶段使用，只能对自己使用。
    ///
    /// 这里负责三件事（药水效果本身交给 PotionData.Resolve）：
    /// ① 阶段 / 序号 / 死亡校验；
    /// ② 每场战斗的使用次数限制（maxUsesPerBattle）——超了就不让喝，
    ///    免得出现"道具被吃掉但没有任何效果"；
    /// ③ 维护"本场战斗这瓶饮料喝了几次"的计数，并传给 Resolve。
    ///    这是"劣质混合饮料"第 3 瓶触发副作用的判定依据。
    /// </summary>
    public bool UsePotion(int index, Unit target = null)
    {
        if (IsDead) return false;   // 已死亡：不能用道具

        if (index < 0 || index >= potions.Count)
        {
            Debug.Log("药水序号无效");
            return false;
        }

        if (!CanActNow())
        {
            Debug.Log("不是玩家行动阶段，不能使用药水");
            return false;
        }

        PotionData potion = potions[index];

        // 本场已经喝过几瓶（含这一次）
        int usedBefore = GetPotionUseCount(potion);
        int usedCount = usedBefore + 1;

        // 次数上限校验：放在"把道具从背包里拿掉"之前，
        // 否则会被白白吃掉一瓶却不生效。
        // 约定：maxUsesPerBattle 0 或负数 = 不限次数；正数 N = 每场最多喝 N 瓶
        if (potion.maxUsesPerBattle > 0 && usedCount > potion.maxUsesPerBattle)
        {
            Debug.Log($"【{potion.potionName}】本场战斗最多喝 {potion.maxUsesPerBattle} 瓶，已经喝过了");
            return false;
        }

        // 真正消耗道具：先记数，再移除
        potionUseCount[potion] = usedCount;
        potions.RemoveAt(index);

        // 药水效果：默认作用在自己身上。
        // 传 usedCount 进去——"劣质混合饮料"靠它判断是不是第 3 瓶。
        potion.Resolve(target != null ? target : this, usedCount);
        EventManager.Trigger("PotionChanged", this);
        return true;
    }

    /// <summary>
    /// 本场战斗这瓶饮料已经喝了几次（没喝过返回 0）。
    /// UI 可以用它显示"已用 N/M"。
    /// </summary>
    public int GetPotionUseCount(PotionData potion)
    {
        if (potion == null) return 0;
        return potionUseCount.TryGetValue(potion, out int count) ? count : 0;
    }

    /// <summary>
    /// 获得遗物：记录进列表，并立刻应用它的效果（永久 buff / 即时效果）。
    /// </summary>
    public bool AddRelic(RelicData relic)
    {
        // 已经持有过同一件遗物就直接跳过（掉落去重）
        if (relic == null || HasRelic(relic)) return false;

        relics.Add(relic);
        relic.Apply(this);
        EventManager.Trigger("RelicChanged", this);
        return true;
    }

    /// <summary>
    /// 是否已经持有这件遗物（掉落池去重、UI 显示已拥有用）。
    /// </summary>
    public bool HasRelic(RelicData relic)
    {
        return relic != null && relics.Contains(relic);
    }

    /// <summary>
    /// 现在能不能进行主动操作（用药水等）。
    /// 优先看 TurnManager 的精确阶段；没有 TurnManager 时退回 GameManager。
    /// </summary>
    protected bool CanActNow()
    {
        if (TurnManager.Instance != null)
            return TurnManager.Instance.Phase == TurnPhase.玩家行动;

        return GameManager.Instance != null &&
               GameManager.Instance.IsInBattle &&
               GameManager.Instance.isPlayerTurn;
    }

    // ============================================================
    //                       死亡扩展
    // ============================================================

    /// <summary>
    /// 玩家死亡额外逻辑。
    /// 走基类的 OnDeath() 扩展点而不是覆写 Die()——基类保证这里
    /// 只会在"首次死亡"时被调用一次，不会重复判"全员阵亡"。
    /// </summary>
    protected override void OnDeath()
    {
        if (GameManager.Instance == null) return;

        // 检查是否所有玩家都死了
        var allPlayers = FindObjectsOfType<PlayerUnit>();
        bool allDead = true;
        foreach (var p in allPlayers)
        {
            // 判 IsDead 而不是只看血量：Destroy 延迟到帧末，同帧内刚死的队友对象还在
            if (p != this && !p.IsDead && p.GetCurrentHealth() > 0)
            {
                allDead = false;
                break;
            }
        }

        if (allDead)
            GameManager.Instance.OnPlayerDefeat();
    }
}
