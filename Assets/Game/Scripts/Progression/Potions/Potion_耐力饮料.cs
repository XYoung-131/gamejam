using UnityEngine;

// ============================================================
//  【耐力饮料】  稀有度：普通
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：回复20点耐力
//    · 稀有度：普通
//    · 补充：（无）
//
//  ── 实现方式 ─────────────────────────────────────────────
//  走 PotionData 基类的「回复耐力」分支，内部调用
//  PlayerUnit.RestoreStamina(20)：
//
//    · 耐力 = 行动资源，移动和出牌共用同一个池子（currentStamina）；
//    · 回复量会被 maxStamina 截断（默认 40），和策划"上限 40"一致；
//    · 回复后广播 PlayerResourceChanged，UI 资源条自动刷新；
//    · 不会回复思绪、不影响生命，是纯粹的资源补给。
//
//  ── 与「力竭」的交互（待策划确认）────────────────────────
//  策划只写了「力竭：耐力**回复**减半」，没区分"回合回复"和"道具回复"。
//  当前实现里力竭只作用于回合结束的 15 点回复（PlayerUnit.OnTurnEnd），
//  喝饮料的即时回复不受影响。若想让它也减半，需在 PotionData 的
//  回复耐力分支里乘一次 GetTotalEffectValue(BuffEffectType.耐力回复倍率)。
//  → 已记入项目待办，等策划拍板后统一改，不要两处各改一半。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/耐力饮料
//  数值已在 Reset() 里预填，创建出来即可用，无需手填任何字段。
//  建议存放路径：Assets/Game/ScriptableObjects/饮料/耐力饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "耐力饮料", menuName = "Game/Potion/耐力饮料")]
public class Potion_耐力饮料 : PotionData
{
    /// <summary>
    /// Unity 在"新建这个资产"或 Inspector 里点 Reset 时调用。
    /// 作用：把策划表里的数值预先填好，省掉手填一遍的出错机会。
    /// 只影响新建/重置，不会去覆盖已经手改过的美术字段（icon 等）。
    /// </summary>
    private void Reset()
    {
        potionName = "耐力饮料";
        description = "回复20点耐力";
        rarity = PotionRarity.普通;

        // 效果只有一条：给自己回 20 点耐力
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.回复耐力,
            targetType = PotionTargetType.自身,
            value = 20
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        // 没有副作用机制
        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「回复20点耐力」，稀有度「普通」。";
    }
}
