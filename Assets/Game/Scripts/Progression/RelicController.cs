using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  RelicController——遗物条件触发的"总调度"（挂在玩家身上）
//
//  为什么需要它：
//  RelicData 只负责"给定一个触发时机，把这件遗物该触发的那几条效果结算掉"
//  （RelicData.Trigger），它并不知道游戏什么时候到了那个时机。
//  这个组件就是那根线：订阅 EventManager 的事件 / 单位生命周期钩子，
//  在 17 个 RelicTrigger 时机上，对玩家持有的每一件遗物调用
//        relic.Trigger(时机, 玩家, 目标)
//  把队友写好的遗物效果真正跑起来。
//
//  另外还兜底几件"RelicData 里留了枚举、但引擎没有对应系统"的独有能力：
//    · 一击摧毁障碍物（垃圾毁灭者）      —— 出攻击技能时 ForceDestroy 全场障碍物
//    · 自动闪避到安全格（反射神经）      —— 受到近战攻击前躲到安全格，取消这次伤害
//    · 永久技能伤害成长（玛塔的板牙）    —— 用指定技能累计击杀，永久加伤
//    · 战斗结束掉落物品（劣质饮料机）    —— 战斗结束把配置的饮料塞进背包
//
//  挂载位置：和 PlayerUnit 挂在同一个 GameObject 上（[RequireComponent] 会自动保证）。
// ============================================================

[RequireComponent(typeof(PlayerUnit))]
public class RelicController : MonoBehaviour
{
    protected PlayerUnit player;

    // 当前楼层（第 1 章 = 1 层），由 LevelStarted 事件刷新
    protected int floorNumber = 1;

    // 玛塔的板牙：按遗物名记账的"成长击杀数"
    protected readonly Dictionary<string, int> growthKillCounts = new Dictionary<string, int>();

    // 当前正在结算的牌（供击杀归属判定：这一击是不是用指定技能打的）
    protected CardData resolvingCard;

    // 同一触发时机的重入保护，避免"受到伤害时→造成伤害→又受到伤害"这类递归
    private readonly HashSet<RelicTrigger> activeTriggers = new HashSet<RelicTrigger>();

    private void Awake()
    {
        player = GetComponent<PlayerUnit>();
    }

    private void OnEnable()
    {
        EventManager.AddListener<int>("LevelStarted", HandleLevelStarted);
        EventManager.AddListener<int, bool>("WaveStarted", HandleWaveStarted);
        EventManager.AddListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.AddListener("BuffsSettled", HandlePlayerTurnEnded);

        EventManager.AddListener<Unit, DamageContext>("UnitAboutToTakeDamage", HandleAboutToTakeDamage);
        EventManager.AddListener<Unit, Unit>("UnitDamagedBy", HandleUnitDamagedBy);
        EventManager.AddListener<Unit>("UnitDied", HandleUnitDied);
        EventManager.AddListener<Obstacle>("ObstacleDestroyed", HandleObstacleDestroyed);
        EventManager.AddListener<Unit>("UnitMoved", HandleUnitMoved);

        EventManager.AddListener<CardData, Unit>("PlayerCardAboutToResolve", HandleCardAboutToResolve);
        EventManager.AddListener<CardData, Unit, Unit>("CardUsed", HandleCardUsed);
        EventManager.AddListener<CardData, Unit, Obstacle>("CardUsedOnObstacle", HandleCardUsedOnObstacle);
        EventManager.AddListener<PotionData, Unit>("PotionUsed", HandlePotionUsed);
        EventManager.AddListener<GameState>("GameStateChanged", HandleGameStateChanged);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<int>("LevelStarted", HandleLevelStarted);
        EventManager.RemoveListener<int, bool>("WaveStarted", HandleWaveStarted);
        EventManager.RemoveListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.RemoveListener("BuffsSettled", HandlePlayerTurnEnded);

        EventManager.RemoveListener<Unit, DamageContext>("UnitAboutToTakeDamage", HandleAboutToTakeDamage);
        EventManager.RemoveListener<Unit, Unit>("UnitDamagedBy", HandleUnitDamagedBy);
        EventManager.RemoveListener<Unit>("UnitDied", HandleUnitDied);
        EventManager.RemoveListener<Obstacle>("ObstacleDestroyed", HandleObstacleDestroyed);
        EventManager.RemoveListener<Unit>("UnitMoved", HandleUnitMoved);

        EventManager.RemoveListener<CardData, Unit>("PlayerCardAboutToResolve", HandleCardAboutToResolve);
        EventManager.RemoveListener<CardData, Unit, Unit>("CardUsed", HandleCardUsed);
        EventManager.RemoveListener<CardData, Unit, Obstacle>("CardUsedOnObstacle", HandleCardUsedOnObstacle);
        EventManager.RemoveListener<PotionData, Unit>("PotionUsed", HandlePotionUsed);
        EventManager.RemoveListener<GameState>("GameStateChanged", HandleGameStateChanged);
    }

