using UnityEngine;

// ============================================================
//  【蓝莓饮料】  稀有度：稀有
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：获得3层开悟
//    · 稀有度：稀有
//
//  ── 实现方式 ─────────────────────────────────────────────
//  本文件只是"数值模板"：结算逻辑全部在 PotionData 基类
//  （队员版，数据驱动），子类只在 Reset() 里预填策划数值。
//  实际投入运行的资产是 Assets/Game/ScriptableObjects/药水/蓝莓饮料.asset。
//
//  基类「施加Buff」分支 → ApplyBuffEffect：
//  user.ApplyBuff(effect.buff, Mathf.Max(1, effect.buffStacks))。
//  ⚠ 层数在 buffStacks 字段里（不是 value）——改层数改 buffStacks。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/蓝莓饮料
//  存放路径：Assets/Game/ScriptableObjects/药水/蓝莓饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "蓝莓饮料", menuName = "Game/Potion/蓝莓饮料")]
public class Potion_蓝莓饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "蓝莓饮料";
        description = "获得3层开悟";
        rarity = PotionRarity.稀有;

        // 效果只有一条：给自己挂 3 层开悟
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.施加Buff,
            targetType = PotionTargetType.自身,
            value = 0,
            buffStacks = 3,   // ← 层数在这里，基类按 buffStacks 传给 ApplyBuff
            buff = null,      // ← 资产引用：Buff/开悟.asset（药水/蓝莓饮料.asset 已配）
            requiredBuffName = "开悟"
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyMode = PotionPenaltyMode.达到次数后每次触发;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;
        rewardCondition = PotionRewardCondition.使用后;

        devNotes = "策划表原文：效果「获得3层开悟」，稀有度「稀有」。" +
                   "开悟本体规格策划表里还没给，先按占位 buff 处理" +
                   "（canStack = true / maxStack = 99，所以 3 层能叠上）。";
    }
}
