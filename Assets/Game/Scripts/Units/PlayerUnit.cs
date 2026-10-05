using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 玩家单位——继承 Unit，玩家可以操控
/// 挂在玩家单位的 GameObject 上
/// </summary>
public class PlayerUnit : Unit
{
    [Header("玩家特有")]
    [Tooltip("每回合思绪上限")]
    public int maxThought = 20;
    [Tooltip("每回合行动点上限")]
    public int maxAction = 3;
    [Tooltip("每回合抽卡数")]
    public int drawPerTurn = 5;

    // 运行时状态
    protected int currentThought;
    protected int currentAction;
    protected bool hasActedThisTurn = false;

    /// <summary>
    /// 当前思绪
    /// </summary>
    public int CurrentThought => currentThought;

    /// <summary>
    /// 当前行动点
    /// </summary>
    public int CurrentAction => currentAction;

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
        currentThought = maxThought;
        currentAction = maxAction;
        hasActedThisTurn = false;
    }

    // ============================================================
    //                     回合生命周期
    // ============================================================

    public override void OnTurnStart()
    {
        base.OnTurnStart();  // 先执行基类的 buff 结算

        // 回合开始：补满思绪和行动点
        currentThought = maxThought;
        currentAction = maxAction;
        hasActedThisTurn = false;
    }

    public override void OnTurnEnd()
    {
        base.OnTurnEnd();
        isSelected = false;
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

        if (currentAction <= 0)
        {
            Debug.Log("行动点不足");
            return false;
        }

        // 先算路径
        var path = HexPathfinding.FindPath(currentCoord, target);
        if (path == null || path.Count <= 1)
        {
            Debug.Log("无法到达目标");
            return false;
        }

        // 移动距离不能超过行动点（简化：1 行动点 = 走 1 格）
        int moveDistance = path.Count - 1;  // path 包含起点
        int moveCost = moveDistance;        // 简化：每格消耗 1 行动点

        // 考虑移动距离加成 buff
        int moveBonus = GetMoveBonus();
        int maxMove = currentAction + moveBonus;

        if (moveCost > maxMove)
        {
            Debug.Log($"移动距离不够，需要 {moveCost} 行动点，当前 {currentAction}（加成 {moveBonus}）");
            return false;
        }

        // 扣行动点
        currentAction = Mathf.Max(0, currentAction - moveCost);

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

        if (currentAction <= 0)
        {
            Debug.Log("行动点不足");
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

        // 扣行动点
        currentAction--;

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

        int moveRange = currentAction + GetMoveBonus();
        var reachable = HexPathfinding.GetReachableCells(currentCoord, moveRange);

        foreach (var coord in reachable)
        {
            HexCell cell = HexGrid.Instance.GetCell(coord);
            if (cell != null && cell.isWalkable && cell.occupyUnit == null)
                cell.SetHighlight(true);
        }
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