    // ============================================================
    //                    触发时机 → Trigger()
    // ============================================================

    /// <summary>
    /// 对玩家当前持有的每一件遗物，在指定时机调用 RelicData.Trigger。
    /// 拷贝一份列表再遍历：遗物效果可能在本轮结算里给玩家新增遗物（如战斗结束掉落），
    /// 直接遍历原列表会抛"集合被修改"异常。
    /// </summary>
    protected void TriggerRelics(RelicTrigger trigger, Unit target = null)
    {
        if (player == null || player.IsDead) return;
        if (!activeTriggers.Add(trigger)) return;   // 同一时机正在结算，跳过递归

        try
        {
            var owned = new List<RelicData>(player.Relics);
            foreach (var relic in owned)
            {
                if (relic == null) continue;
                relic.Trigger(trigger, player, target);
            }
        }
        finally
        {
            activeTriggers.Remove(trigger);
        }
    }

    // ---------- 关卡 / 波次 ----------

    private void HandleLevelStarted(int floor)
    {
        floorNumber = Mathf.Max(1, floor);

        TriggerRelics(RelicTrigger.关卡开始);
        // 新关卡的敌人此刻入场（笑靥：远程单位入场眩晕等）
        TriggerRelics(RelicTrigger.敌人入场时);
    }

    private void HandleWaveStarted(int waveIndex, bool isLastWave)
    {
        TriggerRelics(RelicTrigger.波次开始);
        TriggerRelics(RelicTrigger.敌人入场时);

        if (isLastWave)
            TriggerRelics(RelicTrigger.最后一波开始);
    }

    // ---------- 回合 ----------

    private void HandlePlayerTurnStarted(int turnCount)
    {
        TriggerRelics(RelicTrigger.回合开始);
    }

    private void HandlePlayerTurnEnded()
    {
        TriggerRelics(RelicTrigger.回合结束);
    }

    // ---------- 伤害 / 击杀 / 移动 ----------

    private void HandleAboutToTakeDamage(Unit unit, DamageContext ctx)
    {
        if (unit != player || ctx == null) return;

        // 受到攻击前：遗物先手（反伤、自动闪避等）
        TriggerRelics(RelicTrigger.受到攻击前, ctx.attacker);
        if (ctx.amount <= 0) return;

        // 反射神经：被近战打之前自动躲到安全格，取消这次伤害
        TryAutoDodge(ctx);
    }

    private void HandleUnitDamagedBy(Unit damaged, Unit source)
    {
        if (damaged == player)
            TriggerRelics(RelicTrigger.受到伤害时, source);

        // 玩家造成的伤害（source = 玩家）→ 造成伤害时，目标是被打的那个
        if (source == player && damaged != player)
            TriggerRelics(RelicTrigger.造成伤害时, damaged);
    }

    private void HandleUnitDied(Unit dead)
    {
        if (dead == null) return;

        if (dead is EnemyUnit)
        {
            TriggerRelics(RelicTrigger.击杀敌人时, dead);
            CountGrowthKill(dead);      // 玛塔的板牙：累计击杀 → 永久加伤
        }
    }

