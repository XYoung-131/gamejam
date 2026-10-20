using UnityEngine;

/// <summary>
/// 运行时创建的战斗 UI 所需素材。
/// 资产放在 Resources 中，使正式 Build 也能加载这些引用。
/// </summary>
[CreateAssetMenu(fileName = "BattleUIAssetCatalog", menuName = "Game/Battle UI Asset Catalog")]
public class BattleUIAssetCatalog : ScriptableObject
{
    [Header("通用")]
    public Sprite statusBarSprite;
    public Sprite hpSlotSprite;
    public Sprite staminaSlotSprite;
    public Sprite potionSlotSprite;

    [Header("事件界面")]
    public Sprite eventFrameSprite;

    [Header("战斗结束界面")]
    public Sprite rewardFrameSprite;
    public Sprite rewardTitleBarSprite;
    public Sprite buttonSprite;
    public Sprite buttonHoverSprite;
    public Sprite arrowSprite;
    public Sprite arrowHoverSprite;
    public Sprite emptyCardFaceSprite;
    public Sprite attackCardFaceSprite;
    public Sprite defenseCardFaceSprite;
}
