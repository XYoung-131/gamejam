using UnityEngine;

// ============================================================
//  【劣质混合饮料】  稀有度：特殊
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：回复8点耐力和8点思绪
//            （在同1场战斗中第3次使用后扣除所有耐力和思绪，
//              并眩晕1回合，战斗结束掉落遗物 饮料成瘾）
//    · 稀有度：特殊
//    · 补充：括号里面的内容玩家看不到，但是会实装
//
//  → 所以 description（玩家看得见的说明）只写"回复8点耐力和8点思绪"，
//    括号里的副作用写在 devNotes 里，靠玩家自己踩坑发现。
//
//  ── 为什么它是"特殊"稀有度 ───────────────────────────────
//  它是全表唯一带"战斗内使用次数惩罚"的饮料：
//  前两瓶是干净的小补给（8/8，比混合饮料的 10/10 少），
//  从第 3 瓶起每次都是"先给 8/8、再全部扣光 + 眩晕一回合"，
//  净收益为负。奖励则是战斗结束白送一件遗物【饮料成瘾】。
//
//  ── 实现方式（全部走基类，本文件只负责配数值）────────────
//
//    第 1 步  effects 依次结算 → 回 8 耐力、回 8 思绪
//    第 2 步  ShouldTriggerPenalty(本场第 N 瓶) 判定 N >= penaltyAfterUses(3)
//             成立则 ApplyPenalty：
//               ① penaltyDrainResources  → PlayerUnit.DrainAllResources()（耐力思绪清零）
//               ② penaltyBuff            → ApplyBuff(眩晕)（眩晕 duration = 1 回合）
//               ③ rewardRelic            → LootSystem.QueueBattleEndRelic(饮料成瘾)
//    第 3 步  OnPotionResolved（本类覆写，只打日志，给 QA 看"这是第几瓶"）
//
//  ⚠ 顺序是"先补给、后扣光"——这是策划明确要求的（第三瓶等于白喝 + 全清）。
//    基类 Resolve 已经把惩罚放在效果之后，不要调整成反过来。
//
//  ⚠ 从第 3 瓶起，第 4、5……瓶会继续触发惩罚（判定用的是 >=，不是 ==）。
//    策划原文"第3次使用后"按"第 3 次及以后"理解。
//
//  ── 依赖的资产（生成脚本已直接配好引用）────────────────────
//    ① penaltyBuff   ← Buff/眩晕.asset        ✓ 已配
//    ② rewardRelic   ← 遗物/饮料成瘾.asset     ✓ 已配
//  从 Create 菜单新建资产时 Reset() 填不了引用，这两处要手动拖。
//  脚本生成的资产（饮料/劣质混合饮料.asset）引用已配好，"眩晕"与"白送遗物"都能生效。
//
//  ── 使用次数从哪来 ───────────────────────────────────────
//    PlayerUnit.UsePotion 里维护了一张"本场战斗每瓶饮料各喝了几次"的表，
//    每次喝之前 +1 再传给 Resolve(user, usedCount)；
//    战斗开始时（Initialize）清空，所以跨战斗不会串味。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/劣质混合饮料
//  建议存放路径：Assets/Game/ScriptableObjects/饮料/劣质混合饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "劣质混合饮料", menuName = "Game/Potion/劣质混合饮料")]
public class Potion_劣质混合饮料 : PotionData
{
    /// <summary>策划表里写死的触发阈值：同 1 场战斗第 3 次使用后</summary>
    private const int 副作用触发次数 = 3;

    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "劣质混合饮料";

        // 玩家看得见的只有补给部分，副作用是藏起来的
        description = "回复8点耐力和8点思绪";
        rarity = PotionRarity.特殊;

        // ---- 正面效果：回 8 耐力 + 8 思绪 ----
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.回复耐力,
            targetType = PotionTargetType.自身,
            value = 8
        });
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.回复思绪,
            targetType = PotionTargetType.自身,
            value = 8
        });

        // ---- 副作用：本场第 3 瓶起扣光资源 + 眩晕 ----
        penaltyAfterUses = 副作用触发次数;
        penaltyDrainResources = true;
        // penaltyBuff / rewardRelic 是资产引用，Reset 里填不了，
        // 已在 饮料/劣质混合饮料.asset 里拖好：
        //   penaltyBuff  ← Buff/眩晕.asset
        //   rewardRelic  ← 遗物/饮料成瘾.asset

        maxUsesPerBattle = 0;   // 不限次数（惩罚本身就限制了玩家不会多喝）

        devNotes = "策划表原文：效果「回复8点耐力和8点思绪（在同1场战斗中第3次使用后" +
                   "扣除所有耐力和思绪，并眩晕1回合，战斗结束掉落遗物 饮料成瘾）」，" +
                   "稀有度「特殊」。表格补充列注明：括号里面的内容玩家看不到，但是会实装。" +
                   "penaltyBuff / rewardRelic 已分别指向 Buff/眩晕.asset 与 遗物/饮料成瘾.asset。";
    }

    /// <summary>
    /// 结算完成后的收尾：只打日志，方便 QA / 策划在 Console 里确认
    /// "这是本场第几瓶、副作用有没有触发"。
    ///
    /// 副作用本身（扣光资源、眩晕、登记遗物）已经在基类 ApplyPenalty 里做完了，
    /// 这里不要重复扣一次，否则会多扣。
    /// </summary>
    protected override void OnPotionResolved(Unit user, int usedCountThisBattle)
    {
        if (!HasPenalty) return;

        int bottlesLeft = penaltyAfterUses - usedCountThisBattle;

        if (bottlesLeft > 0)
        {
            Debug.Log($"【{potionName}】本场第 {usedCountThisBattle} 瓶，还没到副作用：" +
                      $"再喝 {bottlesLeft} 瓶就会触发");
            return;
        }

        string relicName = rewardRelic != null ? rewardRelic.relicName : "未配置";
        Debug.Log($"【{potionName}】本场第 {usedCountThisBattle} 瓶，副作用已触发：" +
                  $"耐力与思绪清零、眩晕 1 回合，战斗结束时将获得遗物【{relicName}】");
    }
}
