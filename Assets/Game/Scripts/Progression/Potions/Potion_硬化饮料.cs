using UnityEngine;

// ============================================================
//  【硬化饮料】  稀有度：普通
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：获得6点护盾
//    · 稀有度：普通
//
//  ── 实现方式 ─────────────────────────────────────────────
//  本文件只是"数值模板"：结算逻辑全部在 PotionData 基类
//  （队员版，数据驱动），子类只在 Reset() 里预填策划数值。
//  实际投入运行的资产是 Assets/Game/ScriptableObjects/药水/硬化饮料.asset。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/硬化饮料
//  存放路径：Assets/Game/ScriptableObjects/药水/硬化饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "硬化饮料", menuName = "Game/Potion/硬化饮料")]
public class Potion_硬化饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "硬化饮料";
        description = "获得6点护盾";
        rarity = PotionRarity.普通;

        // 效果只有一条：给自己加 6 点护盾
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.获得护盾,
            targetType = PotionTargetType.自身,
            value = 6
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyMode = PotionPenaltyMode.达到次数后每次触发;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;
        rewardCondition = PotionRewardCondition.使用后;

        devNotes = "策划表原文：效果「获得6点护盾」，稀有度「普通」。";
    }
}
