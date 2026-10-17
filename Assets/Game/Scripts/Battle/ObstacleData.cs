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

    [Header("爆炸（被摧毁时对周围单位造成伤害，如炸药桶）")]
    [Tooltip("勾选后被摧毁时会爆炸")]
    public bool isExplosion = false;
    [Tooltip("爆炸造成的伤害值")]
    public int explosionDamage = 0;
    [Tooltip("爆炸波及的格数（以自身为中心）")]
    public int explosionRadius = 1;
    [Tooltip("爆炸的伤害类型（默认物理）")]
    public DamageType explosionDamageType = DamageType.物理;

    [Header("相关Buff")]
    public List<CardData> buffs = new List<CardData>();

    [Header("资源引用")]
    public Sprite icon;
    public GameObject prefab;
}
