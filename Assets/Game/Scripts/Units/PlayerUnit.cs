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
    }

    // ============================================================
    //                     回合生命周期
    // ============================================================

    public override void OnTurnStart()
    {
        // 只做 buff 结算和护盾清空，不再回满资源——
        // 资源恢复统一放在"回合结束"（文档：每回合结束恢复 15 点）
        base.OnTurnStart();
        hasActedThisTurn = false;
    }

    public override void OnTurnEnd()
    {
        base.OnTurnEnd();

        // 回合结束：恢复思绪/耐力，但不超过上限（文档：每回合结束恢复 15 点）
        currentThought = Mathf.Min(maxThought, currentThought + thoughtRegenPerTurnEnd);
        currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerTurnEnd);
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
    //                     移动（玩家操作）
    // ============================================================

    /// <summary>
    /// 玩家尝试移动到目标格
    /// </summary>
    public bool TryMoveTo(HexCoord target)
    {
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
        int moveCost = moveDistance;        // 简化：每格消耗 1 耐力

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
    /// 使用第 index 瓶药水。文档：只能在玩家的行动阶段使用。
    /// 只能对自己使用（target 传 null 即自己）。
    /// </summary>
    public bool UsePotion(int index, Unit target = null)
    {
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
        potions.RemoveAt(index);

        // 药水效果：默认作用在自己身上
        potion.Resolve(target != null ? target : this);
        EventManager.Trigger("PotionChanged", this);
        return true;
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
    //                       死亡重写
    // ============================================================

    protected override void Die()
    {
        base.Die();   // 先执行基类死亡逻辑

        // 玩家死亡额外逻辑
        if (GameManager.Instance != null)
        {
            // 检查是否所有玩家都死了
            var allPlayers = FindObjectsOfType<PlayerUnit>();
            bool allDead = true;
            foreach (var p in allPlayers)
            {
                if (p != this && p.GetCurrentHealth() > 0)
                {
                    allDead = false;
                    break;
                }
            }

            if (allDead)
            {
                GameManager.Instance.OnPlayerDefeat();
            }
        }
    }
}
