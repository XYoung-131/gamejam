using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 关卡生成器——按 LevelData 里的布阵，把玩家、敌人、障碍物摆到棋盘上。
///
/// 挂载位置：场景里任意一个常驻 GameObject（全场景只能有一个）。
///
/// 执行顺序（很重要）：
///   Awake：先关掉 GameManager 的自动开战，避免它抢在布阵之前抛"玩家回合开始"事件；
///   Start：① 生成格子 → ② 放玩家/敌人/障碍 → ③ 才调用 StartBattle()。
/// 因为所有单位都是"格子存在之后再 Initialize"的，坐标和占格才不会错。
/// </summary>
public class LevelManager : MonoBehaviour
{
    [Header("关卡")]
    [Tooltip("要加载的关卡数据（ScriptableObjects/关卡/ 下的 .asset）")]
    public LevelData level;

    [Tooltip("用第几个变种：0 = 变种1，1 = 变种2")]
    public int variantIndex = 0;

    [Header("玩家")]
    public UnitData playerData;

    [Tooltip("攻击牌库（拖入 ScriptableObjects/技能/攻击技能 下的卡）")]
    public List<CardData> attackPool = new List<CardData>();

    [Tooltip("防御牌库（拖入 ScriptableObjects/技能/防御技能 下的卡）")]
    public List<CardData> defensePool = new List<CardData>();

    [Header("流程")]
    [Tooltip("布阵完成后自动开始战斗（关掉的话可以手动调 StartBattle 测试）")]
    public bool startBattleAfterSpawn = true;

    [Tooltip("给生成出来的玩家/敌人自动补上立牌翻转特效（预制体上已经有的不会重复加）")]
    public bool addFlipEffect = true;

    [Tooltip("给生成出来的玩家/敌人自动补上 buff 套环特效（预制体上已经有的不会重复加）")]
    public bool addBuffRing = true;

    /// <summary>当前场上的玩家（外部只读）</summary>
    public PlayerUnit Player { get; private set; }

    private readonly List<EnemyUnit> enemies = new List<EnemyUnit>();

    public void RegisterSummonedEnemy(EnemyUnit enemy)
    {
        if (enemy != null && !enemies.Contains(enemy)) enemies.Add(enemy);
    }
    private readonly List<Obstacle> obstacles = new List<Obstacle>();

    // ============================================================
    //                       初始化
    // ============================================================

    private void Awake()
    {
        // 布阵在 Start 里做，所以先让 GameManager 别自动开战
        GameManager manager = GameManager.Instance != null
            ? GameManager.Instance
            : FindObjectOfType<GameManager>();
        if (manager != null)
            manager.autoStartBattle = false;
    }

    private void Start()
    {
        HexGrid grid = HexGrid.Instance != null ? HexGrid.Instance : FindObjectOfType<HexGrid>();
        if (grid == null)
        {
            Debug.LogError("[LevelManager] 场景里找不到 HexGrid，无法布阵");
            return;
        }

        if (level == null || level.variants == null || level.variants.Count == 0)
        {
            Debug.LogError("[LevelManager] 没有指定关卡数据（Level）");
            return;
        }

        // ① 先生成格子——单位的 Initialize 要往格子上登记，必须有格子
        grid.GenerateGrid();

        // ② 布阵
        LevelData.Variant variant = level.variants[Mathf.Clamp(variantIndex, 0, level.variants.Count - 1)];
        SpawnPlayer(variant.playerStart);
        foreach (LevelData.Spawn spawn in variant.spawns)
            SpawnOne(spawn);

        // ②.5 铺特殊地形（灼热/剧毒/刀刃/烟雾/吸血/绒毯，由 TerrainSystem 在回合节点结算）
        foreach (LevelData.TerrainSpawn t in variant.terrains)
        {
            if (t == null || t.terrain == null) continue;
            HexCell cell = HexGrid.Instance.GetCell(t.coord);
            if (cell == null)
            {
                Debug.LogWarning($"[LevelManager] 地形 {t.terrain.name} 的格子 {t.coord} 不在地图上，跳过");
                continue;
            }
            cell.terrain = t.terrain;
        }

        Debug.Log($"[LevelManager] {level.levelName} / {variant.variantName} 布阵完成：" +
                  $"玩家 {Player?.GetCurrentHealth() ?? 0} 血，敌人 {enemies.Count} 个，障碍物 {obstacles.Count} 个");

        // ③ 一切就位后才开战
        if (startBattleAfterSpawn && GameManager.Instance != null)
            GameManager.Instance.StartBattle();
    }

