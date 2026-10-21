using UnityEngine;

/// <summary>
/// 被动能力的共享静态配置。资产中只保存配置，不保存任何战斗状态。
/// 具体被动可以继承此类，并覆写 CreateRuntime 创建自己的运行时类型。
/// </summary>
[CreateAssetMenu(fileName = "NewPassiveAbility", menuName = "Game/Passive Ability Data")]
public class PassiveAbilityData : ScriptableObject
{
    [Header("基础信息")]
    public string abilityName;
    [TextArea] public string description;

    public virtual PassiveRuntime CreateRuntime(Unit owner)
    {
        return new PassiveRuntime(this, owner);
    }
}
