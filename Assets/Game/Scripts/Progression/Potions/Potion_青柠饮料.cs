using UnityEngine;

// ============================================================
//  【青柠饮料】  稀有度：稀有
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：恢复所有技能全部使用次数
//    · 稀有度：稀有
//
//  ── 实现方式 ─────────────────────────────────────────────
//  走 PotionData 基类的数据驱动分支，效果写在 effects 列表里，
//  子类只负责预填数值，不重复实现结算。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/青柠饮料
//  存放路径：Assets/Game/ScriptableObjects/饮料/青柠饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "青柠饮料", menuName = "Game/Potion/青柠饮料")]
public class Potion_青柠饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "青柠饮料";
        description = "恢复所有技能全部使用次数";
        rarity = PotionRarity.稀有;

        // 效果只有一条：把身上所有卡牌槽位的已用次数清零
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.恢复技能使用次数,
            targetType = PotionTargetType.自身,
            value = 1
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「恢复所有技能全部使用次数」，稀有度「稀有」。" +
                   "PotionRarity 共三档（普通 / 特殊 / 稀有），本瓶取「稀有」。";
    }
}
