using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  跨关卡玩家状态缓存（最小 RunData）
//
//  战斗结束奖励界面领取的内容（技能 / 遗物 / 药水 / 上限提升）
//  在这里暂存；点击"跳过奖励"进入下一关时，LevelManager 生成
//  新玩家后统一回放（ApplyTo），奖励才真正带到下一关。
//
//  生命周期：
//  - 战斗胜利 → 面板领取 → 暂存累积；
//  - 跳过奖励 → 场景重载 → ApplyTo 回放给新玩家（不清空，继续累积）；
//  - 战斗失败 → BattleEndPanel 收到 Defeat 时 Reset 清零重来。
// ============================================================
public static class PlayerRunState
{
    public static bool HasSnapshot { get; private set; }
    public static int Health { get; private set; }
    public static int MaxHealth { get; private set; }
    public static int MaxStamina { get; private set; }
    public static int MaxThought { get; private set; }
    public static int StaminaRegen { get; private set; }
    public static int ThoughtRegen { get; private set; }
    public static int PotionMisses { get; set; }
    private static readonly List<CardData> savedAttack = new List<CardData>();
    private static readonly List<CardData> savedDefense = new List<CardData>();
    /// <summary>牌池有没有真的存过一次（没存过就别在 ApplyTo 里拿空列表去覆盖开局牌库）</summary>
    private static bool hasPoolSnapshot;
    /// <summary>战斗结束界面"进行 1 次技能选择"选中的技能（加入抽卡池）</summary>
    public static readonly List<CardData> ExtraCards = new List<CardData>();

    /// <summary>"捡起思维碎片"选中的遗物</summary>
    public static readonly List<RelicData> Relics = new List<RelicData>();

    /// <summary>"药水"选项领取的药水</summary>
    public static readonly List<PotionData> Potions = new List<PotionData>();

    /// <summary>耐力/思绪上限与每回合回复量的累计增量（精英 +2 / BOSS +5）</summary>
    public static int BonusStamina;
    public static int BonusThought;

    // ---- 存档（RunSaveSystem）读取用：这几项本来只有内部可见 ----
    /// <summary>存档下来的攻击牌池（只读）</summary>
    public static IReadOnlyList<CardData> SavedAttackPool { get { return savedAttack; } }
    /// <summary>存档下来的防御牌池（只读）</summary>
    public static IReadOnlyList<CardData> SavedDefensePool { get { return savedDefense; } }
    /// <summary>牌池有没有真的存过一次</summary>
    public static bool HasPoolSnapshot { get { return hasPoolSnapshot; } }

    /// <summary>
    /// 「重启」选项 1「额外进行 2 次新技能选择」暂存次数。
    /// 事件里没有卡牌界面，先记在这里；下一次战斗结算界面（BattleEndPanel）
    /// 打开时按这个数字补出"额外的记忆"按钮，每选 1 次减 1。
    /// </summary>
    public static int PendingExtraCardChoices;

    /// <summary>事件里追加待进行的技能选择次数</summary>
    public static void QueueExtraCardChoices(int count)
    {
        if (count <= 0) return;
        PendingExtraCardChoices += count;
    }

    // ============================================================
    //        事件（战斗外）改动玩家的入口
    //
    //  事件是在"上一场战斗的场景"里弹的，玩家对象还活着，但下一关会
    //  重新生成玩家并走 ApplyTo 回放快照——所以事件改完玩家必须立刻
    //  Capture 一次，否则改动活不过场景切换。
    //  没有场上玩家（开局的"实验开始"）时，直接改快照 / 待回放增量。
    // ============================================================

    /// <summary>
    /// 事件里给"最大耐力上限"加减（负数 = 减少），会带到后续关卡。
    /// 减上限时当前耐力会被夹到新上限——否则 HUD 会出现"20/8"这种，
    /// 而且本场真的还能按 20 点花（TrySpendStamina 只比 current）。
    /// </summary>
    public static void ApplyMaxStaminaDelta(PlayerUnit player, int delta)
    {
        if (delta == 0) return;
        if (player != null)
        {
            player.maxStamina = Mathf.Max(1, player.maxStamina + delta);
            player.ClampResourcesToMax();
            Capture(player);
        }
        else if (HasSnapshot) MaxStamina = Mathf.Max(1, MaxStamina + delta);
        else BonusStamina += delta;   // 没有快照也没有玩家（开局事件）：记增量，回放时再夹
    }

    /// <summary>事件里给"最大思绪上限"加减（负数 = 减少），会带到后续关卡</summary>
    public static void ApplyMaxThoughtDelta(PlayerUnit player, int delta)
    {
        if (delta == 0) return;
        if (player != null)
        {
            player.maxThought = Mathf.Max(1, player.maxThought + delta);
            player.ClampResourcesToMax();
            Capture(player);
        }
        else if (HasSnapshot) MaxThought = Mathf.Max(1, MaxThought + delta);
        else BonusThought += delta;
    }

