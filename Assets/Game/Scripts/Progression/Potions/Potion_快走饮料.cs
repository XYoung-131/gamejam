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
//  ── 实现方式（事件接线，逻辑不写在本文件）──────────────────
//  PotionData 基类（队员版）的「免费使用技能」分支目前只广播事件：
//      EventManager.Trigger("FreeSkillGranted", user, effect.skill)
//  PlayerCardSystem 监听这个事件，调 GrantFreeCard(skill, 1, "快走饮料")
//  把"免费技能"额度塞进玩家的 FreeCards 列表：
//    · 不占技能槽位、不用灌注，玩家点一下就用掉一次；
//    · 出牌时照样走射程 / 目标 / 视线校验，只是不扣思绪和耐力；
//    · 广播 "FreeCardChanged"，UI 拿它刷"免费技能"按钮。
//
//  为什么不直接自动放出来：位移技能需要玩家自己挑落点
//  （走 1 格还是 2 格、往哪个方向），自动放会替玩家做决定。
//
//  ⚠ 已知依赖：CardData.Resolve 目前只处理伤害 / 护盾 / 治疗 / buff，
//    "位移类卡牌"（带 CardTag.位移 的牌）的移动结算还没接
//    （需要先有"选落点 → TeleportTo/MoveTo"的流程）。
//    所以免费按钮现在会正常发出来、能点、也会扣掉次数，
//    但点下去不会真的把角色挪走 —— 等位移卡牌系统落地后自动生效。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/快走饮料
//  存放路径：Assets/Game/ScriptableObjects/药水/快走饮料.asset
//  effects[0].skill 指向 技能/防御技能/快走.asset，药水/快走饮料.asset 已配好；
//  从 Create 菜单新建时 Reset() 填不了引用，需手动拖一次。
// ============================================================

[CreateAssetMenu(fileName = "快走饮料", menuName = "Game/Potion/快走饮料")]
public class Potion_快走饮料 : PotionData
{
    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "快走饮料";
        description = "免费使用1次快走";
        rarity = PotionRarity.普通;

        // 效果只有一条：发 1 次"快走"的免费使用次数
        // skill 是资产引用，Reset 里填不了；药水/快走饮料.asset 已指向 快走.asset，
        // 从 Create 菜单新建时需手动拖一次
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.免费使用技能,
            targetType = PotionTargetType.自身,
            value = 0,      // 免费次数固定 1 次，由 PlayerCardSystem 的监听方决定
            skill = null,   // ← 资产引用：技能/防御技能/快走.asset（药水/快走饮料.asset 已配）
            buff = null
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyMode = PotionPenaltyMode.达到次数后每次触发;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;
        rewardCondition = PotionRewardCondition.使用后;

        devNotes = "策划表原文：效果「免费使用1次快走」，稀有度「普通」。" +
                   "effects[0].skill 已指向 技能/防御技能/快走.asset。" +
                   "另注：位移类卡牌的移动结算尚未实现，见 CardData.Resolve 的说明。";
    }
}