    private void HandleObstacleDestroyed(Obstacle obstacle)
    {
        TriggerRelics(RelicTrigger.击破障碍物时);
    }

    private void HandleUnitMoved(Unit unit)
    {
        if (unit != player) return;
        TriggerRelics(RelicTrigger.移动时);
    }

    private void HandlePotionUsed(PotionData potion, Unit user)
    {
        if (user != player) return;
        TriggerRelics(RelicTrigger.使用药水时);
    }

    // ---------- 使用技能 ----------

    private void HandleCardAboutToResolve(CardData card, Unit caster)
    {
        if (caster != player) return;
        resolvingCard = card;   // 记下正在结算的牌，供击杀归属判定
    }

    private void HandleCardUsed(CardData card, Unit caster, Unit target)
    {
        if (caster != player) return;

        TriggerRelics(RelicTrigger.使用技能时, target);
        TryDestroyAllObstacles(card);
        resolvingCard = null;
    }

    private void HandleCardUsedOnObstacle(CardData card, Unit caster, Obstacle obstacle)
    {
        if (caster != player) return;

        TryDestroyAllObstacles(card);
        resolvingCard = null;
    }

    // ---------- 战斗结束 ----------

    private void HandleGameStateChanged(GameState state)
    {
        if (state != GameState.Victory && state != GameState.Defeat) return;

        TriggerRelics(RelicTrigger.战斗结束);
        GrantBattleEndPotions();

        // 非"跨战斗保留"的成长计数在战斗结束时清零
        ClearNonPersistentGrowth();
        resolvingCard = null;
    }

    // ============================================================
    //     独有能力①：一击摧毁障碍物（垃圾毁灭者）
    // ============================================================

    /// <summary>
    /// 玩家用攻击技能时，若持有"一击摧毁障碍物"类遗物，
    /// 则无视柱子的"无限血量"，直接摧毁场上所有障碍物（含大理石柱）。
    /// </summary>
    private void TryDestroyAllObstacles(CardData card)
    {
        if (player == null || card == null || !card.IsAttack) return;
        if (!HasRelicEffect(RelicEffectType.一击摧毁障碍物)) return;
        if (HexGrid.Instance == null) return;

        foreach (var cell in HexGrid.Instance.GetAllCells())
        {
            if (cell != null && cell.obstacle != null)
                cell.obstacle.ForceDestroy();
        }

        Debug.Log("垃圾毁灭者：一击摧毁了场上所有障碍物");
    }

    // ============================================================
    //     独有能力②：自动闪避到安全格（反射神经）
    // ============================================================

    /// <summary>
    /// 受到近战攻击前，持有"自动闪避到安全格"遗物时，
    /// 尝试移动到相邻的"不被威胁者贴身"的格子，并把本次伤害清零。
    /// </summary>
    private void TryAutoDodge(DamageContext ctx)
    {
        if (player == null || ctx == null || ctx.attacker == null) return;
        if (player.IsDead) return;
        if (!HasRelicEffect(RelicEffectType.自动闪避到安全格)) return;

        // 只躲近战（"反射神经：自动躲开近战"）
        bool melee = ctx.isMelee ||
                     ctx.attacker.GetCoord().Distance(player.GetCoord()) <= 1;
        if (!melee) return;

        if (!TryFindSafeCell(ctx.attacker.GetCoord(), out HexCoord safe)) return;

        player.TeleportTo(safe);
        ctx.amount = 0;
        Debug.Log($"反射神经：自动闪避到 {safe}，本次伤害被取消");
    }

