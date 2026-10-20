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

    [Tooltip("玩家受到的爆炸伤害。小于 0 时使用 explosionDamage")]
    public int explosionDamageToPlayer = -1;
    [Tooltip("敌人受到的爆炸伤害。小于 0 时使用 explosionDamage")]
    public int explosionDamageToEnemies = -1;

    [Header("范围 Buff（每个玩家回合开始时施加）")]
    public BuffData areaBuff;
    [Min(0)] public int areaBuffRadius = 0;
    [Min(1)] public int areaBuffStacks = 1;

    [Header("定时触发（每个完整回合结束时计数）")]
    [Min(0)] public int triggerEveryRounds = 0;
    [Tooltip("定时触发时，在随机相邻空格生成的单位")]
    public UnitData periodicSpawnUnit;
    [Min(0)] public int healthCostAfterSpawn = 0;
    [Tooltip("达到定时回合后摧毁自身，并在原地生成该单位")]
    public UnitData timedDestroySpawnUnit;

    [Header("碰撞")]
    [Min(0)] public int collisionDamageBonus = 0;

    [Header("相关Buff")]
    [Tooltip("旧字段，仅为兼容已有资产保留；新的障碍物 Buff 请使用 areaBuff")]
    public List<CardData> buffs = new List<CardData>();

    [Header("资源引用")]
    public Sprite icon;
    public GameObject prefab;
}
