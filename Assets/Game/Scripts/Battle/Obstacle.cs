using UnityEngine;

/// <summary>
/// 障碍物基类——地图上的环境物体（柱子、炸药桶、抑制器等）
/// 挂在障碍物 GameObject 上，和 Unit 类似但没有主动行为
/// </summary>
public class Obstacle : MonoBehaviour
{
    [Header("配置数据")]
    [Tooltip("引用 ScriptableObjects/Terrain/ 下的障碍物数据资产")]
    public ObstacleData data;

    [Header("运行时状态")]
    [SerializeField] protected int currentHealth;     // 当前血量
    [SerializeField] protected HexCoord currentCoord; // 当前所在六边形坐标

    // 是否已经被摧毁（防止同一帧内被多次打爆导致重复结算）
    protected bool isDestroyed = false;
    private int completedRoundCount;
    private bool eventsRegistered;

    /// <summary>
    /// 初始化障碍物
    /// </summary>
    public virtual void Initialize(ObstacleData obstacleData, HexCoord coord)
    {
        data = obstacleData;
        currentHealth = obstacleData.maxHealth;
        currentCoord = coord;
        isDestroyed = false;
        completedRoundCount = 0;

        // 把自己注册到格子上
        if (HexGrid.Instance != null)
        {
            HexCell cell = HexGrid.Instance.GetCell(coord);
            if (cell != null)
                cell.obstacle = this;
        }

        RegisterEvents();
    }

    protected virtual void OnDestroy()
    {
        UnregisterEvents();
    }

    private void RegisterEvents()
    {
        if (eventsRegistered) return;
        EventManager.AddListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.AddListener("EnemyBuffsSettled", HandleRoundCompleted);
        eventsRegistered = true;
    }

    private void UnregisterEvents()
    {
        if (!eventsRegistered) return;
        EventManager.RemoveListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.RemoveListener("EnemyBuffsSettled", HandleRoundCompleted);
        eventsRegistered = false;
    }

