using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键搭建战斗场景（菜单：Game/搭建战斗场景）。
///
/// 做的事情：
///   1. 建立玩家 UnitData（玩家.asset，10 血，指向 Player_Template 预制体）；
///   2. 按关卡表生成 6 个关卡资产（ScriptableObjects/关卡/Level_1_1 ... Level_1_6），
///      每个关卡两个变种，布阵来自"聚光灯gamejam_关卡示意图"附件；
///   3. 把技能目录下的卡按 library 字段分成攻击/防御两个牌库；
///   4. 新建 Assets/Game/Scenes/Battle.unity：相机 + ConceptMap + GameManager +
///      TurnManager + LevelManager（已连好关卡和牌库），并加入 Build Settings。
///
/// 关卡示意图里出现了两个项目里还没有的障碍物，这里先用现有障碍替代：
///   惊叫礼物 → 炸药桶     石堆 → 柱子
/// </summary>
public static class BuildBattleScene
{
    private const string Root = "Assets/Game";
    private const string ScenePath = Root + "/Scenes/Battle.unity";
    private const string LevelFolder = Root + "/ScriptableObjects/关卡";
    private const string UnitFolder = Root + "/ScriptableObjects/单位";
    private const string ObstacleFolder = Root + "/ScriptableObjects/障碍物";
    private const string SkillFolder = Root + "/ScriptableObjects/技能";
    private const string PlayerDataPath = UnitFolder + "/玩家.asset";
    private const string PlayerPrefabPath = Root + "/Prefabs/Units/Player_Template.prefab";
    private const string MapPrefabPath = Root + "/Prefabs/Board/ConceptMap.prefab";

    // ============================================================
    //                          关卡表
    // ============================================================

    private class SpawnDef
    {
        public string unit;        // 单位资产名（Assets/Game/ScriptableObjects/单位/<名字>.asset）
        public string obstacle;    // 障碍资产名（和上面二选一）
        public int q;
        public int r;
    }

    private class VariantDef
    {
        public string name;
        public SpawnDef[] spawns;
    }

    private class LevelDef
    {
        public string name;
        public VariantDef[] variants;
    }

    private static SpawnDef U(string unit, int q, int r) => new SpawnDef { unit = unit, q = q, r = r };
    private static SpawnDef O(string obstacle, int q, int r) => new SpawnDef { obstacle = obstacle, q = q, r = r };

