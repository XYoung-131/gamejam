using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  掉落系统——负责药水与遗物的掉落规则（对应文档"饮料/遗物"两节）
//
//  药水：
//  - 战斗后 potionDropChance 概率掉 1 瓶；
//  - 连续 guaranteedAfterMisses 关没掉，则下一关结束必定掉落（文档：连续 2 关没掉，
//    第 3 关结束必掉）；
//  - 品质 commonQualityChance 概率普通，否则稀有。
//
//  遗物：
//  - 通用池来自事件/商店/普通掉落；
//  - 精英/BOSS 有专属池，专属遗物的选中概率是同类遗物的 exclusiveWeightMultiplier 倍
//    （文档：2 倍）。
//
//  挂载位置：场景里任意一个常驻 GameObject（全场景唯一）。
//  数值/掉落池全部在 Inspector 配置，不写死。
// ============================================================
public class LootSystem : MonoBehaviour
{
    public static LootSystem Instance { get; private set; }

    [Header("药水掉落")]
    [Tooltip("每场战斗后掉落药水的概率（文档：50%）")]
    public float potionDropChance = 0.5f;
    [Tooltip("连续多少关没掉落之后，下一关结束必定掉落（文档：连着 2 关没掉，第 3 关结束必掉）")]
    public int guaranteedAfterMisses = 2;
    [Tooltip("掉落品质为'普通'的概率（文档：60%），其余为稀有")]
    public float commonQualityChance = 0.6f;
    [Tooltip("普通品质药水池")]
    public List<PotionData> commonPotions = new List<PotionData>();
    [Tooltip("稀有品质药水池")]
    public List<PotionData> rarePotions = new List<PotionData>();

    [Header("遗物掉落")]
    [Tooltip("精英战掉落遗物的概率")]
    public float eliteRelicDropChance = 0.5f;
    [Tooltip("BOSS 战掉落遗物的概率")]
    public float bossRelicDropChance = 1f;
    [Tooltip("专属遗物相对同类遗物的权重倍率（文档：2 倍）")]
    public float exclusiveWeightMultiplier = 2f;
    [Tooltip("通用遗物池（事件/商店/普通掉落）")]
    public List<RelicData> generalPool = new List<RelicData>();
    [Tooltip("精英专属遗物池")]
    public List<RelicData> elitePool = new List<RelicData>();
    [Tooltip("BOSS 专属遗物池")]
    public List<RelicData> bossPool = new List<RelicData>();

    [Header("自动发放")]
    [Tooltip("精英/BOSS 死亡时，是否自动掷骰并把掉到的遗物发给玩家")]
    public bool autoGrantRelicOnDeath = true;

    // 已连续多少关没有掉药水（用于保底计数）
    private int consecutivePotionMisses = 0;