    // ============================================================
    //                       生成棋子
    // ============================================================

    private void SpawnPlayer(HexCoord coord)
    {
        if (playerData == null || playerData.prefab == null)
        {
            Debug.LogError("[LevelManager] 玩家数据或玩家预制体没配，玩家没生成");
            return;
        }

        Vector3 position = HexGrid.Instance.HexToWorld(coord);
        GameObject go = Instantiate(playerData.prefab, position, Quaternion.identity);
        go.name = "Player";

        Player = go.GetComponent<PlayerUnit>();
        if (Player == null)
        {
            Debug.LogError("[LevelManager] 玩家预制体上没有 PlayerUnit 组件");
            return;
        }

        Player.Initialize(playerData, coord);

        // 玩家预制体默认没有卡牌系统，这里补上并填好牌库
        PlayerCardSystem cards = go.GetComponent<PlayerCardSystem>();
        if (cards == null)
            cards = go.AddComponent<PlayerCardSystem>();
        if (attackPool.Count > 0) cards.attackPool = new List<CardData>(attackPool);
        if (defensePool.Count > 0) cards.defensePool = new List<CardData>(defensePool);
        cards.initialCards = CardPoolRules.MergeInitialCards(cards.initialCards, cards.defensePool);

        // 遗物条件触发总调度（RelicData.Trigger 的唯一驱动者，必须和 PlayerUnit 同物体）
        if (go.GetComponent<RelicController>() == null)
            go.AddComponent<RelicController>();

        EnsureFlipEffect(go);
        EnsureBuffRing(go);
    }

    private void SpawnOne(LevelData.Spawn spawn)
    {
        if (spawn == null)
            return;

        if (HexGrid.Instance.GetCell(spawn.coord) == null)
        {
            Debug.LogWarning($"[LevelManager] 格子 {spawn.coord} 不在这张地图上，跳过这个棋子");
            return;
        }

        Vector3 position = HexGrid.Instance.HexToWorld(spawn.coord);

        if (spawn.unit != null)
        {
            if (spawn.unit.prefab == null)
            {
                Debug.LogWarning($"[LevelManager] {spawn.unit.unitName} 的预制体没配，跳过");
                return;
            }

            GameObject go = Instantiate(spawn.unit.prefab, position, Quaternion.identity);
            go.name = "Enemy_" + spawn.unit.unitName + "_" + spawn.coord;

            EnemyUnit enemy = go.GetComponent<EnemyUnit>();
            if (enemy == null)
                enemy = go.AddComponent<EnemyUnit>();

            enemy.Initialize(spawn.unit, spawn.coord);
            enemies.Add(enemy);
            EnsureFlipEffect(go);
            EnsureBuffRing(go);
            return;
        }

        if (spawn.obstacle != null)
        {
            if (spawn.obstacle.prefab == null)
            {
                Debug.LogWarning($"[LevelManager] {spawn.obstacle.obstacleName} 的预制体没配，跳过");
                return;
            }

            GameObject go = Instantiate(spawn.obstacle.prefab, position, Quaternion.identity);
            go.name = "Obstacle_" + spawn.obstacle.obstacleName + "_" + spawn.coord;

            Obstacle obstacle = go.GetComponent<Obstacle>();
            if (obstacle == null)
                obstacle = go.AddComponent<Obstacle>();

            obstacle.Initialize(spawn.obstacle, spawn.coord);
            obstacles.Add(obstacle);
        }
    }

    // ============================================================
    //                     Inspector 右键测试
    // ============================================================

    /// <summary>没挂立牌翻转特效的单位，生成时补一个</summary>
    private void EnsureFlipEffect(GameObject go)
    {
        if (!addFlipEffect) return;
        if (go.GetComponent<StandeeFlip>() == null)
            go.AddComponent<StandeeFlip>();
    }

    /// <summary>没挂 buff 套环特效的单位，生成时补一个</summary>
    private void EnsureBuffRing(GameObject go)
    {
        if (!addBuffRing) return;
        if (go.GetComponent<BuffRingSweep>() == null)
            go.AddComponent<BuffRingSweep>();
    }

    /// <summary>清掉场上的敌人和障碍物（调试用）</summary>
    [ContextMenu("清空场上棋子（不含玩家）")]
    private void ClearSpawned()
    {
        foreach (EnemyUnit enemy in enemies)
            if (enemy != null) Destroy(enemy.gameObject);
        foreach (Obstacle obstacle in obstacles)
            if (obstacle != null) Destroy(obstacle.gameObject);
        enemies.Clear();
        obstacles.Clear();
    }
}