    /// <summary>在玩家当前位置的 6 个邻居里找一格安全落点（能走、没人、且不贴着威胁单位）。</summary>
    private bool TryFindSafeCell(HexCoord threat, out HexCoord result)
    {
        result = default;
        if (HexGrid.Instance == null) return false;

        HexCoord origin = player.GetCoord();

        for (int i = 0; i < 6; i++)
        {
            HexCoord n = origin.GetNeighbor(i);
            HexCell cell = HexGrid.Instance.GetCell(n);
            if (cell == null || !cell.isWalkable) continue;
            if (cell.IsBlockedByObstacle()) continue;
            if (cell.occupyUnit != null && cell.occupyUnit != player) continue;
            if (n.Distance(threat) <= 1) continue;   // 贴着威胁格不算安全

            result = n;
            return true;
        }

        return false;
    }

    // ============================================================
    //     独有能力③：永久技能伤害成长（玛塔的板牙）
    // ============================================================

    /// <summary>
    /// 一件卡牌能拿到的"成长型"伤害加成（供 CardData.GetFinalDamage 查询）。
    /// 成长值 = (累计击杀 / 阈值) × 每步加成，取整。
    /// </summary>
    public int GetSkillDamageBonus(CardData card)
    {
        if (player == null || card == null) return 0;

        int total = 0;
        foreach (var relic in player.Relics)
        {
            if (relic == null || relic.growthKillThreshold <= 0) continue;
            if (!MatchesGrowthSkill(relic, card)) continue;

            growthKillCounts.TryGetValue(relic.relicName, out int kills);
            if (kills <= 0) continue;

            total += (kills / relic.growthKillThreshold) * Mathf.RoundToInt(relic.growthBonusPerStep);
        }
        return total;
    }

    /// <summary>统计击杀：击杀发生时若正在结算的牌命中该遗物的成长条件，就记一次。</summary>
    private void CountGrowthKill(Unit dead)
    {
        if (player == null) return;

        foreach (var relic in player.Relics)
        {
            if (relic == null || relic.growthKillThreshold <= 0) continue;
            if (!MatchesGrowthSkill(relic, resolvingCard)) continue;

            growthKillCounts.TryGetValue(relic.relicName, out int c);
            growthKillCounts[relic.relicName] = c + 1;
        }
    }

    /// <summary>这张牌算不算该遗物的"成长来源技能"。</summary>
    private bool MatchesGrowthSkill(RelicData relic, CardData card)
    {
        if (relic == null || card == null) return false;

        // 按技能族（标签）匹配：玛塔的板牙覆盖所有带啃咬标签的技能
        if (relic.growthFilterBySkillTag)
            return card.tags.Contains(relic.growthSkillTag);

        // 按单张卡匹配
        return relic.growthCountSkill != null && card == relic.growthCountSkill;
    }

    /// <summary>战斗结束：不跨战斗保留的成长计数清零。</summary>
    private void ClearNonPersistentGrowth()
    {
        if (player == null) return;

        foreach (var relic in player.Relics)
        {
            if (relic == null || relic.growthPersistsAcrossBattles) continue;
            growthKillCounts.Remove(relic.relicName);
        }
    }

    // ============================================================
    //     独有能力④：战斗结束掉落物品（劣质饮料机）
    // ============================================================

    /// <summary>战斗结束时，把"掉落物品"类遗物配置的饮料塞进玩家背包。</summary>
    private void GrantBattleEndPotions()
    {
        if (player == null) return;

        foreach (var relic in player.Relics)
        {
            if (relic == null) continue;

            foreach (var effect in relic.GetEffectsByTrigger(RelicTrigger.战斗结束))
            {
                if (effect == null) continue;
                if (effect.effectType != RelicEffectType.掉落物品 || effect.potion == null) continue;

                if (player.AddPotion(effect.potion))
                    Debug.Log($"【{relic.relicName}】战斗结束额外掉落【{effect.potion.potionName}】");
                else
                    Debug.Log($"【{relic.relicName}】想额外掉落【{effect.potion.potionName}】，但药水背包已满");
            }
        }
    }

    // ============================================================
    //                        工具方法
    // ============================================================

    /// <summary>玩家当前是否持有某类遗物效果（常驻查询）。</summary>
    protected bool HasRelicEffect(RelicEffectType type)
    {
        if (player == null) return false;
        return RelicData.HasEffect(player.Relics, type);
    }
}
