using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 一键准备投射物需要的资源（菜单：Game/特效/投射物：准备资源）：
///   1. ParticleSquare.png 的导入设置（按 Sprite 导入，PPU 44 → 贴图里那个方块正好 1 世界单位）
///   2. 生成粒子材质 Shader/Materials/Projectile/ProjectileParticle.mat（URP Particles/Unlit + 正方形贴图）
///   3. 如果已经连好 Shader Graph（Shader 名 = Shader Graphs/ProjectileBlock），生成核心材质 ProjectileCore.mat
///   4. 把 ProjectileSpawner 挂到 Battle 场景的 Managers 上并连好资源，方便在 Inspector 调参
///
/// 粒子材质用的就是同一张贴图，所以粒子是"正方形"；核心方块可换成你连的 Shader Graph。
/// </summary>
public static class ProjectileVFXSetup
{
    private const string SquarePath = "Assets/Game/Art/VFX/ParticleSquare.png";
    private const string MaterialsFolder = "Assets/Game/Shader/Materials/Projectile";
    private const string ParticleMatPath = MaterialsFolder + "/ProjectileParticle.mat";
    private const string CoreMatPath = MaterialsFolder + "/ProjectileCore.mat";
    private const string CoreShaderName = "Shader Graphs/ProjectileBlock";
    private const string ScenePath = "Assets/Game/Scenes/Battle.unity";
    private const string PrefabsVfxFolder = "Assets/Game/Prefabs/VFX";
    private const string ProjectilePrefabPath = PrefabsVfxFolder + "/Projectile.prefab";

    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    [MenuItem("Game/特效/投射物：准备资源")]
    public static void Prepare()
    {
        Sprite square = ConfigureSquareTexture();
        Material particleMaterial = CreateOrUpdateParticleMaterial(square);
        Material coreMaterial = TryCreateCoreMaterial();
        WireScene(square, particleMaterial, coreMaterial);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ProjectileVFXSetup] 完成：粒子材质 " + ParticleMatPath +
                  (coreMaterial != null ? "，核心材质 " + CoreMatPath : "，核心材质未生成（还没找到 Shader Graphs/ProjectileBlock）"));
    }

    /// <summary>
    /// 生成投射物预制体：把 Core / Cluster 子物体预先搭好并存成预制体，
    /// 这样粒子数量、半径、方块大小这些细节可以在 Inspector 里直接调，
    /// 生成器会自动用它（不生成也能跑，只是参数固定在代码默认值）。
    /// </summary>
    [MenuItem("Game/特效/投射物：生成预制体（可调参）")]
    public static void CreateProjectilePrefab()
    {
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
        Material particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(ParticleMatPath);
        Material coreMaterial = AssetDatabase.LoadAssetAtPath<Material>(CoreMatPath);

        var go = new GameObject("Projectile");
        var vfx = go.AddComponent<ProjectileVFX>();
        vfx.squareSprite = square;
        if (particleMaterial != null) vfx.particleMaterial = particleMaterial;
        if (coreMaterial != null) vfx.coreMaterial = coreMaterial;
        vfx.BuildVisuals();          // 在编辑器里把子物体搭出来，之后参数都会存进预制体

        EnsureFolder("Assets/Game/Prefabs", "VFX");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, ProjectilePrefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();

        // 顺手接到场景里的生成器上
        if (!Application.isBatchMode)
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var managers = GameObject.Find("Managers");
        var spawner = managers != null ? managers.GetComponent<ProjectileSpawner>() : null;
        if (spawner != null)
        {
            spawner.projectilePrefab = prefab;
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[ProjectileVFXSetup] 已生成预制体 {ProjectilePrefabPath}" +
                  (spawner != null ? "，并接到 Battle 场景的 Managers → ProjectileSpawner 上" : ""));
    }

    /// <summary>把贴图导入设置调成我要的样子：Sprite 类型、无 mipmap、不压缩、PPU 44</summary>
    private static Sprite ConfigureSquareTexture()
    {
        var importer = AssetImporter.GetAtPath(SquarePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[ProjectileVFXSetup] 找不到贴图 {SquarePath}");
            return null;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        // 贴图 64×64、中间的方块占 44×44：PPU 设 44 让"那个方块"正好是 1 个世界单位
        importer.spritePixelsPerUnit = 44f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 256;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
    }

    private static Material CreateOrUpdateParticleMaterial(Sprite square)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogError("[ProjectileVFXSetup] 找不到 URP 的 Particles/Unlit shader");
            return null;
        }

        EnsureFolder("Assets/Game/Shader", "Materials");
        EnsureFolder("Assets/Game/Shader/Materials", "Projectile");

        var material = AssetDatabase.LoadAssetAtPath<Material>(ParticleMatPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, ParticleMatPath);
        }
        else
        {
            material.shader = shader;
        }

        if (square != null && square.texture != null)
            material.SetTexture(BaseMapId, square.texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

        // 粒子要半透明加法观感：透明面 + 不写深度 + 排到透明队列
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;

        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>核心方块的材质：等你在编辑器里连好 Shader Graph 之后，再跑一次这个菜单就会自动生成</summary>
    private static Material TryCreateCoreMaterial()
    {
        var shader = Shader.Find(CoreShaderName);
        if (shader == null)
        {
            Debug.Log("[ProjectileVFXSetup] 还没找到 Shader Graph（Shader Graphs/ProjectileBlock）。" +
                      "连接要求：Sprite Unlit，属性名 _Glow / _GlowColor，连好后再跑一次本菜单。");
            return null;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(CoreMatPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, CoreMatPath);
            if (material.HasProperty("_Glow")) material.SetFloat("_Glow", 1f);
            if (material.HasProperty("_GlowColor")) material.SetColor("_GlowColor", new Color(0.75f, 0.95f, 1f, 1f));
        }
        else
        {
            material.shader = shader;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>把生成器挂到 Battle 场景的 Managers 上并连好资源</summary>
    private static void WireScene(Sprite square, Material particleMaterial, Material coreMaterial)
    {
        if (!System.IO.File.Exists(ScenePath))
        {
            Debug.LogWarning($"[ProjectileVFXSetup] 没有找到场景 {ScenePath}，跳过场景接线");
            return;
        }

        // 编辑器里先问用户保不保存当前场景；批处理模式直接开
        if (!Application.isBatchMode)
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var managers = GameObject.Find("Managers");
        if (managers == null)
        {
            Debug.LogWarning("[ProjectileVFXSetup] Battle 场景里没找到 Managers 物体，跳过接线");
            return;
        }

        var spawner = managers.GetComponent<ProjectileSpawner>();
        if (spawner == null)
            spawner = managers.AddComponent<ProjectileSpawner>();

        if (square != null) spawner.squareSprite = square;
        if (particleMaterial != null) spawner.particleMaterial = particleMaterial;
        if (coreMaterial != null) spawner.coreMaterial = coreMaterial;

        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ProjectileVFXSetup] 已把 ProjectileSpawner 挂到 Managers 上并连好资源");
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }
}