    /// <summary>
    /// 受到伤害
    /// </summary>
    public virtual void TakeDamage(int amount)
    {
        if (isDestroyed) return;

        // 无限血量的障碍物（如柱子）不掉血
        if (data.maxHealth <= -1)
            return;

        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            OnDestroyed();
        }
    }

    /// <summary>
    /// 强制摧毁：无视"无限血量"设定直接摧毁（垃圾毁灭者用：一击摧毁场上所有障碍物）。
    /// </summary>
    public virtual void ForceDestroy()
    {
        if (isDestroyed) return;

        currentHealth = 0;
        OnDestroyed();
    }

    /// <summary>
    /// 被摧毁时触发特殊效果
    /// 子类重写实现不同效果（爆炸、生成鼠鼠等）
    /// </summary>
    protected virtual void OnDestroyed()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        UnregisterEvents();

        // 先结算爆炸（炸药桶等），此时障碍物还在原格子上、周围单位还没被移走
        ApplyExplosion();

        // 从格子上移除
        if (HexGrid.Instance != null)
        {
            HexCell cell = HexGrid.Instance.GetCell(currentCoord);
            if (cell != null)
                cell.obstacle = null;
        }

        // 触发事件
        EventManager.Trigger("ObstacleDestroyed", this);

        // 销毁物体
        Destroy(gameObject);
    }

    /// <summary>
    /// 被摧毁时的爆炸：对范围内所有单位造成一次爆炸伤害。
    /// 伤害上下文里 isExplosion = true，供「柔顺布料：免疫爆炸」等遗物判定。
    /// </summary>
    protected virtual void ApplyExplosion()
    {
        if (data == null || !data.isExplosion) return;
        if (data.explosionDamage <= 0 &&
            data.explosionDamageToPlayer <= 0 &&
            data.explosionDamageToEnemies <= 0) return;
        if (HexGrid.Instance == null) return;

        int radius = Mathf.Max(1, data.explosionRadius);

        foreach (var cell in HexGrid.Instance.GetCellsInRange(currentCoord, radius))
        {
            if (cell == null || cell.occupyUnit == null) continue;

            Unit unit = cell.occupyUnit;
            if (unit.GetCurrentHealth() <= 0) continue;

            int damage = unit is PlayerUnit
                ? (data.explosionDamageToPlayer >= 0 ? data.explosionDamageToPlayer : data.explosionDamage)
                : (data.explosionDamageToEnemies >= 0 ? data.explosionDamageToEnemies : data.explosionDamage);
            if (damage <= 0) continue;

            DamageContext ctx = new DamageContext(damage, data.explosionDamageType);
            ctx.isExplosion = true;
            unit.TakeDamage(ctx);
        }

        Debug.Log($"【{data.obstacleName}】爆炸：玩家伤害 " +
                  $"{(data.explosionDamageToPlayer >= 0 ? data.explosionDamageToPlayer : data.explosionDamage)}，" +
                  $"敌人伤害 {(data.explosionDamageToEnemies >= 0 ? data.explosionDamageToEnemies : data.explosionDamage)}，" +
                  $"范围 {radius} 格");
    }

    /// <summary>
    /// 是否已被摧毁
    /// </summary>
    public bool IsDestroyed => isDestroyed;

    /// <summary>
    /// 获取当前坐标
    /// </summary>
    public HexCoord GetCoord() => currentCoord;

    /// <summary>
    /// 获取当前血量
    /// </summary>
    public int GetCurrentHealth() => currentHealth;

    /// <summary>单位被位移撞到该障碍物时，撞击者额外承受的伤害。</summary>
    public int GetCollisionDamageBonus()
    {
        return data != null ? Mathf.Max(0, data.collisionDamageBonus) : 0;
    }

    private void HandlePlayerTurnStarted(int turnCount)
    {
        if (isDestroyed || data == null || data.areaBuff == null || data.areaBuffRadius <= 0)
            return;

        ApplyAreaBuff();
    }

    private void ApplyAreaBuff()
    {
        if (HexGrid.Instance == null) return;

        int affected = 0;
        foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(currentCoord, data.areaBuffRadius))
        {
            Unit unit = cell != null ? cell.occupyUnit : null;
            if (unit == null || unit.IsDead) continue;
            unit.ApplyBuff(data.areaBuff, Mathf.Max(1, data.areaBuffStacks));
            affected++;
        }

        Debug.Log($"【{data.obstacleName}】向 {data.areaBuffRadius} 格内 {affected} 个单位施加 {data.areaBuff.buffName}");
    }

    private void HandleRoundCompleted()
    {
        if (isDestroyed || data == null || data.triggerEveryRounds <= 0) return;

        completedRoundCount++;
        if (completedRoundCount % data.triggerEveryRounds != 0) return;

        if (data.periodicSpawnUnit != null)
            TrySpawnInRandomAdjacentCell(data.periodicSpawnUnit);

        if (data.timedDestroySpawnUnit != null)
        {
            UnitData unitToSpawn = data.timedDestroySpawnUnit;
            HexCoord spawnCoord = currentCoord;
            OnDestroyed();
            SpawnEnemy(unitToSpawn, spawnCoord);
        }
    }

    private void TrySpawnInRandomAdjacentCell(UnitData unitData)
    {
        if (HexGrid.Instance == null || unitData == null || unitData.prefab == null) return;

        var candidates = new System.Collections.Generic.List<HexCell>();
        foreach (HexCell cell in HexGrid.Instance.GetNeighborCells(currentCoord))
            if (cell != null && cell.CanStopOn()) candidates.Add(cell);

        if (candidates.Count == 0)
        {
            Debug.Log($"【{data.obstacleName}】周围没有空格，无法生成 {unitData.unitName}");
            return;
        }

        HexCell destination = candidates[Random.Range(0, candidates.Count)];
        if (!SpawnEnemy(unitData, destination.coord)) return;

        if (data.healthCostAfterSpawn > 0)
            TakeDamage(data.healthCostAfterSpawn);
    }

    private static bool SpawnEnemy(UnitData unitData, HexCoord coord)
    {
        if (unitData == null || unitData.prefab == null || HexGrid.Instance == null) return false;
        HexCell cell = HexGrid.Instance.GetCell(coord);
        if (cell == null || !cell.CanStopOn()) return false;

        GameObject go = Instantiate(unitData.prefab, HexGrid.Instance.HexToWorld(coord), Quaternion.identity);
        go.name = "Enemy_" + unitData.unitName + "_" + coord;
        EnemyUnit enemy = go.GetComponent<EnemyUnit>();
        if (enemy == null) enemy = go.AddComponent<EnemyUnit>();
        enemy.Initialize(unitData, coord);

        LevelManager manager = FindObjectOfType<LevelManager>();
        if (manager != null) manager.RegisterSummonedEnemy(enemy);
        return true;
    }

    /// <summary>
    /// 是否阻挡移动（默认阻挡）
    /// </summary>
    public virtual bool BlocksMovement()
    {
        return true;
    }

    /// <summary>
    /// 是否阻挡视线（默认"高大"障碍物阻挡）
    /// </summary>
    public virtual bool BlocksSight()
    {
        // 如果有"高大"标签就阻挡视线
        return data != null && data.tags.Contains(ObstacleTag.高大);
    }
}
