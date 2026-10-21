using UnityEngine;

[CreateAssetMenu(fileName = "Passive_浮空", menuName = "Game/Passive Abilities/浮空")]
public sealed class TerrainImmunityPassiveData : PassiveAbilityData
{
    public override PassiveRuntime CreateRuntime(Unit owner)
    {
        return new TerrainImmunityPassiveRuntime(this, owner);
    }
}

public sealed class TerrainImmunityPassiveRuntime : PassiveRuntime
{
    public TerrainImmunityPassiveRuntime(PassiveAbilityData data, Unit owner) : base(data, owner) { }

    public override bool CanBeAffectedByTerrain(TerrainData terrain)
    {
        return false;
    }
}
