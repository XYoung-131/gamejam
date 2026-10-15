using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  思绪碎片（遗物）配置表——对应设计文档"思绪碎片（遗物）"一节
//
//  规则回顾：
//  - 拿了就永久生效，或者即时生效；
//  - 通过事件、商店、精英和 BOSS 战掉落；
//  - 遗物有各自的掉落池；精英/BOSS 有专属掉落，击败后才掉，
//    掉落概率是同类遗物的 2 倍（概率权重在 LootSystem 里调）。
//
//  这里只描述遗物本身的效果；掉落池/权重放 LootSystem。
// ============================================================

/// <summary>遗物掉落池</summary>
public enum RelicPool
{
    通用,   // 事件 / 商店 / 普通掉落
    精英,   // 精英战专属掉落
    BOSS    // BOSS 战专属掉落
}

[CreateAssetMenu(fileName = "NewRelic", menuName = "Game/Relic Data")]
public class RelicData : ScriptableObject
{
    [Header("基础信息")]
    public string relicName;
    [TextArea] public string description;
    public RelicPool pool = RelicPool.通用;
    public Sprite icon;

    [Header("永久效果（拿了就一直生效，不会随回合减少）")]
    [Tooltip("拾取时挂给持有者的永久 buff")]
    public List<BuffData> permanentBuffs = new List<BuffData>();

    [Header("即时效果（拾取时结算一次）")]
    [Tooltip("勾选后这个遗物是一次性效果，不进永久 buff 列表")]
    public bool instantEffect = false;
    [Tooltip("即时恢复的生命值")]
    public int instantHeal = 0;
    [Tooltip("即时挂上的一次性 buff")]
    public List<BuffData> instantBuffs = new List<BuffData>();

    /// <summary>
    /// 拾取时把效果作用到持有者身上。
    /// </summary>
    public void Apply(Unit holder)
    {
        if (holder == null) return;

        foreach (var buff in permanentBuffs)
        {
            // 用 ApplyPermanentBuff 而不是 ApplyBuff：遗物是"拿了就一直生效"，
            // 不能依赖策划在 BuffData 资产上把 duration 填成 -1
            if (buff != null)
                holder.ApplyPermanentBuff(buff);
        }

        if (instantEffect)
        {
            if (instantHeal > 0)
                holder.Heal(instantHeal);

            foreach (var buff in instantBuffs)
            {
                if (buff != null)
                    holder.ApplyBuff(buff);
            }
        }

        Debug.Log($"{holder.data?.unitName} 获得遗物【{relicName}】");
        EventManager.Trigger("RelicAcquired", this, holder);
    }
}