    /// <summary>事件里发一件遗物：有场上玩家就当场获得（永久 buff 立刻生效），然后刷新快照</summary>
    public static void GrantRelic(PlayerUnit player, RelicData relic)
    {
        if (relic == null) return;
        if (player != null)
        {
            if (!player.HasRelic(relic)) player.AddRelic(relic);
            Capture(player);
            return;
        }
        if (!Relics.Contains(relic)) Relics.Add(relic);
    }

    /// <summary>事件里改当前生命值（负数 = 扣血，最少留 1 点），然后刷新快照</summary>
    public static void ApplyHealthDelta(PlayerUnit player, int delta)
    {
        if (delta == 0) return;
        if (player != null)
        {
            int hp = Mathf.Max(1, player.GetCurrentHealth() + delta);
            player.SetRunHealth(hp, player.GetMaxHealth());
            Capture(player);
            return;
        }
        // 没有场上玩家时只能改快照；连快照都没有（本局还没打过一场）说明
        // 这一局还没有玩家对象可改，写进去也只会变成脏值，所以先判 HasSnapshot。
        if (HasSnapshot) Health = Mathf.Max(1, Health + delta);
    }

    /// <summary>
    /// 事件里改"最大生命上限"（「温泉」增加 2/4 点、「抉择」减少 2 点）。
    /// 增加上限不会补当前血；减少上限时当前血会被压到新上限，避免出现 30/28 这种数值。
    /// </summary>
    public static void ApplyMaxHealthDelta(PlayerUnit player, int delta)
    {
        if (delta == 0) return;
        if (player != null)
        {
            int max = Mathf.Max(1, player.GetMaxHealth() + delta);
            int hp = Mathf.Min(player.GetCurrentHealth(), max);
            player.SetRunHealth(hp, max);
            Capture(player);
            return;
        }
        if (HasSnapshot)
        {
            MaxHealth = Mathf.Max(1, MaxHealth + delta);
            Health = Mathf.Min(Health, MaxHealth);
        }
    }

    /// <summary>事件里按最大生命的比例回血（"回复10%血量"），然后刷新快照</summary>
    public static void HealPercent(PlayerUnit player, float ratio)
    {
        if (player == null || ratio <= 0f) return;
        player.Heal(Mathf.CeilToInt(player.GetMaxHealth() * ratio));
        Capture(player);
    }

    /// <summary>事件里发一瓶药水（背包满则拿不到，返回 false），然后刷新快照</summary>
    public static bool GrantPotion(PlayerUnit player, PotionData potion)
    {
        if (potion == null) return false;
        if (player != null)
        {
            bool added = player.AddPotion(potion);
            Capture(player);
            return added;
        }
        Potions.Add(potion);
        return true;
    }

    /// <summary>
    /// 把玩家当前状态写进快照。
    ///
    /// 注意：这里**不能**因为玩家身上没有 PlayerCardSystem 就整段 return。
    /// 卡池只是快照的一部分；早先写成"没卡系统直接返回"，
    /// 一旦某次 Capture 发生在 LevelManager 补组件之前，血量/上限/遗物/药水
    /// 会全部静默不保存 —— 下一关就变成"血量被重置"的假象。
    /// </summary>
    public static void Capture(PlayerUnit player)
    {
        if (player == null) return;
        Health = player.GetCurrentHealth();
        MaxHealth = player.GetMaxHealth();
        MaxStamina = player.maxStamina;
        MaxThought = player.maxThought;
        StaminaRegen = player.staminaRegenPerTurnEnd;
        ThoughtRegen = player.thoughtRegenPerTurnEnd;
        Potions.Clear(); Potions.AddRange(player.Potions);
        Relics.Clear(); Relics.AddRange(player.Relics);

        // 牌池存不了就直接跳过这两项，其它照样存（下次 Capture 会补上）
        PlayerCardSystem cards = player.GetComponent<PlayerCardSystem>();
        if (cards != null)
        {
            savedAttack.Clear(); savedAttack.AddRange(cards.attackPool);
            savedDefense.Clear(); savedDefense.AddRange(cards.defensePool);
            hasPoolSnapshot = true;
        }
        HasSnapshot = true;
    }

