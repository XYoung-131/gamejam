using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  药水（饮料）配置表——对应设计文档"饮料（药水）"一节
//
//  规则回顾：
//  - 上限 3 个；战斗中玩家的行动阶段可以使用；
//  - 战斗后 50% 概率掉落 1 瓶，连着 2 关没掉落则第 3 关结束必定掉落；
//  - 掉落的品质 60% 普通 / 40% 稀有（具体概率在 LootSystem 里调）。
//
//  掉落与品质判定放在 LootSystem；这里只描述"这一瓶药水是什么效果"。
// ============================================================

/// <summary>药水品质</summary>
public enum PotionQuality
{
    普通,
    稀有
}

[CreateAssetMenu(fileName = "NewPotion", menuName = "Game/Potion Data")]
public class PotionData : ScriptableObject
{
    [Header("基础信息")]
    public string potionName;
    public PotionQuality quality;
    [TextArea] public string description;
    public Sprite icon;

    [Header("效果（策划填写）")]
    [Tooltip("饮用后恢复的生命值")]
    public int healAmount = 0;
    [Tooltip("饮用后挂给使用者的 buff（正面/负面都可，一般填正面的）")]
    public List<BuffData> grantedBuffs = new List<BuffData>();

    /// <summary>
    /// 结算这瓶药水（把效果作用到 target 身上）。
    /// </summary>
    public void Resolve(Unit target)
    {
        if (target == null) return;

        if (healAmount > 0)
        {
            target.Heal(healAmount);
            Debug.Log($"饮用【{potionName}】，恢复 {healAmount} 点生命");
        }

        foreach (var buff in grantedBuffs)
        {
            if (buff != null)
                target.ApplyBuff(buff);
        }

        EventManager.Trigger("PotionUsed", this, target);
    }
}
