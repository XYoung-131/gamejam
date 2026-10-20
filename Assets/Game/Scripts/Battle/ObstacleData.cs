using UnityEngine;
using System.Collections.Generic;

public enum ObstacleTag
{
    环境,
    高大
}

[CreateAssetMenu(fileName = "NewObstacle", menuName = "Game/Obstacle Data")]
public class ObstacleData : ScriptableObject
{
    [Header("基础信息")]
    public string obstacleName;
    public int maxHealth = -1;
    public List<ObstacleTag> tags = new List<ObstacleTag>();

    [Header("能力")]
    [TextArea] public string specialAbility;
    
    [Header("相关Buff")]
    public List<CardData> buffs = new List<CardData>();

    [Header("资源引用")]
    public Sprite icon;
    public GameObject prefab;
}
