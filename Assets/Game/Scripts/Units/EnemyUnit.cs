using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 敌人单位——继承 Unit，由 AI 控制
/// 挂在敌人单位的 GameObject 上
/// </summary>
public class EnemyUnit : Unit
{
    [Header("AI 设置")]
    [Tooltip("AI 思考间隔（秒）")]
    public float thinkDelay = 0.8f;
    [Tooltip("每步移动的间隔（秒）")]
    public float stepDelay = 0.3f;

    // 当前使用哪个技能组
    protected int currentSkillGroup = 1;
    protected bool hasActedThisTurn = false;

    /// <summary>
    /// 是否已经行动过
    /// </summary>
    public bool HasActed => hasActedThisTurn;

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
