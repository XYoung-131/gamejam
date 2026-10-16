using UnityEngine;

// ============================================================
//  【蓝莓饮料】  稀有度：稀有
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：获得3层开悟
//    · 稀有度：稀有
//
//  ── 实现方式 ─────────────────────────────────────────────
//  走 PotionData 基类的数据驱动分支，效果写在 effects 列表里，
//  子类只负责预填数值，不重复实现结算。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/蓝莓饮料
//  存放路径：Assets/Game/ScriptableObjects/饮料/蓝莓饮料.asset
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

        // 效果只有一条：给自己挂 3 层开悟。
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.施加Buff,
            targetType = PotionTargetType.自身,
            value = 3,
            buff = null   // 编辑器里把「开悟.asset」拖进来
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「获得3层开悟」，稀有度「稀有」。" +
                   "开悟本体规格策划表里还没给，先按占位 buff 处理（canStack = true / maxStack = 99，" +
                   "所以 3 层能叠上）。";
    }
}
