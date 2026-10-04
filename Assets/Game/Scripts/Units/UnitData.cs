using UnityEngine;
using System.Collections.Generic;

public enum UnitGrade
{
    小怪,
    精英怪
}

[CreateAssetMenu(fileName = "NewUnit", menuName = "Game/Unit Data")]
public class UnitData : ScriptableObject
{
    [Header("基础信息")]
    public string unitName;
    public int maxHealth;
    public UnitGrade grade;

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