    /// <summary>当前连续没掉药水的关卡数（UI 可读）</summary>
    public int ConsecutivePotionMisses => consecutivePotionMisses;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        // 监听单位死亡：精英/BOSS 倒下时自动掷遗物掉落
        EventManager.AddListener<Unit>("UnitDied", HandleUnitDied);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<Unit>("UnitDied", HandleUnitDied);
    }

    // ============================================================
    //                   遗物自动获取链路
    // ============================================================

    /// <summary>
    /// 有单位死亡时：如果是精英怪或 BOSS，按对应掉落池掷一次遗物并发给玩家。
    /// 普通小怪不掉落（文档：精英和 BOSS 才有专属掉落）。
    /// </summary>
    private void HandleUnitDied(Unit dead)
    {
        if (!autoGrantRelicOnDeath) return;

        EnemyUnit enemy = dead as EnemyUnit;
        if (enemy == null || enemy.data == null) return;

        switch (enemy.data.grade)
        {
            case UnitGrade.精英怪:
                GrantRelicToPlayer(RelicPool.精英, eliteRelicDropChance);
                break;

            case UnitGrade.BOSS:
                GrantRelicToPlayer(RelicPool.BOSS, bossRelicDropChance);
                break;
        }
    }

    /// <summary>
    /// 掷一次遗物掉落，掉到就立刻发给场上玩家。
    /// 精英/BOSS 死亡、事件奖励、商店购买都走这个入口（外部只需传掉落池和概率）。
    /// 返回这次拿到的遗物；没掉到 / 拿不到则返回 null。
    /// </summary>
    public RelicData GrantRelicToPlayer(RelicPool pool, float dropChance)
    {
        RelicData drop = RollRelicDrop(pool, dropChance);
        if (drop == null) return null;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null)
        {
            Debug.LogWarning($"掷到遗物【{drop.relicName}】，但场上没有玩家，未发放");
            return null;
        }

        if (!player.AddRelic(drop))
        {
            Debug.Log($"玩家已持有【{drop.relicName}】，本次掉落作废");
            return null;
        }

        EventManager.Trigger("RelicDropped", drop);
        return drop;
    }

    // ============================================================
    //                        药水掉落
    // ============================================================

    /// <summary>
    /// 战斗结束后掷一次药水掉落。返回掉落到的药水；没掉返回 null。
    /// 调用方负责把返回值塞进玩家背包。
    /// </summary>
    public PotionData RollBattlePotionDrop()
    {
        // 保底判断：已经连续没掉够次数 → 这次必掉
        bool guaranteed = consecutivePotionMisses >= guaranteedAfterMisses;

        if (!guaranteed && Random.value > potionDropChance)
        {
            consecutivePotionMisses++;
            Debug.Log($"本关没有掉落药水（已连续 {consecutivePotionMisses} 关没掉落）");
            return null;
        }

        if (guaranteed)
            Debug.Log("药水掉落触发保底：本关必定掉落 1 瓶");

        consecutivePotionMisses = 0;

        // 品质判定：普通 / 稀有
        bool common = Random.value < commonQualityChance;
        List<PotionData> pool = common ? commonPotions : rarePotions;

        // 对应品质池为空时兜底用另一个池，避免策划漏配导致空手
        if (pool == null || pool.Count == 0)
            pool = common ? rarePotions : commonPotions;
        if (pool == null || pool.Count == 0) return null;

        PotionData drop = pool[Random.Range(0, pool.Count)];
        Debug.Log($"掉落药水【{drop.potionName}】（{(common ? "普通" : "稀有")}）");
        EventManager.Trigger("PotionDropped", drop);
        return drop;
    }

    // ============================================================
    //                        遗物掉落
    // ============================================================

    /// <summary>精英战掉落掷骰</summary>
    public RelicData RollEliteDrop() => RollRelicDrop(RelicPool.精英, eliteRelicDropChance);

    /// <summary>BOSS 战掉落掷骰</summary>
    public RelicData RollBossDrop() => RollRelicDrop(RelicPool.BOSS, bossRelicDropChance);

    /// <summary>通用掉落掷骰（事件/商店）</summary>
    public RelicData RollGeneralDrop() => RollRelicDrop(RelicPool.通用, 1f);

    /// <summary>
    /// 按掉落池掷一个遗物。
    /// 专属池（精英/BOSS）里的遗物权重更高，表现为"掉落概率是同类遗物的 2 倍"。
    /// </summary>
    public RelicData RollRelicDrop(RelicPool pool, float dropChance)
    {
        if (Random.value > dropChance) return null;
        return PickWeighted(pool);
    }

    /// <summary>
    /// 加权随机挑选：通用池权重 1，专属池权重 exclusiveWeightMultiplier。
    /// 已经持有的遗物会被排除，不会重复掉到同一件。
    /// </summary>
    private RelicData PickWeighted(RelicPool pool)
    {
        List<RelicData> owned = GetOwnedRelics();
        List<RelicData> items = new List<RelicData>();
        List<float> weights = new List<float>();

        AddPool(generalPool, 1f, owned, items, weights);
        if (pool == RelicPool.精英) AddPool(elitePool, exclusiveWeightMultiplier, owned, items, weights);
        if (pool == RelicPool.BOSS) AddPool(bossPool, exclusiveWeightMultiplier, owned, items, weights);

        if (items.Count == 0) return null;

        float total = 0f;
        foreach (var w in weights) total += w;

        float roll = Random.value * total;
        for (int i = 0; i < items.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return items[i];
        }
        return items[items.Count - 1];
    }

    /// <summary>收集场上玩家已经持有的所有遗物（用于掉落去重）</summary>
    private List<RelicData> GetOwnedRelics()
    {
        List<RelicData> owned = new List<RelicData>();
        foreach (var player in FindObjectsOfType<PlayerUnit>())
        {
            if (player.Relics == null) continue;
            foreach (var relic in player.Relics)
                if (relic != null && !owned.Contains(relic))
                    owned.Add(relic);
        }
        return owned;
    }

    private void AddPool(List<RelicData> source, float weight, List<RelicData> owned,
                         List<RelicData> items, List<float> weights)
    {
        if (source == null) return;
        foreach (var relic in source)
        {
            if (relic == null) continue;
            if (owned.Contains(relic)) continue;   // 已持有 → 不再掉
            items.Add(relic);
            weights.Add(weight);
        }
    }

    // ============================================================
    //                  Inspector 右键测试
    // ============================================================

    [ContextMenu("测试：模拟一场战斗的药水掉落")]
    private void DebugRollPotion() => RollBattlePotionDrop();

    [ContextMenu("测试：模拟精英遗物掉落")]
    private void DebugRollElite() => RollEliteDrop();

    [ContextMenu("测试：模拟击杀精英并发放遗物")]
    private void DebugGrantElite() => GrantRelicToPlayer(RelicPool.精英, eliteRelicDropChance);

    [ContextMenu("测试：模拟击杀BOSS并发放遗物")]
    private void DebugGrantBoss() => GrantRelicToPlayer(RelicPool.BOSS, bossRelicDropChance);
}
