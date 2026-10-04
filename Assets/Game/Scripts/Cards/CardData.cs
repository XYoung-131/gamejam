using UnityEngine;
using System.Collections.Generic;

public enum Rarity
{
    普通,
    固有,
    稀有,
    敌方特有
}

public enum CardTag
{
    近战,
    远程,
    位移,
    防御,
    特技,
    传送,
    需要瞄准,
    需要准备
}

[CreateAssetMenu(fileName = "NewCard", menuName = "Game/Card Data")]
public class CardData : ScriptableObject
{
    [Header("基础信息")]
    public string cardName;
    [TextArea] public string description;

    [Header("消耗")]
    public int thoughtCost;
    public int actionCost;
    public int maxUses = -1;

    [Header("分类")]
    public Rarity rarity;
    public List<CardTag> tags = new List<CardTag>();

    [Header("内部备注")]
    [TextArea] public string devNotes;
}