    /// <summary>
    /// 把暂存的奖励回放到玩家身上。
    /// 由 LevelManager.SpawnPlayer 在 Initialize 之后调用——
    /// Initialize 会把 currentStamina/currentThought 重置为 start 值，
    /// 所以这里补上限的同时把当前值也补上增量。
    /// </summary>
    public static void ApplyTo(PlayerUnit player)
    {
        if (player == null) return;

        PlayerCardSystem cards = player.GetComponent<PlayerCardSystem>();
        if (HasSnapshot)
        {
            player.SetRunHealth(Health, MaxHealth);
            player.maxStamina = MaxStamina;
            player.maxThought = MaxThought;
            player.staminaRegenPerTurnEnd = StaminaRegen;
            player.thoughtRegenPerTurnEnd = ThoughtRegen;
            if (cards != null && hasPoolSnapshot)
            {
                cards.attackPool = new List<CardData>(savedAttack);
                cards.defensePool = new List<CardData>(savedDefense);
            }
            foreach (RelicData relic in Relics) player.RestoreOwnedRelic(relic);
            foreach (PotionData potion in Potions) player.AddPotion(potion);
            // Initialize 给的初始耐力/思绪可能高于快照里的上限（上限被事件下调过），
            // 不夹一次就会出现"当前值 > 上限"的槽。
            player.ClampResourcesToMax();
            Debug.Log($"[RunState] 回放快照：血量 {Health}/{MaxHealth}（上限 {player.GetMaxHealth()}），" +
                      $"耐力上限 {MaxStamina}、思绪上限 {MaxThought}，" +
                      $"遗物 {Relics.Count} 件、药水 {Potions.Count} 瓶");
            return;
        }
        if (cards != null)
        {
            foreach (CardData card in ExtraCards)
            {
                if (card == null) continue;
                // 奖励技能进对应的抽卡池（攻击 / 防御），不是直接塞进开局槽位——
                // 这样它在后续关卡里是"可以抽到"的牌，符合"加入卡池"的规则。
                List<CardData> pool = card.library == CardPool.防御 ? cards.defensePool : cards.attackPool;
                if (pool != null)
                    pool.Add(card);
            }
        }

        foreach (RelicData relic in Relics)
            if (relic != null && !player.HasRelic(relic))
                player.RestoreOwnedRelic(relic);

        foreach (PotionData potion in Potions)
            if (potion != null)
                player.AddPotion(potion);

        // 没有快照 = 这是本局第一场战斗：把事件里攒下的上限增量补上。
        // 增量可能是负的（开局前就有减上限的事件），所以判 != 0 而不是 > 0，别再静默吞掉。
        if (BonusStamina != 0)
        {
            player.maxStamina = Mathf.Max(1, player.maxStamina + BonusStamina);
            player.staminaRegenPerTurnEnd = Mathf.Max(0, player.staminaRegenPerTurnEnd + BonusStamina);
        }
        if (BonusThought != 0)
        {
            player.maxThought = Mathf.Max(1, player.maxThought + BonusThought);
            player.thoughtRegenPerTurnEnd = Mathf.Max(0, player.thoughtRegenPerTurnEnd + BonusThought);
        }
        player.ClampResourcesToMax();
        Debug.Log($"[RunState] 本局第一场战斗：血量 {player.GetCurrentHealth()}/{player.GetMaxHealth()}（无快照，按玩家配置开局）");
    }

    /// <summary>
    /// 清空全部暂存（战斗失败 / 新的一局开始时调用）。
    ///
    /// 数值字段（Health/MaxHealth/MaxStamina/MaxThought/回复量）也必须一起清：
    /// 它们虽然被 HasSnapshot 挡着不会回放，但事件在没有场上玩家时会直接改这些字段，
    /// 留下上一局的脏值（例如上一局 MaxHealth 20、这一局 10 点血写着 20 上限）。
    /// </summary>
    public static void Reset()
    {
        ExtraCards.Clear();
        Relics.Clear();
        Potions.Clear();
        BonusStamina = 0;
        BonusThought = 0;
        PendingExtraCardChoices = 0;
        HasSnapshot = false;
        savedAttack.Clear(); savedDefense.Clear();
        hasPoolSnapshot = false;
        PotionMisses = 0;
        Health = 0;
        MaxHealth = 0;
        MaxStamina = 0;
        MaxThought = 0;
        StaminaRegen = 0;
        ThoughtRegen = 0;
    }

    /// <summary>
    /// 从存档恢复整份快照（RunSaveSystem.Load 调用）。
    /// 先 Reset 再灌入，避免上一局残留；资产引用已在读档时按名字解析好。
    /// </summary>
    public static void LoadSnapshot(bool hasSnapshot, int health, int maxHealth, int maxStamina, int maxThought,
        int staminaRegen, int thoughtRegen, int potionMisses, bool hasPoolSnapshot,
        List<CardData> attackPool, List<CardData> defensePool, List<CardData> extraCards,
        List<RelicData> relics, List<PotionData> potions,
        int bonusStamina, int bonusThought, int pendingExtraCardChoices)
    {
        Reset();

        HasSnapshot = hasSnapshot;
        Health = health;
        MaxHealth = maxHealth;
        MaxStamina = maxStamina;
        MaxThought = maxThought;
        StaminaRegen = staminaRegen;
        ThoughtRegen = thoughtRegen;
        PotionMisses = potionMisses;
        BonusStamina = bonusStamina;
        BonusThought = bonusThought;
        PendingExtraCardChoices = pendingExtraCardChoices;

        if (attackPool != null) savedAttack.AddRange(attackPool);
        if (defensePool != null) savedDefense.AddRange(defensePool);
        // 牌池快照标记只在这两份列表真的有内容时才算数——空牌池回放会清空开局牌库
        PlayerRunState.hasPoolSnapshot = hasPoolSnapshot && (savedAttack.Count > 0 || savedDefense.Count > 0);
        if (extraCards != null) ExtraCards.AddRange(extraCards);
        if (relics != null) Relics.AddRange(relics);
        if (potions != null) Potions.AddRange(potions);
    }
}
