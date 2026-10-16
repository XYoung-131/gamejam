using UnityEngine;

// ============================================================
//  【橘子饮料】  稀有度：稀有
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：恢复1点血量，移除自身所有负面状态
//    · 稀有度：稀有
//
//  ── 实现方式 ─────────────────────────────────────────────
//  走 PotionData 基类的数据驱动分支，效果写在 effects 列表里，
//  子类只负责预填数值，不重复实现结算。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/橘子饮料
//  存放路径：Assets/Game/ScriptableObjects/饮料/橘子饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "橘子饮料", menuName = "Game/Potion/橘子饮料")]
public class Potion_橘子饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "橘子饮料";
        description = "恢复1点血量，移除自身所有负面状态";
        rarity = PotionRarity.稀有;

        // 两条效果：先回血，再清掉自身的负面状态
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.治疗,
            targetType = PotionTargetType.自身,
            value = 1
        });
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.清除负面状态,
            targetType = PotionTargetType.自身,
            value = 1
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「恢复1点血量，移除自身所有负面状态」，稀有度「稀有」。" +
                   "PotionRarity 共三档（普通 / 特殊 / 稀有），本瓶取「稀有」。";
    }
}
