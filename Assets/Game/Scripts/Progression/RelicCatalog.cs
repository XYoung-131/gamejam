using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 完整遗物目录。负责让随机掉落池以外的事件遗物也进入正式 Build，
/// 并供按名称发放遗物和存档恢复使用。
/// </summary>
[CreateAssetMenu(fileName = "RelicCatalog", menuName = "Game/Relic Catalog")]
public class RelicCatalog : ScriptableObject
{
    public List<RelicData> relics = new List<RelicData>();
}