    /// <summary>玩家在所有变种里都站在棋盘正中 (0,0)</summary>
    private static readonly LevelDef[] Levels =
    {
        new LevelDef { name = "第一关", variants = new[]
        {
            new VariantDef { name = "变种1", spawns = new[]
            {
                O("柱子", 1, 1), O("炸药桶", -1, 0),
                U("神庙鼠", 2, -1), U("神庙鼠", -1, -1),
            }},
            new VariantDef { name = "变种2", spawns = new[]
            {
                U("神庙鼠", 0, 2), U("神庙鼠", 2, -2), U("神庙鼠", -2, 0),
            }},
        }},
        new LevelDef { name = "第二关", variants = new[]
        {
            new VariantDef { name = "变种1", spawns = new[]
            {
                U("飞砾虫", 0, 3), U("飞砾虫", 2, 1), U("飞砾虫", -2, -1),
                O("柱子", 0, 2), U("神庙鼠", 1, 0), O("一桶鼠鼠", 3, -1),
            }},
            new VariantDef { name = "变种2", spawns = new[]
            {
                O("炸药桶", -1, 2), U("飞砾虫", 3, 0), O("一桶鼠鼠", -3, 1),
                U("克勒妮之子", 2, -2),
            }},
        }},
        new LevelDef { name = "第三关", variants = new[]
        {
            new VariantDef { name = "变种1", spawns = new[]
            {
                U("飞砾虫", 0, 3), O("一桶鼠鼠", 2, 2),
                O("尖刺柱子", 0, 2), O("尖刺柱子", -2, 1), O("尖刺柱子", 2, -1), O("尖刺柱子", 0, -2),
                U("神庙鼠", -1, 0), U("神庙鼠", 1, -1), U("神庙鼠", 0, -1),
            }},
            new VariantDef { name = "变种2", spawns = new[]
            {
                U("飞砾虫", 3, 0), U("克勒妮之子", -1, 2),
                O("尖刺柱子", 2, 0), O("大炸药桶", -2, 1), O("一桶鼠鼠", 2, -3),
            }},
        }},
        new LevelDef { name = "第四关", variants = new[]
        {
            // 示意图上的"惊叫礼物"先用炸药桶替代
            new VariantDef { name = "变种1", spawns = new[]
            {
                O("炸药桶", -2, 2), O("炸药桶", 2, 0), U("飞砾虫", 0, -2),
            }},
            // 示意图上的"惊叫礼物"→炸药桶，"石堆"→柱子
            new VariantDef { name = "变种2", spawns = new[]
            {
                U("飞砾虫", 0, 3), O("炸药桶", -2, -1), O("炸药桶", 2, -3),
                O("柱子", 0, -2), O("一桶鼠鼠", 0, -3),
            }},
        }},
        new LevelDef { name = "第五关", variants = new[]
        {
            new VariantDef { name = "变种1", spawns = new[]
            {
                U("飞砾虫", 1, 2), O("炸药桶", -3, 3), O("炸药桶", 1, -3),
                O("尖刺柱子", -2, 2), O("尖刺柱子", 1, -2), O("一桶鼠鼠", 2, 0),
            }},
            new VariantDef { name = "变种2", spawns = new[]
            {
                O("炸药桶", -2, 2), U("克勒妮之子", 2, 0), U("神庙鼠", 2, -2),
            }},
        }},
        new LevelDef { name = "第六关", variants = new[]
        {
            new VariantDef { name = "变种1", spawns = new[]
            {
                O("炸药桶", -2, 3), O("炸药桶", 2, -3),
                O("一桶鼠鼠", 2, 1), O("一桶鼠鼠", -2, -1),
                O("大炸药桶", -2, 1), O("大炸药桶", 2, -1),
            }},
            new VariantDef { name = "变种2", spawns = new[]
            {
                O("柱子", 0, 2), O("炸药桶", 0, 3), O("炸药桶", 2, -2), O("炸药桶", -1, -2),
                U("神庙鼠", -2, 2), O("炸药桶", -1, 0),
            }},
        }},
    };

    // ============================================================
    //                          主流程
    // ============================================================

