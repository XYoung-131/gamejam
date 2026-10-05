using System.Collections.Generic;
using UnityEngine;

public enum BuffPolarity
{
    正面,
    负面
}

public enum BuffTrigger
{
    立即生效,
    回合开始,
    回合结束,
    受到伤害时,
    攻击时,
    移动时,
    死亡时
}

public enum BuffEffectType
{
    // 属性类
    耐力回复倍率,
    思绪回复倍率,
    移动距离加成,
    移动耐力消耗倍率,
    攻击伤害加成,
    攻击耐力消耗倍率,
    受到伤害倍率,
    受到伤害固定减免,

    // 持续伤害
    固定持续伤害,

    // 控制
    禁止行动,

    // 特殊效果
    清除所有负面,
    免疫负面,
    免疫伤害,
    免疫死亡,
    回合结束死亡
}

[System.Serializable]
public class BuffEffect
{
    [Header("效果类型")]
    public BuffEffectType effectType;

    [Header("触发时机")]
    public BuffTrigger trigger;

    [Header("数值")]
    public float value;

    [Header("是否百分比")]
    public bool isPercent;
}

[CreateAssetMenu(fileName = "NewBuff", menuName = "Game/Buff Data")]
public class BuffData : ScriptableObject
{
    [Header("基础信息")]
    public string buffName;

    [TextArea]
    public string description;

    public Sprite icon;


    [Header("分类")]
    public BuffPolarity polarity;


    [Header("持续与叠加")]
    public int duration = -1;

    public int maxStack = 1;

    public bool canStack = false;


    [Header("效果")]
    public List<BuffEffect> effects = new List<BuffEffect>();


    [Header("内部备注")]
    [TextArea]
    public string devNotes;
}