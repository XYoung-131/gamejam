using UnityEngine;
using System.Collections.Generic;

public enum UnitGrade
{
    小怪,
    精英怪,
    // 追加在末尾：Unity 按枚举序号序列化，新增不会影响已有的单位资产
    BOSS
}

[System.Serializable]
public class CardGroup
{
    [Header("技能组名称")]
    public string groupName;
    
    [Header("技能卡组")]
    public List<CardData>  cards = new List<CardData>();
}

[CreateAssetMenu(fileName = "NewUnit", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    [Header("基础信息")]
    public string unitName;
    public int maxHealth;
    public UnitGrade grade;

    [Tooltip("基础攻击力：普通攻击 / 没单独填伤害的卡牌用它。\n默认 1 = 和旧版硬编码的 1 完全一致，老资产不填也不会变成 0 伤害")]
    public int attack = 1;

    [Header("能力")]
    [TextArea] public string passiveAbility;
    [TextArea] public string aiLogic;
    
    [Header("技能组列表")]
    public List<CardGroup> skillGroups = new List<CardGroup>();

    [Header("资源引用")]
    public Sprite icon;
    public GameObject prefab;
}
