using UnityEngine;

// ============================================================
//  【快走饮料】  稀有度：普通
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：免费使用1次快走
//    · 稀有度：普通
//    · 补充：（无）
//
//  ── "快走"是什么 ─────────────────────────────────────────
//  防御牌库里的位移技能（Assets/.../技能/防御技能/快走.asset）：
//  朝相邻格子移动、最多 2 格，正常要付 20 思绪 + 5 耐力。
//
//  ── 实现方式 ─────────────────────────────────────────────
//  走 PotionData 基类的「免费使用技能」分支 → GrantFreeSkill() →
//  PlayerCardSystem.GrantFreeCard(快走, 1, "快走饮料")。
//
//  为什么不直接自动放出来：
//    位移技能需要玩家自己挑落点（走 1 格还是 2 格、往哪个方向），
//    自动放会替玩家做决定。所以做成"免费卡"——
//    不占技能槽位、不用灌注，玩家点一下就用掉一次，
//    出牌时照样走射程 / 目标 / 视线校验，只是不扣思绪和耐力。
//
//  发放后的表现：
//    · PlayerCardSystem.FreeCards 里出现一条 { card = 快走, remainingUses = 1 }；
//    · 广播 "FreeCardChanged"，UI 拿它刷"免费技能"按钮；
//    · 用掉一次后计数归零，条目自动移除。
//
//  ⚠ 已知依赖：CardData.Resolve 目前只处理伤害 / 护盾 / 治疗 / buff，
//    "位移类卡牌"（带 CardTag.位移 的牌）的移动结算还没接
//    （需要先有"选落点 → TeleportTo/MoveTo"的流程）。
//    所以这瓶饮料的免费按钮现在会正常发出来、能点、也会扣掉次数，
//    但点下去不会真的把角色挪走 —— 等位移卡牌系统落地后自动生效，
//    本文件不需要再改。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/快走饮料
//  存放路径：Assets/Game/ScriptableObjects/饮料/快走饮料.asset
//  effects[0].skill 指向 技能/防御技能/快走.asset，脚本生成的资产已配好；
//  从 Create 菜单新建时 Reset() 填不了引用，需手动拖一次。
// ============================================================

[CreateAssetMenu(fileName = "快走饮料", menuName = "Game/Potion/快走饮料")]
public class Potion_快走饮料 : PotionData
{
    /// <summary>
    /// 策划分给这瓶饮料的免费次数。写死成常量是为了以后策划调值时有唯一出处，
    /// 也让 Reset() 与文档说明不会对不上。
    /// </summary>
    private const int FreeUses = 1;

    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "快走饮料";
        description = "免费使用1次快走";
        rarity = PotionRarity.普通;

        // 效果只有一条：发 1 次"快走"的免费使用次数
        // skill 是资产引用，Reset 里填不了；脚本生成的资产已指向 快走.asset，
        // 从 Create 菜单新建时需手动拖一次
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.免费使用技能,
            targetType = PotionTargetType.自身,
            value = FreeUses,
            skill = null,   // ← 资产引用：技能/防御技能/快走.asset（脚本生成的资产已配）
            buff = null
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「免费使用1次快走」，稀有度「普通」。" +
                   "effects[0].skill 已指向 技能/防御技能/快走.asset。" +
                   "另注：位移类卡牌的移动结算尚未实现，见 CardData.Resolve 的说明。";
    }
}
