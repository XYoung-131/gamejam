using UnityEngine;

[CreateAssetMenu(fileName = "Passive_科鲁巴格", menuName = "Game/Passive Abilities/科鲁巴格")]
public sealed class KorubagPassiveAbilityData : PassiveAbilityData
{
    [Min(0)] public int initialShield = 3;
    [Min(1)] public int strengthRound = 3;
    public BuffData strengthBuff;

    public override PassiveRuntime CreateRuntime(Unit owner)
    {
        return new KorubagPassiveRuntime(this, owner);
    }
}

public sealed class KorubagPassiveRuntime : PassiveRuntime
{
    private readonly KorubagPassiveAbilityData config;
    private bool strengthGranted;

    public KorubagPassiveRuntime(KorubagPassiveAbilityData data, Unit owner) : base(data, owner)
    {
        config = data;
    }

    public override void OnActivated()
    {
        Owner.AddShield(Mathf.Max(0, config.initialShield));
    }

    public override void OnRoundStarted(int round)
    {
        if (strengthGranted || round < Mathf.Max(1, config.strengthRound) ||
            config.strengthBuff == null) return;
        strengthGranted = true;
        Owner.ApplyBuff(config.strengthBuff, 1);
    }
}
