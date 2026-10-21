using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Passive_大抱抱", menuName = "Game/Passive Abilities/大抱抱")]
public sealed class BigHugPassiveAbilityData : PassiveAbilityData
{
    public UnitData smallHugData;

    [Min(1)]
    [Tooltip("每损失多少百分比最大生命生成一只小抱抱")]
    public int healthLossPercentPerSpawn = 25;

    [Min(1)]
    [Tooltip("生成位置与大抱抱的最大格子距离")]
    public int spawnRadius = 2;

    public override PassiveRuntime CreateRuntime(Unit owner)
    {
        return new BigHugPassiveRuntime(this, owner);
    }
}

public sealed class BigHugPassiveRuntime : PassiveRuntime
{
    private readonly BigHugPassiveAbilityData config;
    private int spawnedHealthMilestones;

    public BigHugPassiveRuntime(BigHugPassiveAbilityData data, Unit owner) : base(data, owner)
    {
        config = data;
    }

    public override void OnActivated()
    {
        spawnedHealthMilestones = GetCrossedHealthMilestones();
    }

    public override void OnOwnerDamaged(Unit source, int healthDamage)
    {
        int crossed = GetCrossedHealthMilestones();
        int spawnCount = Mathf.Max(0, crossed - spawnedHealthMilestones);
        spawnedHealthMilestones = Mathf.Max(spawnedHealthMilestones, crossed);
        for (int i = 0; i < spawnCount; i++) SpawnSmallHug();
    }

    public override void OnOwnerDied(Unit killer)
    {
        // 正常伤害致死会在 OnOwnerDamaged 中跨过最后一个 25% 阶段；
        // 直接死亡效果没有伤害事件，因此在这里保证死亡至少生成一只。
        int totalMilestones = GetTotalMilestones();
        if (spawnedHealthMilestones >= totalMilestones) return;
        spawnedHealthMilestones++;
        SpawnSmallHug();
    }

    private int GetCrossedHealthMilestones()
    {
        int maximum = Mathf.Max(1, Owner.GetMaxHealth());
        int lost = Mathf.Clamp(maximum - Owner.GetCurrentHealth(), 0, maximum);
        return Mathf.Clamp(lost * GetTotalMilestones() / maximum, 0, GetTotalMilestones());
    }

    private int GetTotalMilestones()
    {
        return Mathf.Max(1, Mathf.CeilToInt(100f / Mathf.Max(1, config.healthLossPercentPerSpawn)));
    }

    private void SpawnSmallHug()
    {
        if (config.smallHugData == null || config.smallHugData.prefab == null ||
            HexGrid.Instance == null) return;

        var candidates = new List<HexCell>();
        foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(Owner.GetCoord(),
                     Mathf.Max(1, config.spawnRadius)))
        {
            if (cell == null || !cell.IsEmpty()) continue;
            int distance = Owner.GetCoord().Distance(cell.coord);
            if (distance >= 1 && distance <= config.spawnRadius) candidates.Add(cell);
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("大抱抱触发了生成小抱抱，但相邻2格内没有可用空格");
            return;
        }

        HexCell destination = candidates[Random.Range(0, candidates.Count)];
        GameObject instance = Object.Instantiate(config.smallHugData.prefab,
            HexGrid.Instance.HexToWorld(destination.coord), Quaternion.identity);
        instance.name = "Enemy_" + config.smallHugData.unitName + "_" + destination.coord;
        EnemyUnit enemy = instance.GetComponent<EnemyUnit>();
        if (enemy == null) enemy = instance.AddComponent<EnemyUnit>();
        enemy.Initialize(config.smallHugData, destination.coord);

        LevelManager manager = Object.FindObjectOfType<LevelManager>();
        if (manager != null) manager.RegisterSummonedEnemy(enemy);
    }
}
