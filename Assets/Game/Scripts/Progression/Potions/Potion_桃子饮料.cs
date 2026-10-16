using UnityEngine;

// ============================================================
//  【桃子饮料】  稀有度：稀有
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：获得3层轻盈
//    · 稀有度：稀有
//
//  ── 实现方式 ─────────────────────────────────────────────
//  走 PotionData 基类的数据驱动分支，效果写在 effects 列表里，
//  子类只负责预填数值，不重复实现结算。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/桃子饮料
//  存放路径：Assets/Game/ScriptableObjects/饮料/桃子饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "桃子饮料", menuName = "Game/Potion/桃子饮料")]
public class Potion_桃子饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "桃子饮料";
        description = "获得3层轻盈";
        rarity = PotionRarity.稀有;

        // 效果只有一条：给自己挂 3 层轻盈。
        // 层数写在 value 里，由基类 GetBuffStacks 传给 Unit.ApplyBuff。
        // buff 是资产引用（Reset 填不了），饮料/桃子饮料.asset 里已配好「轻盈.asset」。
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.施加Buff,
            targetType = PotionTargetType.自身,
            value = 3,
            buff = null   // ← 资产引用：饮料/桃子饮料.asset 已指向 轻盈.asset
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「获得3层轻盈」，稀有度「稀有」。" +
                   "轻盈本体规格策划表里还没给，先按占位 buff 处理（canStack = true / maxStack = 99，" +
                   "所以 3 层能叠上）。";
    }
}
