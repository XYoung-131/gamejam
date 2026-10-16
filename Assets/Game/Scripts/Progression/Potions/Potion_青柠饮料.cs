using UnityEngine;

// ============================================================
//  【青柠饮料】  稀有度：稀有
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：恢复所有技能全部使用次数
//    · 稀有度：稀有
//
//  ── 实现方式 ─────────────────────────────────────────────
//  本文件只是"数值模板"：结算逻辑全部在 PotionData 基类
//  （队员版，数据驱动），子类只在 Reset() 里预填策划数值。
//  实际投入运行的资产是 Assets/Game/ScriptableObjects/药水/青柠饮料.asset。
//
//  基类「恢复所有技能使用次数」分支 → RestoreSkillUses(user)：
//  遍历 PlayerCardSystem 的技能槽位，把仍在槽中的有限次数技能
//  （card.maxUses > 0）的 remainingUses 补回上限，再广播 SlotChanged。
//  已被卡牌系统移除的技能不会凭空重建。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/青柠饮料
//  存放路径：Assets/Game/ScriptableObjects/药水/青柠饮料.asset
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
        description = "恢复所有技能的全部使用次数";
        rarity = PotionRarity.稀有;

        // 效果只有一条：把身上所有卡牌槽位的已用次数清零
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.恢复所有技能使用次数,
            targetType = PotionTargetType.自身,
            value = 0
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyMode = PotionPenaltyMode.达到次数后每次触发;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;
        rewardCondition = PotionRewardCondition.使用后;

        devNotes = "策划表原文：效果「恢复所有技能全部使用次数」，稀有度「稀有」。";
    }
}
