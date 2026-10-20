using UnityEngine;
using System.Collections.Generic;

public enum UnitGrade
{
    小怪,
    精英怪,
    // 追加在末尾：Unity 按枚举序号序列化，新增不会影响已有的单位资产
    BOSS
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

    [Header("技能组（引用卡牌）")]
    public List<CardData> skillGroup1 = new List<CardData>();
    public List<CardData> skillGroup2 = new List<CardData>();
    public List<CardData> skillGroup3 = new List<CardData>();

    [Header("资源引用")]
    public Sprite icon;
    public GameObject prefab;
}
