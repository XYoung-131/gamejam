using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════
//  事件物品库：按中文名找遗物 / 药水资产，以及随机抽一瓶饮料
//
//  为什么需要它：
//  事件文案来自飞书「游戏事件」表，里面写的是物品中文名
//  （"获得 解压垃圾""获得 药水腰带"），没有资产引用。
//  具体事件脚本结算时要把名字换成真正的 ScriptableObject 资产。
//
//  查找顺序（先工程，后掉落池）：
//    ① 编辑器下扫工程里所有 t:RelicData / t:PotionData；
//    ② 再并上 LootSystem 的三个遗物池 / 两个药水池（去重）。
//  两边都没有 = 这件物品的资产还没建，返回 null，调用方自己兜底。
// ═══════════════════════════════════════════════════════════════
public static class EventItemLibrary
{
    private static List<RelicData> relics;
    private static List<PotionData> potions;

    /// <summary>工程里能找到的全部遗物（只读）</summary>
    public static IReadOnlyList<RelicData> AllRelics
    {
        get { EnsureLoaded(); return relics; }
    }

    /// <summary>工程里能找到的全部药水（只读）</summary>
    public static IReadOnlyList<PotionData> AllPotions
    {
        get { EnsureLoaded(); return potions; }
    }

    /// <summary>按名称找遗物（找不到返回 null）</summary>
    public static RelicData FindRelic(string relicName)
    {
        if (string.IsNullOrEmpty(relicName)) return null;
        EnsureLoaded();
        foreach (RelicData relic in relics)
            if (relic != null && relic.relicName == relicName) return relic;
        return null;
    }

    /// <summary>按名称找药水（找不到返回 null）</summary>
    public static PotionData FindPotion(string potionName)
    {
        if (string.IsNullOrEmpty(potionName)) return null;
        EnsureLoaded();
        foreach (PotionData potion in potions)
            if (potion != null && potion.potionName == potionName) return potion;
        return null;
    }

    /// <summary>随机抽一瓶饮料（池子为空时返回 null）</summary>
    public static PotionData RandomPotion()
    {
        EnsureLoaded();
        if (potions.Count == 0) return null;
        return potions[Random.Range(0, potions.Count)];
    }

    /// <summary>
    /// 建一件"占位遗物"——真资产还没建时用，让事件不至于白扣属性。
    /// 只活在本次运行内存里；真正资产建好后（同名）FindRelic 会先找到它，占位自然作废。
    /// </summary>
    public static RelicData CreatePlaceholderRelic(string relicName, string description)
    {
        RelicData relic = ScriptableObject.CreateInstance<RelicData>();
        relic.relicName = relicName;
        relic.description = string.IsNullOrEmpty(description) ? "效果待策划补充" : description;
        relic.relicType = RelicType.事件掉落;
        relic.pool = RelicPool.通用;
        relic.requiresDesignDetails = true;
        relic.devNotes = "事件运行时占位遗物：请把真正的资产建到 ScriptableObjects/遗物/ 下，" +
                         "名称与事件文案完全一致即自动接管（占位只在本次运行有效）。";
        return relic;
    }

    private static void EnsureLoaded()
    {
        if (relics != null && potions != null) return;

        relics = new List<RelicData>();
        potions = new List<PotionData>();

#if UNITY_EDITOR
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:RelicData"))
        {
            RelicData relic = UnityEditor.AssetDatabase.LoadAssetAtPath<RelicData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (relic != null) relics.Add(relic);
        }
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:PotionData"))
        {
            PotionData potion = UnityEditor.AssetDatabase.LoadAssetAtPath<PotionData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (potion != null) potions.Add(potion);
        }
#endif

        // 打包后 AssetDatabase 不可用，退回 LootSystem 的掉落池（缺失的池子跳过）
        LootSystem loot = LootSystem.Instance;
        if (loot != null)
        {
            Merge(relics, loot.generalPool);
            Merge(relics, loot.elitePool);
            Merge(relics, loot.bossPool);
            Merge(potions, loot.commonPotions);
            Merge(potions, loot.rarePotions);
        }
    }

    /// <summary>把 src 里还没进 dst 的资产追加进去（去重，空项跳过）</summary>
    private static void Merge<T>(List<T> dst, List<T> src) where T : Object
    {
        if (src == null) return;
        foreach (T item in src)
            if (item != null && !dst.Contains(item)) dst.Add(item);
    }

    /// <summary>清空缓存（编辑器里改了资产名后，下一局重新扫）</summary>
    public static void Invalidate()
    {
        relics = null;
        potions = null;
    }
}
