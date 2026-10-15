using UnityEngine;
using System.Collections.Generic;

// ============================================================
//  特殊地形配置表（对应设计文档"特殊地形"一节）
//
//  TerrainData 是一个 ScriptableObject，每个 .asset 描述一种地形：
//  克勒妮绒毯 / 灼热地板 / 剧毒地板 / 烟雾地板 / 吸血地板。
//  实际生效由 TerrainSystem 统一在回合节点结算。
//
//  使用方式：
//  1. 在 Inspector 里新建几个 TerrainData 资产并填好数值；
//  2. 把这些资产拖到场景中 HexCell 组件的 terrain 字段上。
// ============================================================

/// <summary>地形类型</summary>
public enum TerrainType
{
    克勒妮绒毯,   // 特定单位站在上面时获得力量
    灼热地板,     // 回合结束给上面的单位上灼烧
    剧毒地板,     // 回合结束给上面的单位上中毒
    烟雾地板,     // 每隔若干回合喷烟，烟雾中的单位不能被远程选中
    吸血地板      // 回合结束对上面的单位造成真实伤害
}

[CreateAssetMenu(fileName = "NewTerrain", menuName = "Game/Terrain Data")]
public class TerrainData : ScriptableObject
{
    [Header("基础信息")]
    public string terrainName;
    public TerrainType terrainType;

    [TextArea] public string description;

    [Header("克勒妮绒毯")]
    [Tooltip("站在上面能获得力量的单位名字；留空表示所有单位都能获得")]
    public List<string> grantedUnitNames = new List<string>();
    [Tooltip("获得的力量层数（文档：1 点力量）")]
    public int strengthStacks = 1;
    [Tooltip("力量 buff 资产（会被挂给符合条件的单位）")]
    public BuffData strengthBuff;

    [Header("灼热地板 / 剧毒地板")]
    [Tooltip("回合结束时给上面单位施加的层数（文档：1 层）")]
    public int stacksPerTurnEnd = 1;
    [Tooltip("灼烧 buff 资产（灼热地板使用）")]
    public BuffData burnBuff;
    [Tooltip("中毒 buff 资产（剧毒地板使用）")]
    public BuffData poisonBuff;

    [Header("烟雾地板")]
    [Tooltip("每隔几回合切换一次烟雾状态（文档：每隔 1 回合）")]
    public int smokeInterval = 1;

    [Header("吸血地板")]
    [Tooltip("回合结束对上面单位造成的真实伤害（文档：1 点）")]
    public int trueDamagePerTurnEnd = 1;
}
