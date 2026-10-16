using UnityEngine;

// ============================================================
//  【混合饮料】  稀有度：普通
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：回复10点耐力和10点思绪
//    · 稀有度：普通
//    · 补充：（无）
//
//  ── 实现方式 ─────────────────────────────────────────────
//  一个 PotionData 可以挂多条 effect，这瓶就是两条：
//
//    effects[0] 回复耐力 自身 10   → PlayerUnit.RestoreStamina(10)
//    effects[1] 回复思绪 自身 10   → PlayerUnit.RestoreThought(10)
//
//  Resolve() 会按 List 顺序依次结算两条，互不干扰，
//  两条都各自被上限截断（耐力/思绪上限默认 40）。
//
//  数值取向：总量 20，是「耐力饮料」「提神饮料」（各 20 单资源）的
//  "总量不变、拆成两份"版本 —— 更灵活但单项上限更低，
//  只剩 5 点耐力的场景下，混合饮料只能补到 15，耐力饮料能补到 25。
//  配掉落权重时可以按这个差别区分。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/混合饮料
//  建议存放路径：Assets/Game/ScriptableObjects/饮料/混合饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "混合饮料", menuName = "Game/Potion/混合饮料")]
public class Potion_混合饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "混合饮料";
        description = "回复10点耐力和10点思绪";
        rarity = PotionRarity.普通;

        // 两条效果：先回耐力，再回思绪（顺序不影响结果，但保持和策划表一致）
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.回复耐力,
            targetType = PotionTargetType.自身,
            value = 10
        });
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.回复思绪,
            targetType = PotionTargetType.自身,
            value = 10
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「回复10点耐力和10点思绪」，稀有度「普通」。" +
                   "和「劣质混合饮料」是同一套效果，数值更高且没有副作用。";
    }
}
