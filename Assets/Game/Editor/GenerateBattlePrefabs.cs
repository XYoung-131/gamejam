using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the first set of battle prefabs from the project's existing data assets.
/// Generated sprites are deliberately simple placeholders and can be replaced later.
/// </summary>
public static class GenerateBattlePrefabs
{
    private const string Root = "Assets/Game";
    private const string Art = Root + "/Art/Placeholders";
    private const string Prefabs = Root + "/Prefabs";
    private const string Data = Root + "/ScriptableObjects";

    [MenuItem("Game/Generate Battle Prefabs")]
    public static void Generate()
    {
        EnsureFolder(Root, "Art");
        EnsureFolder(Root + "/Art", "Placeholders");
        EnsureFolder(Root, "Prefabs");
        EnsureFolder(Prefabs, "Board");
        EnsureFolder(Prefabs, "Units");
        EnsureFolder(Prefabs, "Obstacles");

        Sprite cellSprite = MakeSprite("HexCell", 256, new Color32(62, 78, 93, 255), Shape.Hex, 120);
        Sprite playerSprite = MakeSprite("Player", 128, new Color32(68, 181, 221, 255), Shape.Disc, 128);
        Sprite enemySprite = MakeSprite("Enemy", 128, new Color32(222, 114, 91, 255), Shape.Disc, 128);
        Sprite obstacleSprite = MakeSprite("Obstacle", 128, new Color32(173, 145, 98, 255), Shape.Block, 128);

        HexCell cell = CreateCell(cellSprite);
        CreateGrid(cell);
        CreatePlayer(playerSprite);
        CreateEnemies(enemySprite);
        CreateObstacles(obstacleSprite);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Battle prefabs generated in " + Prefabs);
    }

    private enum Shape { Hex, Disc, Block }

    private static Sprite MakeSprite(string name, int size, Color32 fill, Shape shape, int pixelsPerUnit)
    {
        string path = Art + "/" + name + ".png";
        if (!File.Exists(path))
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 edge = new Color32(25, 34, 43, 255);
            Color32 shine = new Color32(255, 255, 255, 95);
            float center = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x - center) / center;
                float ny = (y - center) / center;
                float radius = Mathf.Sqrt(nx * nx + ny * ny);
                bool inside;
                bool border;
                switch (shape)
                {
                    case Shape.Hex:
                        float h = Mathf.Abs(ny) + Mathf.Abs(nx) * 0.57735f;
                        inside = Mathf.Abs(nx) <= 0.93f && h <= 0.93f;
                        border = Mathf.Abs(nx) > 0.88f || h > 0.86f;
                        break;
                    case Shape.Disc:
                        inside = radius <= 0.81f;
                        border = radius > 0.70f;
                        break;
                    default:
                        float box = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                        inside = box <= 0.72f;
                        border = box > 0.62f;
                        break;
                }

                Color32 color = clear;
                if (inside)
                    color = border ? edge : fill;
                if (inside && !border && ny > 0.20f && nx < -0.15f)
                    color = Color32.Lerp(fill, shine, 0.22f);
                texture.SetPixel(x, y, color);
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static HexCell CreateCell(Sprite sprite)
    {
        var go = new GameObject("HexCell");
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 0;
        var collider = go.AddComponent<PolygonCollider2D>();
        collider.pathCount = 1;
        collider.SetPath(0, new[] {
            new Vector2(0.93f, 0), new Vector2(0.465f, 0.805f),
            new Vector2(-0.465f, 0.805f), new Vector2(-0.93f, 0),
            new Vector2(-0.465f, -0.805f), new Vector2(0.465f, -0.805f)
        });
        var cell = go.AddComponent<HexCell>();
        cell.spriteRenderer = renderer;
        cell.defaultColor = Color.white;
        cell.highlightColor = new Color(0.45f, 0.92f, 1f, 1f);
        Save(go, Prefabs + "/Board/HexCell.prefab");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/Board/HexCell.prefab");
        return prefab.GetComponent<HexCell>();
    }

    private static void CreateGrid(HexCell cell)
    {
        var go = new GameObject("BattleGrid");
        var grid = go.AddComponent<HexGrid>();
        grid.cellPrefab = cell;
        grid.gridRadius = 4;
        grid.hexSize = 1f;
        grid.orientation = HexOrientation.FlatTop;
        Save(go, Prefabs + "/Board/BattleGrid.prefab");
    }

    private static void CreatePlayer(Sprite sprite)
    {
        var go = new GameObject("Player_Template");
        AddUnitVisual(go, sprite, Color.white);
        go.AddComponent<PlayerUnit>();
        Save(go, Prefabs + "/Units/Player_Template.prefab");
    }

    private static void CreateEnemies(Sprite sprite)
    {
        string[] guids = AssetDatabase.FindAssets("t:UnitData", new[] { Data + "/单位" });
        Array.Sort(guids, StringComparer.Ordinal);
        foreach (string guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<UnitData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            string safeName = SafeName(data.name);
            var go = new GameObject("Enemy_" + safeName);
            Color tint = data.grade == UnitGrade.精英怪 ? new Color(1f, 0.78f, 0.68f) : Color.white;
            AddUnitVisual(go, sprite, tint);
            var enemy = go.AddComponent<EnemyUnit>();
            enemy.data = data;
            string path = Prefabs + "/Units/Enemy_" + safeName + ".prefab";
            Save(go, path);
            data.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (data.icon == null) data.icon = sprite;
            EditorUtility.SetDirty(data);
        }
    }

    private static void CreateObstacles(Sprite sprite)
    {
        string[] guids = AssetDatabase.FindAssets("t:ObstacleData", new[] { Data + "/障碍物" });
        Array.Sort(guids, StringComparer.Ordinal);
        foreach (string guid in guids)
        {
            var data = AssetDatabase.LoadAssetAtPath<ObstacleData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            string safeName = SafeName(data.name);
            var go = new GameObject("Obstacle_" + safeName);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 5;
            go.AddComponent<BoxCollider2D>();
            var obstacle = go.AddComponent<Obstacle>();
            obstacle.data = data;
            string path = Prefabs + "/Obstacles/Obstacle_" + safeName + ".prefab";
            Save(go, path);
            data.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (data.icon == null) data.icon = sprite;
            EditorUtility.SetDirty(data);
        }
    }

    private static void AddUnitVisual(GameObject go, Sprite sprite, Color tint)
    {
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = tint;
        renderer.sortingOrder = 10;
        var collider = go.AddComponent<CircleCollider2D>();
        collider.radius = 0.4f;
    }

    private static string SafeName(string name)
    {
        foreach (char character in Path.GetInvalidFileNameChars())
            name = name.Replace(character, '_');
        return name;
    }

    private static void Save(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        UnityEngine.Object.DestroyImmediate(go);
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }
}
