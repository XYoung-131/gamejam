using UnityEngine;
using System.Collections.Generic;

// ============================================================
//  特殊地形配置表（对应设计文档"特殊地形"一节）
//
//  TerrainData 是一个 ScriptableObject，每个 .asset 描述一种地形，
//  资产放在 ScriptableObjects/地形/ 下，共 6 种：
//
//   克勒妮绒毯  神庙鼠 / 克勒妮之子 / 克勒妮之盾 位于上面时，获得 1 点力量
//   灼热地板    回合结束，对上面的所有单位施加 1 层灼烧
//   剧毒地板    回合结束，对上面的所有单位施加 1 层中毒
//   刀刃地板    回合结束，对上面的所有单位施加 1 层流血
//   烟雾地板    初始状态随机；每隔 1 回合喷出烟雾，
//               位于烟雾中的单位不能被选为远程攻击的目标
//   吸血地板    回合结束，对上面的所有单位造成 1 点真实伤害
//
//  实际生效由 TerrainSystem 统一在回合节点结算。
//
//  使用方式：
//  1. 把这些资产拖到场景中 HexCell 组件的 terrain 字段上；
//  2. buff 类地形必须把对应的 BuffData 资产拖进 burnBuff / poisonBuff /
//     bleedBuff 字段，否则该地板不生效（TerrainSystem 会跳过空引用）。
// ============================================================

/// <summary>地形类型</summary>
/// 注意：新成员一律追加在末尾——Unity 按序号序列化，
/// 插在中间会把已有资产的地形类型串掉（和 UnitGrade 同理）。
public enum TerrainType
{
    克勒妮绒毯,   // 特定单位站在上面时获得力量
    灼热地板,     // 回合结束给上面的单位上灼烧
    剧毒地板,     // 回合结束给上面的单位上中毒
    烟雾地板,     // 每隔若干回合喷烟，烟雾中的单位不能被远程选中
    吸血地板,     // 回合结束对上面的单位造成真实伤害
    刀刃地板      // 回合结束给上面的单位上流血
}

[CreateAssetMenu(fileName = "NewTerrain", menuName = "Game/Terrain Data")]
public class TerrainData : ScriptableObject
{
    [Header("基础信息")]
    public string terrainName;
    public TerrainType terrainType;

    [TextArea] public string description;

    [Header("克勒妮绒毯")]
    [Tooltip("站在上面能获得力量的单位（神庙鼠 / 克勒妮之子 / 克勒妮之盾）；留空表示所有单位都能获得")]
    public List<UnitData> grantedUnits = new List<UnitData>();
    [Tooltip("获得的力量层数（文档：1 点力量）")]
    public int strengthStacks = 1;
    [Tooltip("力量 buff 资产（会被挂给符合条件的单位）")]
    public BuffData strengthBuff;

    [Header("灼热地板 / 剧毒地板 / 刀刃地板")]
    [Tooltip("回合结束时给上面单位施加的层数（文档：1 层）")]
    public int stacksPerTurnEnd = 1;
    [Tooltip("灼烧 buff 资产（灼热地板使用）")]
    public BuffData burnBuff;
    [Tooltip("中毒 buff 资产（剧毒地板使用）")]
    public BuffData poisonBuff;
    [Tooltip("流血 buff 资产（刀刃地板使用）")]
    public BuffData bleedBuff;

    [Header("烟雾地板")]
    [Tooltip("每隔几回合切换一次烟雾状态（文档：每隔 1 回合）")]
    public int smokeInterval = 1;

    [Header("吸血地板")]
    [Tooltip("回合结束对上面单位造成的真实伤害（文档：1 点）")]
    public int trueDamagePerTurnEnd = 1;
}
