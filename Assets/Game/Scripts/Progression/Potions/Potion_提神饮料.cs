using UnityEngine;

// ============================================================
//  【提神饮料】  稀有度：普通
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：回复20点思绪
//    · 稀有度：普通
//    · 补充：（无）
//
//  ── 实现方式 ─────────────────────────────────────────────
//  本文件只是"数值模板"：结算逻辑全部在 PotionData 基类
//  （队员版，数据驱动），子类只在 Reset() 里预填策划数值。
//  实际投入运行的资产是 Assets/Game/ScriptableObjects/药水/提神饮料.asset。
//
//  基类「回复思绪」分支内部调用 PlayerUnit.RestoreThought(20)：
//    · 思绪 = 卡牌资源，灌注抽卡（5 点/次）和出牌都从这里扣；
//    · 回复量会被 maxThought 截断（默认 40）；
//    · 回复后广播 PlayerResourceChanged，UI 资源条自动刷新。
//
//  和「耐力饮料」是一对：一个补行动资源，一个补卡牌资源，
//  数值同为 20，所以两瓶可以放心当成同一档补给来配掉落权重。
//
//  ── 与「混乱」的交互（待策划确认）────────────────────────
//  同耐力饮料：混乱（思绪回复减半）当前只作用于回合结束的 15 点回复，
//  不影响这瓶饮料的即时回复。要改就统一改，见项目待办。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/提神饮料
//  建议存放路径：Assets/Game/ScriptableObjects/药水/提神饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "提神饮料", menuName = "Game/Potion/提神饮料")]
public class Potion_提神饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "提神饮料";
        description = "回复20点思绪";
        rarity = PotionRarity.普通;

        // 效果只有一条：给自己回 20 点思绪
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.回复思绪,
            targetType = PotionTargetType.自身,
            value = 20
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyMode = PotionPenaltyMode.达到次数后每次触发;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;
        rewardCondition = PotionRewardCondition.使用后;

        devNotes = "策划表原文：效果「回复20点思绪」，稀有度「普通」。";
    }
}
