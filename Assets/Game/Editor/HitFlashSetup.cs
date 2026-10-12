using UnityEditor;
using UnityEngine;

/// <summary>
/// 受击闪红的编辑器辅助工具（只挂组件 / 建材质，不改动任何已有脚本）
///
/// 菜单：
///   Game/受击闪红/1. 创建闪红材质
///       —— 按 Shader Graph 生成 shader，在 Assets/Game/Shader/Materials/ 下建好 DamageFlash.mat
///   Game/受击闪红/2. 给所有预制体挂上组件
///       —— 遍历 Prefabs/Units、Prefabs/Obstacles，给还没有 HitFlash 的预制体挂上，
///          并把材质填进 Flash Material Template 字段
/// </summary>
public static class HitFlashSetup
{
    private const string GraphPath = "Assets/Game/Shader/VFX/DamageFlash/DamageFlash.shadergraph";
    private const string MaterialFolder = "Assets/Game/Shader/Materials";
    private const string MaterialPath = MaterialFolder + "/DamageFlash.mat";

    private static readonly string[] PrefabFolders =
    {
        "Assets/Game/Prefabs/Units",
        "Assets/Game/Prefabs/Obstacles",
    };

    // ============================================================
    //                      1. 创建材质
    // ============================================================

    [MenuItem("Game/受击闪红/1. 创建闪红材质")]
    public static void CreateFlashMaterial()
    {
        Shader shader = FindGeneratedShader(GraphPath);
        if (shader == null)
        {
            Debug.LogError($"[HitFlash] 没找到 {GraphPath} 生成的 shader。" +
                           "先在 Project 里点开这个 graph 并 Save Asset，然后重跑这个菜单。");
            return;
        }

        EnsureFolder("Assets/Game/Shader", "Materials");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        // 初始必须是"没闪"的状态，否则所有精灵一开局就是红的
        int amountId = Shader.PropertyToID("_FlashAmount");
        if (material.HasProperty(amountId))
            material.SetFloat(amountId, 0f);

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = material;
        Debug.Log($"[HitFlash] 闪红材质已就绪：{MaterialPath}");
    }

    // ============================================================
    //                  2. 给预制体挂上组件
    // ============================================================

    [MenuItem("Game/受击闪红/2. 给所有预制体挂上组件")]
    public static void AttachToPrefabs()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogWarning($"[HitFlash] 还没找到 {MaterialPath}，先跑一遍\"1. 创建闪红材质\"。" +
                             "组件照样会挂上：运行时会自动退回顶点色闪红。");
        }

        int added = 0;
        int total = 0;

        foreach (string folder in PrefabFolders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                total++;

                HitFlash flash = contents.GetComponent<HitFlash>();
                if (flash == null)
                {
                    flash = contents.AddComponent<HitFlash>();
                    added++;
                }

                if (flash.flashMaterialTemplate == null && material != null)
                    flash.flashMaterialTemplate = material;

                if (flash.target == null)
                    flash.target = contents.GetComponentInChildren<SpriteRenderer>();

                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[HitFlash] 扫描 {total} 个预制体，新挂上 {added} 个 HitFlash 组件。");
    }

    // ============================================================
    //                        工具方法
    // ============================================================

    /// <summary>
    /// 从 .shadergraph 的子资产里取出生成的 shader
    /// （Shader Graph 生成的 shader 是 graph 文件的子资产，用 path 直接 LoadAssetAtPath 取不到）
    /// </summary>
    private static Shader FindGeneratedShader(string graphPath)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(graphPath))
        {
            if (asset is Shader shader)
                return shader;
        }

        // 兜底：按 shader 名字找一次
        return Shader.Find("Shader Graphs/DamageFlash");
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }
}
