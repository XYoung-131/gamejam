using UnityEngine;

[CreateAssetMenu(fileName = "Passive_无法被击退", menuName = "Game/Passive Abilities/无法被击退")]
public sealed class KnockbackImmunityPassiveData : PassiveAbilityData
{
    public override PassiveRuntime CreateRuntime(Unit owner)
    {
        return new KnockbackImmunityRuntime(this, owner);
    }
}

public sealed class KnockbackImmunityRuntime : PassiveRuntime
{
    public KnockbackImmunityRuntime(PassiveAbilityData data, Unit owner) : base(data, owner) { }

    public override bool CanBeDisplaced(DisplacementType type)
    {
        return type != DisplacementType.击退;
    }
}