    [MenuItem("Game/搭建战斗场景 (Build Battle Scene)")]
    public static void Build()
    {
        EnsureFolder(Root + "/ScriptableObjects", "关卡");

        UnitData playerData = CreateOrUpdatePlayerData();
        List<LevelData> levels = CreateOrUpdateLevels();
        SplitCardPools(out List<CardData> attackPool, out List<CardData> defensePool);
        CreateScene(levels[0], playerData, attackPool, defensePool);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BuildBattleScene] 完成：{levels.Count} 个关卡资产、牌库 攻击 {attackPool.Count} 张 / " +
                  $"防御 {defensePool.Count} 张，场景 {ScenePath}");
    }

    // ============================================================
    //                       玩家数据
    // ============================================================

    private static UnitData CreateOrUpdatePlayerData()
    {
        UnitData data = AssetDatabase.LoadAssetAtPath<UnitData>(PlayerDataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<UnitData>();
            AssetDatabase.CreateAsset(data, PlayerDataPath);
        }

        data.unitName = "玩家";
        if (data.maxHealth <= 0) data.maxHealth = 10;
        data.grade = UnitGrade.小怪;
        if (data.prefab == null)
            data.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

        EditorUtility.SetDirty(data);
        return data;
    }

    // ============================================================
    //                       关卡资产
    // ============================================================

    private static List<LevelData> CreateOrUpdateLevels()
    {
        var result = new List<LevelData>();
        for (int i = 0; i < Levels.Length; i++)
            result.Add(CreateOrUpdateLevel(Levels[i], i + 1));
        return result;
    }

    private static LevelData CreateOrUpdateLevel(LevelDef def, int index)
    {
        string path = $"{LevelFolder}/Level_1_{index}.asset";
        LevelData asset = AssetDatabase.LoadAssetAtPath<LevelData>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.levelName = def.name;
        asset.variants = new List<LevelData.Variant>();

        foreach (VariantDef variantDef in def.variants)
        {
            var variant = new LevelData.Variant
            {
                variantName = variantDef.name,
                playerStart = new HexCoord(0, 0),
            };

            foreach (SpawnDef spawnDef in variantDef.spawns)
            {
                LevelData.Spawn spawn = BuildSpawn(spawnDef, def.name, variantDef.name);
                if (spawn != null)
                    variant.spawns.Add(spawn);
            }

            asset.variants.Add(variant);
        }

        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static LevelData.Spawn BuildSpawn(SpawnDef def, string levelName, string variantName)
    {
        if (!string.IsNullOrEmpty(def.unit))
        {
            var unit = AssetDatabase.LoadAssetAtPath<UnitData>($"{UnitFolder}/{def.unit}.asset");
            if (unit == null)
            {
                Debug.LogWarning($"[BuildBattleScene] {levelName}{variantName}：找不到单位 {def.unit}");
                return null;
            }

            return new LevelData.Spawn { unit = unit, coord = new HexCoord(def.q, def.r) };
        }

        var obstacle = AssetDatabase.LoadAssetAtPath<ObstacleData>($"{ObstacleFolder}/{def.obstacle}.asset");
        if (obstacle == null)
        {
            Debug.LogWarning($"[BuildBattleScene] {levelName}{variantName}：找不到障碍 {def.obstacle}");
            return null;
        }

        return new LevelData.Spawn { obstacle = obstacle, coord = new HexCoord(def.q, def.r) };
    }

    // ============================================================
    //                         牌库
    // ============================================================

    private static void SplitCardPools(out List<CardData> attack, out List<CardData> defense)
    {
        attack = new List<CardData>();
        defense = new List<CardData>();

        if (!AssetDatabase.IsValidFolder(SkillFolder))
            return;

        foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { SkillFolder }))
        {
            var card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid));
            if (card == null) continue;
            if (card.library == CardPool.攻击) attack.Add(card);
            else defense.Add(card);
        }
    }

    // ============================================================
    //                         场景
    // ============================================================

    private static void CreateScene(LevelData firstLevel, UnitData playerData,
                                    List<CardData> attackPool, List<CardData> defensePool)
    {
        var mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
        if (mapPrefab == null)
            throw new System.InvalidOperationException("找不到地图预制体：" + MapPrefabPath);

        // 批处理模式下当前是一个"未命名的空场景"，这种情况下不允许用 Additive 新建，
        // 所以直接用 Single 模式替换掉它（批处理里没有用户数据要保存）
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // --- 相机 ---
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 6.7f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.white;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<CameraController>();
        SceneManager.MoveGameObjectToScene(cameraObject, scene);

        // --- 地图（格子改由 LevelManager 在布阵前生成）---
        GameObject map = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab, scene);
        var grid = map.GetComponent<HexGrid>();
        if (grid != null)
            grid.generateOnStart = false;

        // --- 管理器 ---
        var managers = new GameObject("Managers");
        SceneManager.MoveGameObjectToScene(managers, scene);

        var gameManager = managers.AddComponent<GameManager>();
        gameManager.autoStartBattle = false;

        managers.AddComponent<TurnManager>();

        var levelManager = managers.AddComponent<LevelManager>();
        levelManager.level = firstLevel;
        levelManager.variantIndex = 0;
        levelManager.playerData = playerData;
        levelManager.attackPool = new List<CardData>(attackPool);
        levelManager.defensePool = new List<CardData>(defensePool);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);

        // --- 加进 Build Settings ---
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }

    // ============================================================
    //                       场景自检
    // ============================================================

    /// <summary>
    /// 检查战斗场景的引用和关卡数据有没有配错（格子、玩家、牌库、每个棋子的数据与坐标）。
    /// 可以带 -quit 在批处理里跑：-executeMethod BuildBattleScene.VerifyBattleScene
    /// </summary>
    [MenuItem("Game/检查战斗场景 (Verify Battle Scene)")]
    public static void VerifyBattleScene()
    {
        int problems = 0;

        // --- 1. 场景里的引用 ---
        EditorSceneManager.OpenScene(ScenePath);

        var levelManager = Object.FindObjectOfType<LevelManager>();
        var grid = Object.FindObjectOfType<HexGrid>();
        var camera = Object.FindObjectOfType<CameraController>();
        var gameManager = Object.FindObjectOfType<GameManager>();

        if (levelManager == null) { Debug.LogError("[Verify] 场景里没有 LevelManager"); problems++; }
        if (grid == null) { Debug.LogError("[Verify] 场景里没有 HexGrid（地图预制体）"); problems++; }
        if (camera == null) { Debug.LogError("[Verify] 相机上没有 CameraController"); problems++; }
        if (gameManager == null) { Debug.LogError("[Verify] 场景里没有 GameManager"); problems++; }
        if (Object.FindObjectOfType<TurnManager>() == null) { Debug.LogError("[Verify] 场景里没有 TurnManager"); problems++; }

        if (grid != null && grid.generateOnStart)
        {
            Debug.LogError("[Verify] 地图的 generateOnStart 应该关掉（格子交给 LevelManager 生成）");
            problems++;
        }

        if (levelManager != null)
        {
            if (levelManager.level == null) { Debug.LogError("[Verify] LevelManager 没指定关卡"); problems++; }
            if (levelManager.playerData == null) { Debug.LogError("[Verify] LevelManager 没指定玩家数据"); problems++; }
            if (levelManager.playerData != null && levelManager.playerData.prefab == null)
            {
                Debug.LogError("[Verify] 玩家数据里没有预制体"); problems++;
            }
            if (levelManager.attackPool.Count == 0) { Debug.LogError("[Verify] 攻击牌库是空的"); problems++; }
            if (levelManager.defensePool.Count == 0) { Debug.LogError("[Verify] 防御牌库是空的"); problems++; }

            Debug.Log($"[Verify] 场景已连：关卡 {levelManager.level?.levelName}（{levelManager.level?.variants.Count} 个变种）、" +
                      $"玩家 {levelManager.playerData?.unitName}、牌库 攻 {levelManager.attackPool.Count} / 防 {levelManager.defensePool.Count}");
        }

        // --- 2. 每个关卡的每个棋子 ---
        int spawnCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:LevelData", new[] { LevelFolder }))
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (LevelData.Variant variant in level.variants)
            {
                if (!IsOnMap(variant.playerStart))
                {
                    Debug.LogError($"[Verify] {level.levelName}{variant.variantName}：玩家出生格 {variant.playerStart} 不在棋盘上");
                    problems++;
                }

                if (variant.spawns.Count == 0)
                {
                    Debug.LogError($"[Verify] {level.levelName}{variant.variantName}：一个棋子都没有");
                    problems++;
                }

                foreach (LevelData.Spawn spawn in variant.spawns)
                {
                    spawnCount++;
                    bool hasUnit = spawn.unit != null;
                    bool hasObstacle = spawn.obstacle != null;
                    if (hasUnit == hasObstacle)
                    {
                        Debug.LogError($"[Verify] {level.levelName}{variant.variantName}：{spawn.coord} 上" +
                                       "既不是单位也不是障碍（或者两者都填了）");
                        problems++;
                    }
                    if (spawn.unit != null && spawn.unit.prefab == null)
                    {
                        Debug.LogError($"[Verify] {level.levelName}{variant.variantName}：单位 {spawn.unit.unitName} 没有预制体");
                        problems++;
                    }
                    if (spawn.obstacle != null && spawn.obstacle.prefab == null)
                    {
                        Debug.LogError($"[Verify] {level.levelName}{variant.variantName}：障碍 {spawn.obstacle.obstacleName} 没有预制体");
                        problems++;
                    }
                    if (!IsOnMap(spawn.coord))
                    {
                        Debug.LogError($"[Verify] {level.levelName}{variant.variantName}：{spawn.coord} 不在棋盘上");
                        problems++;
                    }
                }
            }
        }

        Debug.Log($"[Verify] 检查了 {spawnCount} 个棋子，问题 {problems} 个");
        if (problems > 0)
            EditorApplication.Exit(1);
    }

    /// <summary>坐标是否落在这张 63 格地图上（和 BuildConceptMap 里的形状一致）</summary>
    private static bool IsOnMap(HexCoord coord)
    {
        switch (coord.q)
        {
            case -5: return coord.r >= 1 && coord.r <= 4;
            case -4: return coord.r >= 0 && coord.r <= 4;
            case -3: return coord.r >= -1 && coord.r <= 4;
            case -2: return coord.r >= -2 && coord.r <= 4;
            case -1: return coord.r >= -2 && coord.r <= 3;
            case 0: return coord.r >= -3 && coord.r <= 3;
            case 1: return coord.r >= -3 && coord.r <= 2;
            case 2: return coord.r >= -4 && coord.r <= 2;
            case 3: return coord.r >= -4 && coord.r <= 1;
            case 4: return coord.r >= -4 && coord.r <= 0;
            case 5: return coord.r >= -4 && coord.r <= -1;
            default: return false;
        }
    }
}
