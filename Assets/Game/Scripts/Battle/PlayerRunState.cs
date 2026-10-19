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
    /// <summary>战斗结束界面"进行 1 次技能选择"选中的技能（加入抽卡池）</summary>
    public static readonly List<CardData> ExtraCards = new List<CardData>();

    /// <summary>"捡起思维碎片"选中的遗物</summary>
    public static readonly List<RelicData> Relics = new List<RelicData>();

    /// <summary>"药水"选项领取的药水</summary>
    public static readonly List<PotionData> Potions = new List<PotionData>();

    /// <summary>耐力/思绪上限与每回合回复量的累计增量（精英 +2 / BOSS +5）</summary>
    public static int BonusStamina;
    public static int BonusThought;

    public static void Capture(PlayerUnit player)
    {
        if (player == null) return;
        PlayerCardSystem cards = player.GetComponent<PlayerCardSystem>();
        if (cards == null) return;
        Health = player.GetCurrentHealth();
        MaxHealth = player.GetMaxHealth();
        MaxStamina = player.maxStamina;
        MaxThought = player.maxThought;
        StaminaRegen = player.staminaRegenPerTurnEnd;
        ThoughtRegen = player.thoughtRegenPerTurnEnd;
        savedAttack.Clear(); savedAttack.AddRange(cards.attackPool);
        savedDefense.Clear(); savedDefense.AddRange(cards.defensePool);
        Potions.Clear(); Potions.AddRange(player.Potions);
        Relics.Clear(); Relics.AddRange(player.Relics);
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
            if (cards != null)
            {
                cards.attackPool = new List<CardData>(savedAttack);
                cards.defensePool = new List<CardData>(savedDefense);
            }
            foreach (RelicData relic in Relics) player.RestoreOwnedRelic(relic);
            foreach (PotionData potion in Potions) player.AddPotion(potion);
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

        if (BonusStamina > 0)
        {
            player.maxStamina += BonusStamina;
            player.staminaRegenPerTurnEnd += BonusStamina;
        }
        if (BonusThought > 0)
        {
            player.maxThought += BonusThought;
            player.thoughtRegenPerTurnEnd += BonusThought;
        }
    }

    /// <summary>清空全部暂存（战斗失败 / 新的一局开始时调用）</summary>
    public static void Reset()
    {
        ExtraCards.Clear();
        Relics.Clear();
        Potions.Clear();
        BonusStamina = 0;
        BonusThought = 0;
        HasSnapshot = false;
        savedAttack.Clear(); savedDefense.Clear();
        PotionMisses = 0;
    }
}
