using UnityEngine;

// ============================================================
//  【打嗝饮料】  稀有度：普通
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：将所有相邻目标推远1格
//    · 稀有度：普通
//    · 补充：（无）
//
//  ── 实现方式 ─────────────────────────────────────────────
//  本文件只是"数值模板"：结算逻辑全部在 PotionData 基类
//  （队员版，数据驱动），子类只在 Reset() 里预填策划数值。
//  实际投入运行的资产是 Assets/Game/ScriptableObjects/药水/打嗝饮料.asset。
//
//  基类「击退相邻目标」分支 → PushNeighborsAway(user, distance)：
//    · 以自己所在格为中心，取 6 个邻居格（HexCoord.GetAllNeighbors），
//      邻居格上站着谁就推谁；不推自己、不推尸体；
//    · 位移走 Unit.PushBack（以自己为参照点 → 目标朝外飞）。
//
//  ⚠ 与技能击退的差别（记入待办）：Unit.PushBack 是"只挪位置、不算碰撞"的
//    简化版；全局其他击退（技能"推""撞击"等）走 Unit.ApplyDisplacement，
//    会结算撞人/撞障碍物的碰撞伤害。同一件"被人推开"的事目前有两套判定。
//    如果策划希望统一，把基类 PushNeighborsAway 里的 PushBack 换成
//    ApplyDisplacement(DisplacementType.击退, distance, user) 即可，一处改全局生效。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/打嗝饮料
//  数值已在 Reset() 里预填，创建出来即可用，无需手填。
//  建议存放路径：Assets/Game/ScriptableObjects/药水/打嗝饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "打嗝饮料", menuName = "Game/Potion/打嗝饮料")]
public class Potion_打嗝饮料 : PotionData
{
    /// <summary>策划表里写死的击退格数</summary>
    private const int PushDistance = 1;

    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "打嗝饮料";
        description = "将所有相邻目标推远1格";
        rarity = PotionRarity.普通;

        // 效果只有一条：把周围 1 格内的单位推远 1 格
        // value 在这里的含义是"击退格数"，不是伤害也不是回复量
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.击退相邻目标,
            targetType = PotionTargetType.相邻目标,
            value = PushDistance
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyMode = PotionPenaltyMode.达到次数后每次触发;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;
        rewardCondition = PotionRewardCondition.使用后;

        devNotes = "策划表原文：效果「将所有相邻目标推远1格」，稀有度「普通」。";
    }
}
